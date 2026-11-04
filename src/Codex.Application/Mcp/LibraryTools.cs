using System.ComponentModel;
using Codex.Application.Features.Shell;
using Codex.Contracts.Books;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// A. Get my bearings: list_books (#20). A thin adapter over the Library query the shelf uses (IShellQueries), so it lists
// what the Library lists: the caller's own books and those shared with them (#80). Each book and series carries the
// caller's role there and its owner, read with the refs (book_access).
[McpServerToolType]
public sealed class LibraryTools(IShellQueries shell, McpRefs refs)
{
    private static readonly Dictionary<string, string> Sorts = new(StringComparer.Ordinal)
    {
        ["last_edited"] = LibrarySorts.LastEdited,
        ["title"] = LibrarySorts.Title,
        ["series_number"] = LibrarySorts.Series,
    };

    // Role: view, comment, write or owner. Owner: who owns it, { id, display_name }.
    public sealed record BookSummary(
        McpItem Item, string Title, int? SeriesNumber, string Status, int Words, int? TargetWords, string LastEdited,
        DateTimeOffset LastEditedAt, string? Role, BookOwnerDto? Owner);

    public sealed record SeriesGroup(
        McpItem Item, int BookCount, int PublishedCount, int Words, string LastEdited, IReadOnlyList<BookSummary> Books,
        string? Role, BookOwnerDto? Owner);

    public sealed record ListBooksResult(IReadOnlyList<SeriesGroup> Series, IReadOnlyList<BookSummary> Standalone) : McpResult;

    [McpServerTool(Name = "list_books", Title = "What am I working on?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The Library: series (with their totals) and standalone books, each with status, words / target, last edited and open_url. " +
                 "It lists the author's own books and the books shared with them; each book and series has `role` (view, comment, write or owner: " +
                 "what the author may do there) and `owner`.")]
    public async Task<ListBooksResult> ListBooksAsync(
        [Description("Only the books of this series (id or title).")] string? series = null,
        [Description("Only these statuses: planning, draft, writing, revising, published.")] string[]? status = null,
        [Description("last_edited (default, like the Library), title or series_number.")] string sort = "last_edited",
        CancellationToken cancellationToken = default)
    {
        if (!Sorts.TryGetValue(sort, out var librarySort))
        {
            throw McpToolException.Invalid("sort", "sort is last_edited, title or series_number.");
        }
        var unknown = status?.FirstOrDefault(s => !LibraryStatuses.All.Contains(s) || s == LibraryStatuses.Archived);
        if (unknown != null)
        {
            throw McpToolException.Invalid("status", $"\"{unknown}\" is not a book status: planning, draft, writing, revising or published.");
        }
        var seriesId = series == null ? (Guid?)null : (await refs.SeriesAsync(series, cancellationToken)).Id;
        var library = await shell.GetLibraryAsync(new LibraryQuery(null, librarySort, LibraryGroups.Series, null), cancellationToken);
        var access = await refs.AccessibleAllAsync(cancellationToken);
        BookSummary Summary(LibraryBookDto book) => new(
            new McpItem(book.Id, "book", book.Title, CodexLinks.Plan(book.Id)), book.Title, book.SeriesNumber, book.Status,
            book.WordCount, book.TargetWords, book.LastEditedLabel, book.LastEditedAt,
            access.GetValueOrDefault(book.Id)?.Role, access.GetValueOrDefault(book.Id)?.Owner);

        bool Keep(LibraryBookDto book) => status == null || status.Length == 0 || status.Contains(book.Status);
        var groups = library.Series
            .Where(s => seriesId == null || s.Id == seriesId)
            .Select(s => new SeriesGroup(
                new McpItem(s.Id, "series", s.Name, CodexLinks.Series(s.Id)), s.BookCount, s.PublishedCount, s.WordCount,
                s.LastEditedLabel, s.Books.Where(Keep).Select(Summary).ToList(),
                access.GetValueOrDefault(s.Id)?.Role, access.GetValueOrDefault(s.Id)?.Owner))
            .Where(g => g.Books.Count > 0 || status == null || status.Length == 0)
            .ToList();
        var standalone = seriesId == null ? library.Standalone.Where(Keep).Select(Summary).ToList() : [];
        return new ListBooksResult(groups, standalone);
    }
}
