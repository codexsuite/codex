using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.KnowledgeBase;
using Codex.Application.Features.Manuscript;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// E. Knowledge base, write side: upsert_kb_entry and link_kb_entry (#20 section E), adapters over the Plan screen's
// commands (IKbEntryCommands, IKbLinks, KbAdditionCommands, the prose store for mentions). Each call records one
// change set whose steps reverse it (archive a created entry, put an updated entry's fields back, unlink, restore
// the scene's prose, put a beat's context back, unrelate or relate the pairs that changed).
[McpServerToolType]
public sealed class KbWriteTools(
    McpRefs refs, McpScenes scenes, McpQueries queries, IKbQueries kbQueries, IKbEntryQueries entryQueries, IKbEntryCommands entryCommands,
    IKbLinks links, IKbDeleteCommands deletes, KbAdditionCommands additions, IProseStore prose, BlockCommands blocks, McpChangeSets changeSets,
    IKbRelatedCommands related, IBookAccess access)
{
    public sealed record UpsertKbEntryResult(
        bool Created, string Category, string Scope, IReadOnlyList<string> Aliases, bool AlwaysInContext) : McpWriteResult;

    // One link of link_kb_entry: `as` is scene, mention, addition, context or source; the other members are the ones
    // that kind takes (#20 section E).
    public sealed record KbLink(
        string As, string? Scene = null, ProseTools.Anchor? Anchor = null, Guid? Beat = null, JsonElement? At = null, string? Text = null,
        int? ExpectedVersion = null, string? Source = null);

    public sealed record LinkResult(int Index, string As, bool Unlink, bool Done, McpItem? Item, int? Version, McpError? Error);

    public sealed record LinkKbEntryResult(IReadOnlyList<LinkResult> Links, int Failed) : McpWriteResult;

    [McpServerTool(Name = "upsert_kb_entry", Title = "Remember this person, law, place or idea", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a Knowledge base entry (omit `entry`; needs book, name and category) or updates one (`entry`: id, name or alias). " +
                 "`category` is a category name; create_category adds one. scope \"series\" puts a new entry in the book's series, shared by every book in it. " +
                 "If the name or an alias already belongs to another entry in scope the call fails ambiguous unless allow_duplicate. `tags` replaces the set; `exclude_from_auto_context` keeps the entry out of the context chosen automatically for a draft. " +
                 "`related_add` and `related_remove` relate or unrelate other entries (id, name or alias, in the entry's scope; an entry outside it fails out_of_scope before anything is written); " +
                 "a pair already related, or not related, changes nothing and undo reverses only the pairs that changed.")]
    public async Task<UpsertKbEntryResult> UpsertKbEntryAsync(
        McpServer server,
        [Description("The book whose Knowledge base this is (id, title or \"<series>/<number>\"). Required to create.")] string? book = null,
        [Description("Update this entry; omit to create.")] string? entry = null,
        [Description("The entry's name.")] string? name = null,
        [Description("The category's name (People, Places, ...).")] string? category = null,
        [Description("\"book\" (default) or \"series\", when creating.")] string? scope = null,
        [Description("Other names the entry goes by; they drive the dotted-underline matches in Write. Replaces the set.")] IReadOnlyList<string>? aliases = null,
        [Description("One or two lines.")] string? summary = null,
        [Description("The entry's text.")] string? body = null,
        [Description("The category's field values, by field key.")] Dictionary<string, string>? fields = null,
        [Description("Always send the entry to the AI as context.")] bool? always_in_context = null,
        [Description("The entry's tags. Replaces the set.")] IReadOnlyList<string>? tags = null,
        [Description("Never pick the entry for a draft's context automatically.")] bool? exclude_from_auto_context = null,
        [Description("Create the category when no category has that name.")] bool create_category = false,
        [Description("Allow a name or alias another entry in scope already has.")] bool allow_duplicate = false,
        [Description("Entries to relate this one to (id, name or alias).")] IReadOnlyList<string>? related_add = null,
        [Description("Entries to unrelate from this one (id, name or alias).")] IReadOnlyList<string>? related_remove = null,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        McpBook? scopeBook = book == null ? null : await refs.BookAsync(book, cancellationToken);
        var existing = entry == null ? null : await refs.EntryAsync(entry, scopeBook, cancellationToken);
        var kbBook = scopeBook ?? (existing is { LinkBookId: { } linked } ? await refs.BookAsync(linked.ToString(), cancellationToken) : null)
                     ?? throw McpToolException.Invalid("book", "Give the book whose Knowledge base this is.");
        if (scope is not (null or "book" or "series"))
        {
            throw McpToolException.Invalid("scope", "scope is \"book\" or \"series\".");
        }

        var relatedAdd = await ResolveRelatedAsync(related_add, "related_add", kbBook, existing?.Id, cancellationToken);
        var relatedRemove = await ResolveRelatedAsync(related_remove, "related_remove", kbBook, existing?.Id, cancellationToken);
        if (relatedAdd.Select(r => r.Id).Intersect(relatedRemove.Select(r => r.Id)).Any())
        {
            throw McpToolException.Invalid("related_remove", "An entry can't be in both related_add and related_remove.");
        }

        var names = (name == null ? [] : new[] { name }).Concat(aliases ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (!allow_duplicate && names.Count > 0)
        {
            await RefuseDuplicatesAsync(kbBook, names, existing?.Id, cancellationToken);
        }

        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
            {
                throw McpToolException.Invalid("name", "Creating an entry needs name and category.");
            }
            var owner = scope == "series"
                ? await refs.BookAsync(kbBook.SeriesId?.ToString() ?? throw McpToolException.Invalid("scope", $"{kbBook.Title} is not in a series."), cancellationToken)
                : kbBook;
            // A series entry is shared by every book in the series, so it needs write on the series itself (#80).
            await access.RequireAsync(owner.Id, BookRole.Write, cancellationToken);
            var categoryDto = await CategoryAsync(owner, category, create_category, cancellationToken);
            var created = await entryCommands.CreateAsync(kbBook.Id, new CreateKbEntryRequest(categoryDto.Id, name.Trim()), cancellationToken);
            try
            {
                if (summary != null || body != null || fields != null || aliases != null || always_in_context != null
                    || tags != null || exclude_from_auto_context != null)
                {
                    created = await entryCommands.PatchAsync(created.Id,
                        new PatchKbEntryRequest(null, summary, body, fields, null, aliases, always_in_context, null, tags, exclude_from_auto_context), cancellationToken);
                }
                // The pairs go with the entry if undo archives it, so they record no steps of their own.
                foreach (var other in relatedAdd)
                {
                    await related.AddAsync(created.Id, other.Id, cancellationToken);
                }
            }
            catch
            {
                await deletes.DeleteAsync(created.Id, cancellationToken);
                throw;
            }
            var undo = await changeSets.RecordAsync(kbBook.Id, "upsert_kb_entry", $"Added Knowledge base entry {created.Name}", caller,
                [new McpUndoStep("archive_entry", created.Id)], cancellationToken);
            var item = new McpEntry(created.Id, created.Name, created.BookId, kbBook.Id).Item;
            return new UpsertKbEntryResult(true, created.CategoryName, created.BookId == kbBook.Id ? "book" : "series", created.Aliases, created.AlwaysInContext)
            {
                Item = item, UndoId = undo,
                SideEffects =
                [
                    $"{created.Name} is in {created.CategoryName}" + (created.BookId == kbBook.Id ? "." : ", shared by every book in the series."),
                    .. relatedAdd.Select(o => $"{created.Name} is related to {o.Name}."),
                ],
                Affected = [new McpAffected("kb_entry", created.Id, "created", null, created.Name), .. relatedAdd.Select(o => PairChange(created.Id, o, true))],
            };
        }

        // The entry's own book or series: a series entry needs write on the series (#80).
        await access.RequireAsync(BookItem.KbEntry, existing.Id, BookRole.Write, cancellationToken);
        var before = await entryQueries.GetAsync(existing.Id, cancellationToken);
        Guid? categoryId = null;
        if (category != null)
        {
            categoryId = (await CategoryAsync(await refs.BookAsync(before.BookId.ToString(), cancellationToken), category, create_category, cancellationToken)).Id;
        }
        // Related pairs first: a pair already in the wanted state changes nothing and records no step. If the entry's own
        // fields then fail validation, the pairs that did change are put back, so a failed call writes nothing.
        var held = before.Related.Select(r => r.Id).ToHashSet();
        var pairSteps = new List<McpUndoStep>();
        var pairChanges = new List<(McpEntry Other, bool Added)>();
        try
        {
            foreach (var other in relatedAdd.Where(o => !held.Contains(o.Id)))
            {
                await related.AddAsync(existing.Id, other.Id, cancellationToken);
                pairSteps.Add(new McpUndoStep("unrelate_entries", existing.Id, Parent: other.Id));
                pairChanges.Add((other, true));
            }
            foreach (var other in relatedRemove.Where(o => held.Contains(o.Id)))
            {
                await related.RemoveAsync(existing.Id, other.Id, cancellationToken);
                pairSteps.Add(new McpUndoStep("relate_entries", existing.Id, Parent: other.Id));
                pairChanges.Add((other, false));
            }
        }
        catch
        {
            await PutPairsBackAsync(existing.Id, pairChanges, cancellationToken);
            throw;
        }

        var hasPatch = name != null || summary != null || body != null || fields != null || aliases != null || always_in_context != null
                       || categoryId != null || tags != null || exclude_from_auto_context != null || (related_add is not { Count: > 0 } && related_remove is not { Count: > 0 });
        KbEntryDto after;
        var steps = new List<McpUndoStep>();
        try
        {
            after = hasPatch
                ? await entryCommands.PatchAsync(existing.Id,
                    new PatchKbEntryRequest(name, summary, body, fields, null, aliases, always_in_context, categoryId, tags, exclude_from_auto_context), cancellationToken)
                : await entryQueries.GetAsync(existing.Id, cancellationToken);
        }
        catch
        {
            await PutPairsBackAsync(existing.Id, pairChanges, cancellationToken);
            throw;
        }
        if (hasPatch)
        {
            steps.Add(new McpUndoStep("patch_entry", existing.Id, Title: before.Name, Summary: before.Summary, Body: before.Body,
                Texts: before.Aliases, Flag: before.AlwaysInContext, Fields: before.Fields,
                Tags: tags == null ? null : before.Tags, Exclude: exclude_from_auto_context == null ? null : before.ExcludeFromAutoContext,
                Now: new McpUndoState(Title: name, Summary: summary, Body: body, Flag: always_in_context, Texts: name == null ? aliases : null,
                    Tags: tags, Exclude: exclude_from_auto_context)));
        }
        steps.AddRange(pairSteps);
        var updateUndo = steps.Count == 0
            ? null
            : await changeSets.RecordAsync(kbBook.Id, "upsert_kb_entry", $"Updated Knowledge base entry {after.Name}", caller, steps, cancellationToken);
        return new UpsertKbEntryResult(false, after.CategoryName, after.BookId == kbBook.Id ? "book" : "series", after.Aliases, after.AlwaysInContext)
        {
            Item = new McpEntry(after.Id, after.Name, after.BookId, kbBook.Id).Item, UndoId = updateUndo,
            SideEffects =
            [
                UpdateSentence(after.Name, hasPatch, categoryId != null),
                .. pairChanges.Select(p => $"{after.Name} is {(p.Added ? "now related to" : "no longer related to")} {p.Other.Name}."),
            ],
            Affected = [new McpAffected("kb_entry", after.Id, "updated", null, null), .. pairChanges.Select(p => PairChange(after.Id, p.Other, p.Added))],
        };
    }

    private static string UpdateSentence(string name, bool patched, bool recategorised)
    {
        if (!patched)
        {
            return $"{name} is unchanged.";
        }
        return $"{name} is updated." + (recategorised ? " Its category is not put back by undo." : "");
    }

    private static McpAffected PairChange(Guid entryId, McpEntry other, bool added) =>
        new("kb_related", entryId, "related", added ? null : "related", added ? "related" : null, other.Name);

    // Resolves related_add / related_remove in the entry's scope before anything is written: an unknown name is
    // not_found, one outside the scope is out_of_scope, the entry itself is invalid.
    private async Task<IReadOnlyList<McpEntry>> ResolveRelatedAsync(
        IReadOnlyList<string>? references, string field, McpBook scope, Guid? self, CancellationToken ct)
    {
        var resolved = new List<McpEntry>();
        foreach (var reference in references ?? [])
        {
            var entry = await refs.EntryAsync(reference, scope, ct);
            if (entry.Id == self)
            {
                throw McpToolException.Invalid(field, "An entry can't be related to itself.");
            }
            if (resolved.All(r => r.Id != entry.Id))
            {
                resolved.Add(entry);
            }
        }
        return resolved;
    }

    private async Task PutPairsBackAsync(Guid entryId, List<(McpEntry Other, bool Added)> changes, CancellationToken ct)
    {
        foreach (var (other, added) in Enumerable.Reverse(changes))
        {
            if (added)
            {
                await related.RemoveAsync(entryId, other.Id, ct);
            }
            else
            {
                await related.AddAsync(entryId, other.Id, ct);
            }
        }
    }

    [McpServerTool(Name = "link_kb_entry", Title = "Connect this entry to the story", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Connects an entry to the story, one result per link. `as`: scene (the chip on the Plan card; `scene`), mention (turns the first plain use " +
                 "of `anchor.text` in `scene` into a mention chip), addition (`scene`, `at`, `text`, expected_version: what the reader learns from here on), " +
                 "context (`beat`: + Context). `unlink` removes scene, mention and context links. A link outside the book's scope fails that link with " +
                 "out_of_scope and the rest still run. Linking a source arrives with the References tools.")]
    public async Task<LinkKbEntryResult> LinkKbEntryAsync(
        McpServer server,
        [Description("The entry: id, name or alias.")] string entry,
        [Description("Links to add: { as, scene?, anchor?, beat?, at?, text?, expected_version? }.")] IReadOnlyList<KbLink>? links = null,
        [Description("Links to remove (same shape).")] IReadOnlyList<KbLink>? unlink = null,
        [Description("The book, when a scene is a display number or to scope a name.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        if (links is not { Count: > 0 } && unlink is not { Count: > 0 })
        {
            throw McpToolException.Invalid("links", "Give links to add or unlink.");
        }
        var caller = McpCaller.From(server);
        var resolved = await refs.EntryAsync(entry, book == null ? null : await refs.BookAsync(book, cancellationToken), cancellationToken);
        var steps = new List<McpUndoStep>();
        var results = new List<LinkResult>();
        var affected = new List<McpAffected>();
        Guid? bookId = null;
        var all = (links ?? []).Select(l => (Link: l, Unlink: false)).Concat((unlink ?? []).Select(l => (Link: l, Unlink: true))).ToList();
        for (var i = 0; i < all.Count; i++)
        {
            var (link, isUnlink) = all[i];
            try
            {
                var done = await ApplyAsync(server, resolved, link, isUnlink, book, steps, caller, cancellationToken);
                bookId ??= done.BookId;
                results.Add(new LinkResult(i, link.As, isUnlink, true, done.Item, done.Version, null));
                affected.Add(new McpAffected("kb_link", resolved.Id, link.As, isUnlink ? "linked" : null, isUnlink ? null : "linked", done.Item.Label));
            }
            catch (Exception e) when (e is McpToolException or ApiException)
            {
                var error = e is McpToolException m
                    ? new McpError(m.Code, m.Message, m.Candidates, m.Field, m.Rule, m.Scope, m.OpenUrl)
                    : new McpError(McpErrorCodes.FromApiCode(((ApiException)e).Code), e.Message, null, null, null);
                results.Add(new LinkResult(i, link.As, isUnlink, false, null, null, error));
            }
        }

        var failed = results.Count(r => !r.Done);
        var undoId = steps.Count == 0 || bookId == null
            ? null
            : await changeSets.RecordAsync(bookId.Value, "link_kb_entry", $"Linked {resolved.Name}", caller, steps, cancellationToken);
        return new LinkKbEntryResult(results, failed)
        {
            Item = resolved.Item, UndoId = undoId, Affected = affected,
            SideEffects = results.Where(r => r.Done).Select(r => $"{resolved.Name}: {(r.Unlink ? "removed" : "added")} {r.As} link on {r.Item!.Label}.").ToList(),
        };
    }

    private sealed record Done(McpItem Item, int? Version, Guid BookId);

    private async Task<Done> ApplyAsync(
        McpServer server, McpEntry entry, KbLink link, bool unlink, string? book, List<McpUndoStep> steps, McpCaller caller, CancellationToken ct)
    {
        switch (link.As)
        {
            case "scene":
            {
                var target = await SceneAsync(server, entry, link, book, ct);
                var sceneId = target.Scene.Dto.Id;
                var linked = await queries.SceneLinksEntryAsync(sceneId, entry.Id, ct);
                if (unlink && linked)
                {
                    await links.UnlinkSceneAsync(sceneId, entry.Id, ct);
                    steps.Insert(0, new McpUndoStep("link_scene", entry.Id, Parent: sceneId));
                }
                else if (!unlink && !linked)
                {
                    await links.LinkSceneAsync(sceneId, entry.Id, ct);
                    steps.Insert(0, new McpUndoStep("unlink_scene", entry.Id, Parent: sceneId));
                }
                return new Done(target.Item, null, target.Outline.Book.Id);
            }
            case "mention":
            {
                var target = await SceneAsync(server, entry, link, book, ct);
                var text = link.Anchor?.Text;
                if (!unlink && string.IsNullOrWhiteSpace(text))
                {
                    throw McpToolException.Invalid("anchor", "A mention link needs anchor.text: the plain text to turn into a mention.");
                }
                if (link.Anchor?.Occurrence is > 1)
                {
                    throw McpToolException.Invalid("anchor", "Only the first plain use of the text can become a mention.");
                }
                var version = await EditProseAsync(target, caller, steps, body => unlink
                    ? ProseDocument.Parse(body).ReplaceMentionWithText(entry.Id).ToString()
                    : ProseDocument.Parse(body).WrapMention(text!, entry.Id).ToString(), ct);
                return new Done(target.Item, version, target.Outline.Book.Id);
            }
            case "addition":
            {
                if (unlink)
                {
                    throw McpToolException.Invalid("unlink", "An addition is removed by editing its ::kb-addition line in Write.");
                }
                if (link.At is not { } at || string.IsNullOrWhiteSpace(link.Text) || link.ExpectedVersion is not { } expected)
                {
                    throw McpToolException.Invalid("at", "An addition link needs scene, at, text and expected_version.");
                }
                var target = await SceneAsync(server, entry, link, book, ct);
                var sceneId = target.Scene.Dto.Id;
                var current = await prose.GetAsync(sceneId, ct);
                if (current.Version != expected)
                {
                    throw ProseTools.Conflict(target, expected, current.Version);
                }
                var additionId = Guid.CreateVersion7();
                var next = ProseTools.InsertLine(current.Body, at, $"::kb-addition{{id=\"{additionId}\"}}");
                int version;
                try
                {
                    version = (await additions.InsertAsync(sceneId, new InsertKbAdditionRequest(additionId, entry.Id, link.Text.Trim(), next, current.Version), ct)).Version;
                }
                catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
                {
                    throw ProseTools.Conflict(target, expected, (await prose.GetAsync(sceneId, ct)).Version);
                }
                await changeSets.CutAgentRevisionAsync(sceneId, current.Body, caller, ct);
                KeepProseStep(steps, sceneId, current.Body, version);
                return new Done(target.Item, version, target.Outline.Book.Id);
            }
            case "context":
            {
                var beatId = link.Beat ?? throw McpToolException.Invalid("beat", "A context link needs `beat`: the beat's id.");
                var sceneId = await queries.SceneOfBeatAsync(beatId, ct) ?? throw McpToolException.NotFound($"No live beat {beatId}.");
                var target = await scenes.SceneAsync(server, null, sceneId.ToString(), ct);
                await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, ct);
                await refs.EntryAsync(entry.Id.ToString(), target.Outline.Book, ct);
                var beat = (await blocks.ListAsync(target.Outline.Book.Id, ct)).Beats.SingleOrDefault(b => b.Id == beatId)
                           ?? throw McpToolException.NotFound($"No live beat {beatId}.");
                var context = unlink ? beat.ContextEntryIds.Where(id => id != entry.Id).ToList() : beat.ContextEntryIds.Union([entry.Id]).ToList();
                var now = new McpUndoState(Labels: context);
                var held = steps.FindIndex(s => s.Op == "patch_beat" && s.Id == beatId);
                if (held >= 0)
                {
                    steps[held] = steps[held] with { Now = now };
                }
                else if (context.Count != beat.ContextEntryIds.Count)
                {
                    steps.Insert(0, new McpUndoStep("patch_beat", beatId, Body: beat.Body, Version: beat.TargetWordCount, Flag: beat.IsHidden,
                        Labels: beat.ContextEntryIds, Now: now));
                }
                await links.SetBeatContextAsync(beatId, context, ct);
                return new Done(target.Item, null, target.Outline.Book.Id);
            }
            case "source":
                // TODO: linking a source (reference.kb_entry_id, update_source's `entry`) arrives with the References tools; L9.3 replaces this.
                throw McpToolException.Invalid("as", "Linking a source is not supported yet; it arrives with the References tools.");
            default:
                throw McpToolException.Invalid("as", "`as` is scene, mention, addition, context or source.");
        }
    }

    // The link's scene, checked against the entry's scope: an entry from outside the scene's book (its own and its
    // series') fails that link with out_of_scope. A link writes to the scene's book, so it needs write there (#80).
    private async Task<McpScenes.Target> SceneAsync(McpServer server, McpEntry entry, KbLink link, string? book, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(link.Scene))
        {
            throw McpToolException.Invalid("scene", "This link needs `scene`.");
        }
        var target = await scenes.SceneAsync(server, book, link.Scene, ct);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, ct);
        await refs.EntryAsync(entry.Id.ToString(), target.Outline.Book, ct);
        return target;
    }

    private async Task<int> EditProseAsync(McpScenes.Target target, McpCaller caller, List<McpUndoStep> steps, Func<string, string> edit, CancellationToken ct)
    {
        var sceneId = target.Scene.Dto.Id;
        var current = await prose.GetAsync(sceneId, ct);
        string? before = null;
        int version;
        try
        {
            version = await prose.UpdateAsync(sceneId, body =>
            {
                before = body;
                return edit(body);
            }, current.Version, ProseTools.Origin, new ProseWriteOptions { InTransaction = changeSets.AgentRevision(sceneId, () => before, caller) }, ct);
        }
        catch (ProseEditException e)
        {
            throw McpToolException.Invalid("anchor", e.Message);
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
        {
            throw ProseTools.Conflict(target, current.Version, (await prose.GetAsync(sceneId, ct)).Version);
        }
        KeepProseStep(steps, sceneId, before!, version);
        return version;
    }

    // One prose step per scene: it holds the body from before the first edit and the version the last one left, so a
    // second link in the same scene still undoes in one step.
    private static void KeepProseStep(List<McpUndoStep> steps, Guid sceneId, string before, int version)
    {
        var at = steps.FindIndex(s => s.Op == "prose" && s.Id == sceneId);
        if (at >= 0)
        {
            steps[at] = steps[at] with { Version = version };
        }
        else
        {
            steps.Insert(0, new McpUndoStep("prose", sceneId, Body: before, Version: version));
        }
    }

    private async Task RefuseDuplicatesAsync(McpBook book, IReadOnlyList<string> names, Guid? except, CancellationToken ct)
    {
        var entries = (await kbQueries.GetBookKbAsync(book.Id, ct)).Entries;
        foreach (var name in names)
        {
            var clash = entries.Where(e => e.Id != except
                                           && (string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
                                               || e.Aliases.Any(a => string.Equals(a, name.Trim(), StringComparison.OrdinalIgnoreCase))))
                .Select(e => new McpEntry(e.Id, e.Name, book.Id, book.Id).Item).ToList();
            if (clash.Count > 0)
            {
                throw McpToolException.Ambiguous(
                    $"\"{name.Trim()}\" already belongs to {string.Join(", ", clash.Select(c => c.Label))} in {book.Title}'s Knowledge base. Update that entry, or pass allow_duplicate.",
                    clash);
            }
        }
    }

    // A category by name in the book's (or series') Knowledge base, created when asked.
    private async Task<KbCategoryDto> CategoryAsync(McpBook owner, string name, bool create, CancellationToken ct)
    {
        var categories = (await kbQueries.GetBookKbAsync(owner.Id, ct)).Categories;
        var found = categories.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            return found;
        }
        if (!create)
        {
            throw McpToolException.NotFound(
                $"{owner.Title}'s Knowledge base has no category \"{name}\" (it has {string.Join(", ", categories.Select(c => c.Name))}). Pass create_category to add it.");
        }
        return await entryCommands.CreateCategoryAsync(owner.Id, new CreateKbCategoryRequest(name.Trim(), "category-research"), ct);
    }
}
