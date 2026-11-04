using System.ComponentModel;
using System.Text.Json;
using Codex.Contracts;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, adding a source without citing it: add_source (#20 review 2, J9B's "add it for later"), over
// McpSourceAdds, the same identifier lookup and typed-in form cite_source takes. A new source is Not cited until a use names
// it. An identifier the book already has answers that source and writes nothing. Undo archives a source the call added.
[McpServerToolType]
public sealed class AddSourceTools(McpRefs refs, McpSources sources, McpSourceAdds adds, McpChangeSets changeSets, IBookAccess access)
{
    public sealed record AddSourceResult(string How, string Status, bool DryRun) : McpWriteResult;

    [McpServerTool(Name = "add_source", Title = "Add a source for later", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds a source to a book's References without citing it. `source` is `{ isbn | doi | url }` (looked up and matched against the " +
                 "book's sources, Not cited ones included) or `{ new: { kind, title, authors, year, publisher, edition, isbn, doi, url, accessed_on, " +
                 "licence, rights_holder } }`. When the book already has it, that source is returned (how: matched) and nothing is written; " +
                 "force_new adds a lookup result anyway, though never a second source with the same identifier. A new source is Not cited until " +
                 "cite_source uses it. dry_run looks the identifier up and reports without writing. Undo archives a source this call added.")]
    public async Task<AddSourceResult> AddSourceAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        [Description("{ isbn | doi | url } or { new: { … } }.")] JsonElement source,
        [Description("Add a looked-up source even when one matches the identifier.")] bool force_new = false,
        [Description("Report the result without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            throw McpToolException.Invalid("source", "source is { isbn | doi | url } or { new: { … } }. To cite a source already in the book, use cite_source.");
        }
        var owner = await refs.BookAsync(book, cancellationToken);
        await access.RequireAsync(owner.Id, BookRole.Write, cancellationToken);
        var caller = McpCaller.From(server);

        var resolved = await adds.ResolveAsync(owner, source, force_new, dry_run, caller, cancellationToken);
        if (resolved.Id is not { } id)
        {
            return new AddSourceResult(resolved.How, "not_cited", true)
            {
                Item = new McpItem(Guid.Empty, "source", "(a new source)", CodexLinks.References(owner.Id)),
                SideEffects = ["Would add a new source to References, Not cited; nothing was written."],
            };
        }

        var added = await sources.SourceAsync(id.ToString(), owner.Id.ToString(), cancellationToken);
        if (resolved.Created is not { } created)
        {
            return new AddSourceResult(resolved.How, added.Row.Status, dry_run)
            {
                Item = added.Item,
                SideEffects = [$"The book already has {added.Item.Label}; nothing was added."],
            };
        }

        var undoId = await changeSets.RecordAsync(owner.Id, "add_source", $"Added {added.Item.Label}", caller,
            [new McpUndoStep("archive_source", created)], cancellationToken);
        return new AddSourceResult(resolved.How, added.Row.Status, false)
        {
            Item = added.Item, UndoId = undoId,
            SideEffects = [$"{added.Item.Label} was added to References, Not cited until a passage cites it."],
            Affected = [new McpAffected("source", created, "created", null, added.Row.Status, added.Item.Label)],
        };
    }
}
