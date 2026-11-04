using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Sharing;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Sharing;

// Book settings -> Sharing (#80). Anyone who can open the book sees who else can, read-only and without other people's
// email addresses; only the owner looks people up and adds, changes or removes grants. A grant is on a book or on a
// whole series (the client sends the series id for the "Whole series" list); the owner never has a grant row.
// Every change publishes AccessChanged for each book whose effective role changed for that user (IAccessChanges).
[RegisterSingleton]
public sealed class SharingCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IAccessChanges changes, SharingLookupLimiter limiter)
{
    private const string BookNotFound = "That book doesn't exist.";
    private const string NoSuchUser = "No Codex user has that email.";
    private const string CheckViolation = "23514";

    private sealed record BookRow(Guid OwnerId, string OwnerName, string OwnerEmail, Guid? SeriesId, string? SeriesTitle);

    private sealed record GrantRow(Guid BookId, Guid UserId, string DisplayName, string Email, string Role);

    private sealed record TargetRow(Guid OwnerId, bool Archived);

    private sealed record UserRow(Guid Id, string DisplayName, string Email);

    public async Task<SharingDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var role = await access.RequireAsync(bookId, BookRole.View, cancellationToken, BookNotFound);
        var owner = role == BookRole.Owner;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var book = await connection.QuerySingleOrDefaultAsync<BookRow>(new CommandDefinition(
            """
            SELECT b.owner_id AS OwnerId, u.display_name AS OwnerName, u.email AS OwnerEmail,
                   b.series_id AS SeriesId, s.title AS SeriesTitle
            FROM book b
            JOIN app_user u ON u.id = b.owner_id
            LEFT JOIN book s ON s.id = b.series_id
            WHERE b.id = @bookId AND b.kind = 'book'
            """,
            new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound(BookNotFound);

        var grants = (await connection.QueryAsync<GrantRow>(new CommandDefinition(
            """
            SELECT g.book_id AS BookId, g.user_id AS UserId, u.display_name AS DisplayName, u.email AS Email, g.role AS Role
            FROM book_grant g JOIN app_user u ON u.id = g.user_id
            WHERE (g.book_id = @bookId OR g.book_id = @seriesId) AND g.archived_at IS NULL
            ORDER BY u.display_name, u.id
            """,
            new { bookId, seriesId = book.SeriesId }, cancellationToken: cancellationToken))).ToList();

        List<SharingGrantDto> On(Guid id) => grants.Where(g => g.BookId == id)
            .Select(g => new SharingGrantDto(new SharingUserDto(g.UserId, g.DisplayName, owner ? g.Email : null), g.Role))
            .ToList();

        return new SharingDto(
            new SharingUserDto(book.OwnerId, book.OwnerName, owner ? book.OwnerEmail : null),
            role.ToWire(),
            On(bookId),
            book.SeriesId is { } seriesId ? new SharingSeriesDto(seriesId, book.SeriesTitle!, On(seriesId)) : null);
    }

    // One exact address: case and surrounding whitespace are ignored, nothing else. A disabled user is no match.
    public async Task<SharingUserDto> LookupAsync(Guid bookId, SharingLookupRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, BookNotFound);
        limiter.Acquire(currentUser.Id);
        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            throw ApiException.Validation("Enter an email address.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            """
            SELECT id, display_name AS DisplayName, email FROM app_user
            WHERE lower(email) = lower(@email) AND disabled_at IS NULL
            """,
            new { email }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound(NoSuchUser);
        return new SharingUserDto(user.Id, user.DisplayName, user.Email);
    }

    // Adds or changes userId's grant on id (a book or a series). 409 for the owner or an archived book; 404 for an
    // unknown or disabled user; 400 for a role other than view, comment or write.
    public async Task<SharingGrantDto> SetAsync(Guid id, Guid userId, SetGrantRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(id, BookRole.Owner, cancellationToken, BookNotFound);
        var role = BookRoles.TryParse(request.Role);
        if (role is null or BookRole.Owner)
        {
            throw ApiException.Validation("Role must be view, comment or write.");
        }

        UserRow user;
        AccessSnapshot before;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            var target = await connection.QuerySingleAsync<TargetRow>(new CommandDefinition(
                "SELECT owner_id AS OwnerId, (archived_at IS NOT NULL) AS Archived FROM book WHERE id = @id",
                new { id }, cancellationToken: cancellationToken));
            if (target.OwnerId == userId)
            {
                throw ApiException.Conflict("The owner already has full access.");
            }
            if (target.Archived)
            {
                throw ApiException.Conflict("Restore the book before sharing it.");
            }
            user = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
                "SELECT id, display_name AS DisplayName, email FROM app_user WHERE id = @userId AND disabled_at IS NULL",
                new { userId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("No Codex user has that id.");

            before = await changes.CaptureAsync(await AffectedAsync(connection, id, cancellationToken), userId, cancellationToken);
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO book_grant (book_id, user_id, role, granted_by) VALUES (@id, @userId, @role, @by)
                    ON CONFLICT (book_id, user_id) DO UPDATE
                        SET role = EXCLUDED.role, granted_by = EXCLUDED.granted_by, updated_at = now(), archived_at = NULL
                    """,
                    new { id, userId, role = role.Value.ToWire(), by = currentUser.Id }, cancellationToken: cancellationToken));
            }
            catch (PostgresException e) when (e.SqlState == CheckViolation)
            {
                // book_grant_not_owner: the owner changed between the check and the insert.
                throw ApiException.Conflict("The owner already has full access.");
            }
        }

        await changes.PublishAsync(before, cancellationToken);
        return new SharingGrantDto(new SharingUserDto(user.Id, user.DisplayName, user.Email), role.Value.ToWire());
    }

    // Removes userId's grant on id (a book or a series). 404 when they have none there; a grant they hold through the
    // series (or on the book) is a separate row and stays.
    public async Task RemoveAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(id, BookRole.Owner, cancellationToken, BookNotFound);
        AccessSnapshot before;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            before = await changes.CaptureAsync(await AffectedAsync(connection, id, cancellationToken), userId, cancellationToken);
            var removed = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book_grant SET archived_at = now(), updated_at = now() WHERE book_id = @id AND user_id = @userId AND archived_at IS NULL",
                new { id, userId }, cancellationToken: cancellationToken));
            if (removed == 0)
            {
                throw ApiException.NotFound("That person has no access here to remove.");
            }
        }

        await changes.PublishAsync(before, cancellationToken);
    }

    // The rows whose access a grant on id can change: id itself, every book of a series, and a book's series row (a
    // grant on one book gives view on its series' shared items).
    private static async Task<Guid[]> AffectedAsync(NpgsqlConnection connection, Guid id, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT id FROM book WHERE id = @id OR series_id = @id
            UNION
            SELECT series_id FROM book WHERE id = @id AND series_id IS NOT NULL
            """,
            new { id }, cancellationToken: cancellationToken))).ToArray();
}
