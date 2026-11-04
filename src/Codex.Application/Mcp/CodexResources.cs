using System.ComponentModel;
using Codex.Application.Features.References;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// The resources of #20 Part 5: read-only context a client can pin. Each is the JSON of the tool that reads the same
// thing (M3: no data is resource-only), so the two never drift. A failure is a protocol error "<code>: <message>".
// Subscribing to them is McpSubscriptions (subscriptions/listen).
[McpServerResourceType]
public sealed class CodexResources(IServiceProvider services, McpRefs refs, McpQueries queries, IReferenceQueries references,
    ISourceQueries sources, IQuoteFlags flags)
{
    public sealed record SceneResource(McpItem Item, int Version, string Body);

    [McpServerResource(UriTemplate = "codex://books", Name = "Library", MimeType = "application/json")]
    [Description("The Library, as list_books returns it.")]
    public Task<string> BooksAsync(CancellationToken cancellationToken) =>
        Json(() => Tool<LibraryTools>().ListBooksAsync(cancellationToken: cancellationToken));

    [McpServerResource(UriTemplate = "codex://book/{id}/outline", Name = "Book outline", MimeType = "application/json")]
    [Description("A book's outline, as get_outline returns it.")]
    public Task<string> OutlineAsync(McpServer server, string id, CancellationToken cancellationToken) =>
        Json(() => Tool<BearingsTools>().GetOutlineAsync(server, id, cancellationToken: cancellationToken));

    [McpServerResource(UriTemplate = "codex://book/{id}/style-guide", Name = "Style guide", MimeType = "text/markdown")]
    [Description("The style guide every AI draft for the book is sent: the series' guide, then the book's.")]
    public Task<string> StyleGuideAsync(string id, CancellationToken cancellationToken) =>
        Guarded(async () => (await Tool<SettingsTools>().GetBookSettingsAsync(id, cancellationToken)).StyleGuide.Merged);

    [McpServerResource(UriTemplate = "codex://scene/{id}", Name = "Scene prose", MimeType = "application/json")]
    [Description("One scene's prose as stored (Markdown) with its version.")]
    public Task<string> SceneAsync(McpServer server, string id, CancellationToken cancellationToken) => Json(async () =>
    {
        var sceneId = Guid.TryParse(id, out var parsed) ? parsed : throw McpToolException.Invalid("id", "A scene resource takes a scene id.");
        var bookId = await queries.BookOfAsync(sceneId, null, cancellationToken) ?? throw McpToolException.NotFound($"No scene {id}.");
        var outline = await refs.OutlineAsync(await refs.BookAsync(bookId.ToString(), cancellationToken), McpRefs.SessionKey(server), cancellationToken);
        var item = outline.SceneById(sceneId)?.Item(bookId) ?? throw McpToolException.NotFound($"Scene {id} is not in the outline.");
        var prose = await queries.SceneProseAsync(sceneId, cancellationToken);
        return new SceneResource(item, prose.Version, prose.Body);
    });

    [McpServerResource(UriTemplate = "codex://kb/{id}", Name = "Knowledge base entry", MimeType = "application/json")]
    [Description("A Knowledge base entry and its additions in book order, as get_kb_entry returns it.")]
    public Task<string> KbEntryAsync(McpServer server, string id, CancellationToken cancellationToken) =>
        Json(() => Tool<KbTools>().GetKbEntryAsync(server, id, cancellationToken: cancellationToken));

    [McpServerResource(UriTemplate = "codex://book/{id}/references", Name = "References", MimeType = "application/json")]
    [Description("The book's References: the clearance summary, every source with its number, status and reason, and the unflagged quotes.")]
    public Task<string> ReferencesAsync(string id, CancellationToken cancellationToken) => Json(async () =>
    {
        var book = await refs.BookAsync(id, cancellationToken);
        return new
        {
            item = new McpItem(book.Id, "references", $"{book.Title} · References", CodexLinks.References(book.Id)),
            summary = await references.GetSummaryAsync(book.Id, cancellationToken),
            sources = await sources.GetSourcesAsync(book.Id, cancellationToken),
            unflagged_quotes = await flags.ListAsync(book.Id, cancellationToken),
        };
    });

    private T Tool<T>() => ActivatorUtilities.CreateInstance<T>(services);

    private static Task<string> Json<T>(Func<Task<T>> read) => Guarded(async () => McpJson.Serialize(await read()));

    private static async Task<string> Guarded(Func<Task<string>> read)
    {
        try
        {
            return await read();
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }
        catch (Contracts.Errors.ApiException e)
        {
            throw new McpException($"{McpErrorCodes.FromApiCode(e.Code)}: {e.Message}", e);
        }
    }
}
