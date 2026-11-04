using System.ComponentModel;
using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.KnowledgeBase;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// manage_kb_category (#78): create, rename (and recolour), reorder and delete Knowledge base categories, over the same
// commands as the Knowledge base screen (IKbCategoryCommands, plus CreateCategoryAsync). One change set per call.
// Delete is soft: the category gets archived_at, its archived entries stay in it (so their own undo still works once the
// category is back) and nothing is hard-deleted. Undo puts the name and colour back, puts the category back in its place,
// archives a created category (while it is still empty), or un-archives a deleted one (a live category with its name
// since is a conflict) and moves its entries back.
[McpServerToolType]
public sealed class KbCategoryTools(McpRefs refs, IKbQueries kbQueries, IKbEntryCommands entryCommands, IKbCategoryCommands categories, McpChangeSets changeSets)
{
    private const string DefaultColour = "category-research";

    public sealed record ManageKbCategoryResult(string Op, bool DryRun, string Category, int MovedEntries) : McpWriteResult;

    [McpServerTool(Name = "manage_kb_category", Title = "Create, rename, reorder or delete a Knowledge base category", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Manages one Knowledge base category of a book (or series), by name. `op`: create (`category` is the new name; `colour` optional), " +
                 "rename (`name` and/or `colour`), reorder (one of `before` or `after` another category's name, or `position` \"first\"/\"last\"; " +
                 "categories of the book and of its series order separately), delete. delete refuses a category with live entries (conflict, naming the count) " +
                 "unless `move_to` names a category of the same book or series: the entries move there, in order, and the category is deleted in the same call. " +
                 "A delete is soft: the name can be used again, and the category comes back with undo. `dry_run` reports without writing. Undo puts the name, colour or place back, removes a created " +
                 "category while it is empty, or brings a deleted one back (same id, place and Details fields) and moves its entries back.")]
    public async Task<ManageKbCategoryResult> ManageKbCategoryAsync(
        McpServer server,
        [Description("The book whose Knowledge base this is (id, title or \"<series>/<number>\").")] string book,
        [Description("create, rename, reorder or delete.")] string op,
        [Description("The category's name (for create: the new category's name).")] string category,
        [Description("rename: the new name.")] string? name = null,
        [Description("create, rename: a category colour token (category-blog, category-law, category-story, category-research, category-person).")] string? colour = null,
        [Description("reorder: put the category just before this category.")] string? before = null,
        [Description("reorder: put the category just after this category.")] string? after = null,
        [Description("reorder: \"first\" or \"last\".")] string? position = null,
        [Description("delete: move the live entries to this category first.")] string? move_to = null,
        [Description("Report what would change and write nothing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (op is not ("create" or "rename" or "reorder" or "delete"))
        {
            throw McpToolException.Invalid("op", "`op` is create, rename, reorder or delete.");
        }
        if (string.IsNullOrWhiteSpace(category))
        {
            throw McpToolException.Invalid("category", "Give the category's name.");
        }
        if (colour != null && !KbCategoryCommands.Colours.Contains(colour))
        {
            throw McpToolException.Invalid("colour", $"colour is one of {string.Join(", ", KbCategoryCommands.Colours.Order())}.");
        }
        var caller = McpCaller.From(server);
        var kbBook = await refs.BookAsync(book, cancellationToken);
        var kb = await kbQueries.GetBookKbAsync(kbBook.Id, cancellationToken);
        var context = new Context(kbBook, kb.Categories, caller, dry_run);
        return op switch
        {
            "create" => await CreateAsync(context, category.Trim(), colour, cancellationToken),
            "rename" => await RenameAsync(context, FindCategory(context, category), name, colour, cancellationToken),
            "reorder" => await ReorderAsync(context, FindCategory(context, category), before, after, position, cancellationToken),
            _ => await DeleteAsync(context, FindCategory(context, category), move_to, cancellationToken),
        };
    }

    private sealed record Context(McpBook Book, IReadOnlyList<KbCategoryDto> Categories, McpCaller Caller, bool DryRun);

    private static ManageKbCategoryResult Done(
        Context c, string op, KbCategoryDto category, string summary, IReadOnlyList<McpAffected> affected, string? undoId = null, int moved = 0) =>
        new(op, c.DryRun, category.Name, moved)
        {
            Item = new McpItem(category.Id, "kb_category", category.Name, CodexLinks.Plan(c.Book.Id)),
            UndoId = undoId, SideEffects = [summary], Affected = affected,
        };

    private async Task<ManageKbCategoryResult> CreateAsync(Context c, string name, string? colour, CancellationToken ct)
    {
        if (Named(c, name) != null)
        {
            throw new McpToolException(McpErrorCodes.Conflict, $"{c.Book.Title}'s Knowledge base already has a category named {name}.");
        }
        if (c.DryRun)
        {
            return Done(c, "create", new KbCategoryDto(Guid.Empty, name, null, colour, "", [], false, null), $"Would add the category {name}.", []);
        }
        var created = await entryCommands.CreateCategoryAsync(c.Book.Id, new CreateKbCategoryRequest(name, colour ?? DefaultColour), ct);
        var undo = await changeSets.RecordAsync(c.Book.Id, "manage_kb_category", $"Added Knowledge base category {created.Name}", c.Caller,
            [new McpUndoStep("delete_category", created.Id)], ct);
        return Done(c, "create", created, $"Added the category {created.Name}.", [new McpAffected("kb_category", created.Id, "created", null, created.Name)], undo);
    }

    private async Task<ManageKbCategoryResult> RenameAsync(Context c, KbCategoryDto cat, string? name, string? colour, CancellationToken ct)
    {
        var newName = name?.Trim();
        if (newName == null && colour == null)
        {
            throw McpToolException.Invalid("name", "rename needs `name`, `colour` or both.");
        }
        if (newName is { Length: 0 })
        {
            throw McpToolException.Invalid("name", "The new name cannot be blank.");
        }
        var nameChanges = newName != null && newName != cat.Name;
        var colourChanges = colour != null && colour != cat.Colour;
        if (nameChanges && Named(c, newName!) is { } clash && clash.Id != cat.Id)
        {
            throw new McpToolException(McpErrorCodes.Conflict, $"{c.Book.Title}'s Knowledge base already has a category named {newName}.");
        }
        var changes = new List<McpAffected>();
        if (nameChanges)
        {
            changes.Add(new McpAffected("kb_category", cat.Id, "name", cat.Name, newName));
        }
        if (colourChanges)
        {
            changes.Add(new McpAffected("kb_category", cat.Id, "colour", cat.Colour, colour));
        }
        var verb = c.DryRun ? "Would change" : "Changed";
        var summary = changes.Count == 0
            ? $"{cat.Name} already reads that; nothing changed."
            : $"{verb} {cat.Name}: {string.Join(", ", changes.Select(a => $"{a.Field} {a.From} → {a.To}"))}.";
        if (c.DryRun || changes.Count == 0)
        {
            return Done(c, "rename", cat, summary, changes);
        }
        var after = await categories.PatchAsync(cat.Id, new PatchKbCategoryRequest(nameChanges ? newName : null, colourChanges ? colour : null), ct);
        var step = new McpUndoStep("patch_category", cat.Id, Title: nameChanges ? cat.Name : null, Summary: colourChanges ? cat.Colour : null,
            Now: new McpUndoState(Title: nameChanges ? newName : null, Summary: colourChanges ? colour : null));
        var undo = await changeSets.RecordAsync(c.Book.Id, "manage_kb_category", $"Changed Knowledge base category {cat.Name}", c.Caller, [step], ct);
        return Done(c, "rename", after, summary, changes, undo);
    }

    private async Task<ManageKbCategoryResult> ReorderAsync(
        Context c, KbCategoryDto cat, string? before, string? after, string? position, CancellationToken ct)
    {
        if ((before != null ? 1 : 0) + (after != null ? 1 : 0) + (position != null ? 1 : 0) != 1)
        {
            throw McpToolException.Invalid("position", "reorder needs exactly one of `before`, `after` or `position` (\"first\" or \"last\").");
        }
        if (position is not (null or "first" or "last"))
        {
            throw McpToolException.Invalid("position", "`position` is \"first\" or \"last\".");
        }
        // The book's own categories and the series' keep separate orders, so only categories from the same side count.
        var order = c.Categories.Where(x => x.FromSeries == cat.FromSeries).ToList();
        var others = order.Where(x => x.Id != cat.Id).ToList();
        var oldPredecessor = PredecessorOf(order, cat);
        Guid? newPredecessor;
        if (position != null)
        {
            newPredecessor = position == "first" ? null : others.LastOrDefault()?.Id;
        }
        else
        {
            var anchorName = (before ?? after)!;
            var anchor = FindCategory(c, anchorName);
            if (anchor.Id == cat.Id)
            {
                throw McpToolException.Invalid(before != null ? "before" : "after", "A category cannot be placed next to itself.");
            }
            if (anchor.FromSeries != cat.FromSeries)
            {
                throw McpToolException.Invalid(before != null ? "before" : "after",
                    $"{anchor.Name} and {cat.Name} belong to different scopes (the book and its series); they are ordered separately.");
            }
            newPredecessor = after != null ? anchor.Id : PredecessorOf(others, anchor);
        }

        var side = before != null ? "before" : "after";
        var where = position ?? $"{side} {before ?? after}";
        if (newPredecessor == oldPredecessor)
        {
            return Done(c, "reorder", cat, $"{cat.Name} is already {where}; nothing changed.", []);
        }
        var affected = new McpAffected("kb_category", cat.Id, "position", null, where, cat.Name);
        if (c.DryRun)
        {
            return Done(c, "reorder", cat, $"Would move {cat.Name} {where}.", [affected]);
        }
        await categories.ReorderAsync(cat.Id, new MoveKbCategoryRequest(newPredecessor), ct);
        var step = new McpUndoStep("move_category", cat.Id, After: oldPredecessor, Now: new McpUndoState(Place: true, After: newPredecessor));
        var undo = await changeSets.RecordAsync(c.Book.Id, "manage_kb_category", $"Moved Knowledge base category {cat.Name}", c.Caller, [step], ct);
        return Done(c, "reorder", cat, $"Moved {cat.Name} {where}.", [affected], undo);
    }

    private async Task<ManageKbCategoryResult> DeleteAsync(Context c, KbCategoryDto cat, string? moveTo, CancellationToken ct)
    {
        var live = (await categories.ImpactAsync(cat.Id, ct)).Entries;
        KbCategoryDto? target = null;
        if (moveTo != null)
        {
            target = FindCategory(c, moveTo);
            if (target.Id == cat.Id)
            {
                throw McpToolException.Invalid("move_to", "move_to must be a different category.");
            }
            if (target.FromSeries != cat.FromSeries)
            {
                throw McpToolException.Invalid("move_to", $"{target.Name} belongs to a different scope (the book or its series) from {cat.Name}.");
            }
        }
        if (live > 0 && target == null)
        {
            throw new McpToolException(McpErrorCodes.Conflict,
                $"{cat.Name} has {live} live {(live == 1 ? "entry" : "entries")}. Move or delete them first, or pass move_to to move them to another category.");
        }
        var affected = new List<McpAffected> { new("kb_category", cat.Id, "deleted", cat.Name, null) };
        if (live > 0)
        {
            affected.Add(new McpAffected("kb_category", cat.Id, "entries_moved", live.ToString(), target!.Name, cat.Name));
        }
        var noun = live == 1 ? "entry" : "entries";
        var moveText = live > 0 ? $", moving its {live} {noun} to {target!.Name}" : "";
        if (c.DryRun)
        {
            return Done(c, "delete", cat, $"Would delete the category {cat.Name}{moveText}.", affected, moved: live);
        }
        var moved = await categories.DeleteMovingAsync(cat.Id, target?.Id, ct);
        var steps = new List<McpUndoStep> { new("restore_category", cat.Id) };
        steps.AddRange(moved.Select(entry => new McpUndoStep("set_entry_category", entry, Parent: cat.Id, Now: new McpUndoState(Parent: target!.Id))));
        var undo = await changeSets.RecordAsync(c.Book.Id, "manage_kb_category", $"Deleted Knowledge base category {cat.Name}", c.Caller, steps, ct);
        return Done(c, "delete", cat, $"Deleted the category {cat.Name}{moveText}.", affected, undo, moved.Count);
    }

    private static Guid? PredecessorOf(List<KbCategoryDto> order, KbCategoryDto category)
    {
        var at = order.IndexOf(category);
        return at > 0 ? order[at - 1].Id : null;
    }

    private static KbCategoryDto? Named(Context c, string name) =>
        c.Categories.FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static KbCategoryDto FindCategory(Context c, string name) =>
        Named(c, name) ?? throw McpToolException.NotFound(
            $"{c.Book.Title}'s Knowledge base has no category \"{name}\" (it has {string.Join(", ", c.Categories.Select(x => x.Name))}).");
}
