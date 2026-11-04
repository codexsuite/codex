using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print;

// The checks every placement of an image makes before it writes (a figure, an act or chapter opener): the asset is in a
// book or series the user can open (whoever uploaded it, #80), live, an image, and uploaded to the book or its series. The schema's scope triggers enforce the last one too;
// this gives the author a message instead of a constraint error.
public static class PlaceableAsset
{
    private sealed record AssetRow(string Kind, bool InScope);

    public static async Task RequireAsync(NpgsqlConnection connection, Guid bookId, Guid assetId, Guid userId, CancellationToken cancellationToken)
    {
        var asset = await connection.QuerySingleOrDefaultAsync<AssetRow>(new CommandDefinition(
            """
            SELECT a.kind,
                   EXISTS (SELECT 1 FROM book_scope bs WHERE bs.book_id = @bookId AND bs.scope_id = a.book_id) AS InScope
            FROM asset a
            WHERE a.id = @assetId AND a.archived_at IS NULL
              AND EXISTS (SELECT 1 FROM book_access x WHERE x.book_id = a.book_id AND x.user_id = @userId)
            """,
            new { bookId, assetId, userId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Image {assetId} was not found.");
        if (!asset.InScope)
        {
            throw ApiException.Validation("The image must be uploaded to this book or its series.");
        }
        if (asset.Kind != "image")
        {
            throw ApiException.Validation("Only an image can be placed.");
        }
    }
}
