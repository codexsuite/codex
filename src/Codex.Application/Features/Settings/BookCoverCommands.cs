using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Settings;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// The uploaded front cover (book.cover_asset_id). Without one, Library shows the generated cover in the book's palette.
// Setting or clearing it needs write (#80). The image may be any collaborator's upload to the book or its series: the
// asset is found through book_access, not its uploader (asset.owner_id).
[RegisterSingleton]
public sealed class BookCoverCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events)
{
    private const string BookNotFound = "That book doesn't exist.";

    private static readonly string[] Fields = ["coverUrl"];

    private sealed record AssetRow(string Kind, bool InScope);

    // 404 unknown book or unknown/archived asset; 400 no asset id, an asset outside the book and its series, or not an image.
    public async Task SetAsync(Guid bookId, SetBookCoverRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, BookNotFound);
        if (request.AssetId is not { } assetId)
        {
            throw ApiException.Validation("Choose an image for the cover.");
        }

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await RequireBookAsync(connection, bookId, cancellationToken);
            var asset = await connection.QuerySingleOrDefaultAsync<AssetRow>(new CommandDefinition(
                """
                SELECT a.kind,
                       EXISTS (SELECT 1 FROM book_scope bs WHERE bs.book_id = @bookId AND bs.scope_id = a.book_id) AS InScope
                FROM asset a
                LEFT JOIN book_access x ON x.book_id = a.book_id AND x.user_id = @user
                WHERE a.id = @assetId AND a.archived_at IS NULL
                  AND (x.role IS NOT NULL OR (a.book_id IS NULL AND a.owner_id = @user))
                """,
                new { bookId, assetId, user = currentUser.Id }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("That image doesn't exist.");
            if (!asset.InScope)
            {
                throw ApiException.Validation("The cover must be an image uploaded to this book or its series.");
            }
            if (asset.Kind != "image")
            {
                throw ApiException.Validation("The cover must be an image.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET cover_asset_id = @assetId WHERE id = @bookId",
                new { bookId, assetId }, cancellationToken: cancellationToken));
        }

        await events.PublishAsync(new BookMetaChanged(bookId, Fields));
    }

    // Back to the generated cover. 404 unknown book; clearing a book without an uploaded cover is a no-op.
    public async Task ClearAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, BookNotFound);
        int cleared;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await RequireBookAsync(connection, bookId, cancellationToken);
            cleared = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET cover_asset_id = NULL WHERE id = @bookId AND cover_asset_id IS NOT NULL",
                new { bookId }, cancellationToken: cancellationToken));
        }

        if (cleared > 0)
        {
            await events.PublishAsync(new BookMetaChanged(bookId, Fields));
        }
    }

    // A series id passes the access check; it is not a book.
    private static async Task RequireBookAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken)
    {
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')",
            new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound(BookNotFound);
        }
    }
}
