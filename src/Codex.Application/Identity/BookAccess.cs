using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Identity;

// IBookAccess over SQL: each item kind joins up to its book (or series) and reads the current user's role there from
// book_access (#80), which combines the owner, direct grants and series grants. Assets with no book and imports are
// checked on their own owner columns, because they can belong to the user without belonging to a book; their owner
// has the Owner role on them and nobody else has any.
[RegisterSingleton]
public sealed class BookAccess(NpgsqlDataSource dataSource, ICurrentUser currentUser) : IBookAccess
{
    private sealed record Access(Guid? BookId, string Role);

    private static readonly Dictionary<BookItem, string> Sql = new()
    {
        [BookItem.Book] = "SELECT a.book_id AS BookId, a.role AS Role FROM book_access a WHERE a.book_id = @id AND a.user_id = @user",
        [BookItem.Act] = InBook("act"),
        [BookItem.Chapter] = InBook("chapter"),
        [BookItem.Scene] = InBook("scene"),
        [BookItem.Beat] = InScene("beat"),
        [BookItem.Figure] = InScene("figure"),
        [BookItem.KbCategory] = InBook("kb_category"),
        [BookItem.KbEntry] = InBook("kb_entry"),
        [BookItem.KbAddition] = InScene("kb_progression"),
        [BookItem.Label] = InBook("label"),
        [BookItem.Source] = InBook("reference"),
        [BookItem.ReferenceUse] = InBook("reference_use"),
        [BookItem.QuoteFlag] = InBook("quote_flag"),
        [BookItem.PermissionRequest] =
            """
            SELECT r.book_id AS BookId, a.role AS Role FROM permission_request x
            JOIN reference r ON r.id = x.reference_id
            JOIN book_access a ON a.book_id = r.book_id AND a.user_id = @user
            WHERE x.id = @id
            """,
        [BookItem.Asset] =
            """
            SELECT x.book_id AS BookId, coalesce(a.role, 'owner') AS Role FROM asset x
            LEFT JOIN book_access a ON a.book_id = x.book_id AND a.user_id = @user
            WHERE x.id = @id AND (a.role IS NOT NULL OR (x.book_id IS NULL AND x.owner_id = @user))
            """,
        [BookItem.Generation] = InBook("ai_generation"),
        [BookItem.Import] = "SELECT i.book_id AS BookId, 'owner' AS Role FROM book_import i WHERE i.id = @id AND i.created_by = @user",
        [BookItem.ExportJob] = InBook("export_job"),
    };

    public async Task<BookRole> RequireAsync(
        Guid bookId, BookRole minimum, CancellationToken cancellationToken = default, string? notFound = null)
        => Check(BookItem.Book, bookId, minimum, await FindAsync(BookItem.Book, bookId, cancellationToken), notFound).Role;

    public async Task<Guid?> RequireAsync(
        BookItem item, Guid id, BookRole minimum, CancellationToken cancellationToken = default, string? notFound = null)
        => Check(item, id, minimum, await FindAsync(item, id, cancellationToken), notFound).BookId;

    public async Task<BookRole?> RoleAsync(Guid bookId, CancellationToken cancellationToken = default)
        => await FindAsync(BookItem.Book, bookId, cancellationToken) is { } found ? BookRoles.Parse(found.Role) : null;

    private async Task<Access?> FindAsync(BookItem item, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Access>(new CommandDefinition(
            Sql[item], new { id, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    private static (Guid? BookId, BookRole Role) Check(BookItem item, Guid id, BookRole minimum, Access? found, string? notFound)
    {
        if (found is null)
            throw ApiException.NotFound(notFound ?? $"{Describe(item)} {id} was not found.");
        var role = BookRoles.Parse(found.Role);
        return role >= minimum ? (found.BookId, role) : throw ApiException.Forbidden(Refusal(role, minimum));
    }

    // The sentence a user below the needed role reads.
    private static string Refusal(BookRole role, BookRole minimum) => minimum switch
    {
        BookRole.Owner => "Only the book's owner can do that.",
        _ when role == BookRole.Comment => "This book is shared with you to comment on, not to edit.",
        _ => "This book is shared with you to view only.",
    };

    // The table names are constants above, never input, so this builds no SQL from values.
    private static string InBook(string table) =>
        $"""
         SELECT x.book_id AS BookId, a.role AS Role FROM {table} x
         JOIN book_access a ON a.book_id = x.book_id AND a.user_id = @user
         WHERE x.id = @id
         """;

    private static string InScene(string table) =>
        $"""
         SELECT s.book_id AS BookId, a.role AS Role FROM {table} x
         JOIN scene s ON s.id = x.scene_id
         JOIN book_access a ON a.book_id = s.book_id AND a.user_id = @user
         WHERE x.id = @id
         """;

    private static string Describe(BookItem item) => item switch
    {
        BookItem.KbCategory => "Knowledge base category",
        BookItem.KbEntry => "Knowledge base entry",
        BookItem.KbAddition => "Knowledge base addition",
        BookItem.ReferenceUse => "Reference use",
        BookItem.QuoteFlag => "Quote flag",
        BookItem.PermissionRequest => "Permission request",
        BookItem.ExportJob => "Job",
        _ => item.ToString(),
    };
}
