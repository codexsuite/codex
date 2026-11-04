using System.ComponentModel;
using Codex.Application.Features.Library;
using Codex.Application.Features.Settings;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.Contracts.Settings;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Shape the Library's series: create_series and update_series (#73), adapters over the Library lane's series commands.
// Books come in through SeriesMoveCommands, the path update_book uses, so they follow its confirm and dry_run rules.
// A series made empty is undone by archiving it; a call that moved books cannot be undone (the move is not reversible).
[McpServerToolType]
public sealed class SeriesTools(
    McpRefs refs, SeriesLifecycleCommands lifecycle, SeriesCommands series, SeriesMoveCommands moves, McpUndo undo, McpChangeSets changeSets)
{
    public sealed record SeriesResult(bool DryRun, string Name, string? Description, IReadOnlyList<McpItem> Books) : McpWriteResult;

    [McpServerTool(Name = "create_series", Title = "Start a new series", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a series owned by you: `name`, optional `description` and `author_name`, and optional `books` (refs: id, title or \"<series>/<number>\") " +
                 "to move in, in order, as books 1, 2, ... of it. Moving books is a series move, as in update_book: a book that is in another series gets that " +
                 "series' Knowledge base copied onto it, so it fails needs_confirmation, writing nothing, until you call again with confirm after a dry_run. " +
                 "Returns the series with `affected` and `undo_id`. Undo archives a series that is still empty; a call that also moved books cannot be undone.")]
    public async Task<SeriesResult> CreateSeriesAsync(
        McpServer server,
        [Description("The series' name.")] string name,
        [Description("What the series is about.")] string? description = null,
        [Description("The author's name.")] string? author_name = null,
        [Description("Books to move in, in order (they become 1, 2, ...). Each must be a book, not a series; it can be standalone or in another series.")] IReadOnlyList<string>? books = null,
        [Description("Confirms moving books whose dry_run you have read.")] bool confirm = false,
        [Description("Report what would change and write nothing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw McpToolException.Invalid("name", "A series needs a name.");
        }
        var members = new List<McpBook>();
        foreach (var reference in books ?? [])
        {
            var book = await refs.BookAsync(reference, cancellationToken);
            if (book.IsSeries)
            {
                throw McpToolException.Invalid("books", $"{book.Title} is a series; books holds books.");
            }
            if (members.Any(m => m.Id == book.Id))
            {
                throw McpToolException.Invalid("books", $"{book.Title} is listed twice.");
            }
            members.Add(book);
        }

        var trimmed = name.Trim();
        var changes = new List<string> { $"Create the series {trimmed}." };
        var affected = new List<McpAffected>();
        foreach (var (book, index) in members.Select((b, i) => (b, i)))
        {
            // A book leaving another series is planned by the move itself: the preview names the copy-down.
            if (book.SeriesId != null)
            {
                var leave = await moves.PreviewAsync(book.Id, new SeriesMoveRequest(null), cancellationToken);
                changes.AddRange(leave.Steps.Select(s => s.Text));
                changes.AddRange(leave.Warnings);
            }
            changes.Add($"Join {trimmed} as book {index + 1}: {book.Title}.");
            affected.Add(new McpAffected("book", book.Id, "series", book.SeriesTitle, trimmed));
        }

        if (dry_run)
        {
            return new SeriesResult(true, trimmed, description, members.Select(b => b.Item).ToList())
            {
                SideEffects = changes, Affected = affected,
            };
        }
        if (members.Count > 0 && !confirm)
        {
            throw new McpToolException(McpErrorCodes.NeedsConfirmation,
                "Moving books into a series copies the Knowledge base of any series they leave onto them and cannot be undone. Nothing was written. " +
                $"It would: {string.Join(" ", changes)} Call again with confirm: true to go ahead.");
        }

        var created = await lifecycle.CreateAsync(new CreateSeriesRequest(trimmed, [], description, author_name), cancellationToken);
        try
        {
            foreach (var book in members)
            {
                await moves.MoveAsync(book.Id, new SeriesMoveRequest(created.Id), cancellationToken);
            }
        }
        catch when (members.Count > 0)
        {
            // Books already moved stay (a move is not reversible); only an untouched series is taken away again.
            try
            {
                await series.ArchiveAsync(created.Id, CancellationToken.None);
            }
            catch (ApiException)
            {
                // It has books now: leave it for the agent to finish or tidy.
            }
            throw;
        }

        var steps = members.Count == 0
            ? new List<McpUndoStep> { new("archive_series", created.Id) }
            : [new McpUndoStep("unsupported", created.Id, Title: "moving a book between series copies the series' Knowledge base onto it")];
        var undoId = await changeSets.RecordAsync(created.Id, "create_series", $"Created the series {trimmed}", caller, steps, cancellationToken);
        List<string> effects = [$"{trimmed} is a new series."];
        effects.AddRange(members.Select((b, i) => $"{b.Title} is book {i + 1} of {trimmed}."));
        return new SeriesResult(false, trimmed, string.IsNullOrWhiteSpace(description) ? null : description.Trim(), members.Select(b => b.Item).ToList())
        {
            Item = new McpItem(created.Id, "series", trimmed, CodexLinks.Series(created.Id)),
            UndoId = undoId, SideEffects = effects,
            Affected = [new McpAffected("series", created.Id, "created", null, trimmed), .. affected],
        };
    }

    [McpServerTool(Name = "update_series", Title = "Change a series", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Changes a series: `name`, `description` (empty clears it), `order` (every live book of the series, as refs, in the new order: they are renumbered 1, 2, ...) " +
                 "and `archive` (a soft delete: the series leaves the shelf and its books go with it, keeping their numbers; undo brings it all back). `dry_run` reports without writing. " +
                 "Undo puts the name, description, order and archive back while they still read what the call left.")]
    public async Task<SeriesResult> UpdateSeriesAsync(
        McpServer server,
        [Description("The series: id or title.")] string series_ref,
        [Description("A new name.")] string? name = null,
        [Description("A new description; empty clears it.")] string? description = null,
        [Description("Every live book of the series in the order you want (refs: id, title or \"<series>/<number>\").")] IReadOnlyList<string>? order = null,
        [Description("Archive the series with its books (undo restores it).")] bool archive = false,
        [Description("Report what would change and write nothing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        var target = await refs.SeriesAsync(series_ref, cancellationToken);
        if (name == null && description == null && order == null && !archive)
        {
            throw McpToolException.Invalid("name", "Nothing to change: give name, description, order or archive.");
        }
        if (name != null && string.IsNullOrWhiteSpace(name))
        {
            throw McpToolException.Invalid("name", "A series needs a name.");
        }
        var before = await series.GetAsync(target.Id, cancellationToken);
        var all = await refs.AccessibleAllAsync(cancellationToken);
        var live = all.Values.Where(b => b.SeriesId == target.Id && !b.Archived).OrderBy(b => b.SeriesNumber).ToList();
        var newOrder = new List<Guid>();
        foreach (var reference in order ?? [])
        {
            var book = await refs.BookAsync(reference, cancellationToken);
            if (book.SeriesId != target.Id || book.Archived)
            {
                throw McpToolException.Invalid("order", $"{book.Title} is not a live book of {target.Title}.");
            }
            newOrder.Add(book.Id);
        }
        if (order != null && (newOrder.Distinct().Count() != newOrder.Count || !newOrder.ToHashSet().SetEquals(live.Select(b => b.Id))))
        {
            throw McpToolException.Invalid("order", $"order must list every live book of {target.Title} exactly once ({live.Count}).");
        }

        var changes = new List<string>();
        var affected = new List<McpAffected>();
        void Note(string field, string? from, string? to)
        {
            changes.Add($"{field}: {from ?? "(none)"} -> {to ?? "(none)"}");
            affected.Add(new McpAffected("series", target.Id, field, from, to));
        }
        var newName = name?.Trim();
        if (newName != null) Note("name", before.Name, newName);
        if (description != null) Note("description", before.Description, string.IsNullOrWhiteSpace(description) ? null : description.Trim());
        if (order != null && !newOrder.SequenceEqual(live.Select(b => b.Id)))
        {
            Note("order", string.Join(", ", live.Select(b => b.Title)), string.Join(", ", newOrder.Select(id => all[id].Title)));
        }
        if (archive) Note("archived", "no", "yes");

        var item = new McpItem(target.Id, "series", newName ?? before.Name, CodexLinks.Series(target.Id));
        var books = live.Select(b => b.Item).ToList();
        if (dry_run)
        {
            return new SeriesResult(true, newName ?? before.Name, description == null ? before.Description : description.Trim(), books) { Item = item, SideEffects = changes, Affected = affected };
        }

        var steps = new List<McpUndoStep>();
        try
        {
            if (newName != null || description != null)
            {
                var after = await series.PatchAsync(target.Id, new PatchSeriesRequest(newName, description), cancellationToken);
                steps.Insert(0, new McpUndoStep("patch_series", target.Id, Title: newName == null ? null : before.Name, Description: description == null ? null : before.Description ?? "",
                    Now: new McpUndoState(Title: newName == null ? null : after.Name, Summary: description == null ? null : after.Description ?? "")));
            }
            if (order != null && !newOrder.SequenceEqual(live.Select(b => b.Id)))
            {
                await series.SetOrderAsync(target.Id, new SetSeriesOrderRequest(newOrder), cancellationToken);
                steps.Insert(0, new McpUndoStep("set_series_order", target.Id, Labels: live.Select(b => b.Id).ToList(), Now: new McpUndoState(Labels: newOrder)));
            }
            if (archive)
            {
                await series.ArchiveAsync(target.Id, cancellationToken);
                steps.Insert(0, new McpUndoStep("restore_series", target.Id));
            }
        }
        catch
        {
            await undo.RunAsync(steps, CancellationToken.None);
            throw;
        }

        var undoId = await changeSets.RecordAsync(target.Id, "update_series", $"Changed the series {target.Title}", caller, steps, cancellationToken);
        var now = await series.GetAsync(target.Id, cancellationToken);
        return new SeriesResult(false, now.Name, now.Description, order == null ? books : newOrder.Select(id => all[id].Item).ToList())
        {
            Item = item, UndoId = undoId, SideEffects = changes, Affected = affected,
        };
    }
}
