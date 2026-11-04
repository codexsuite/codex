using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print;

// The opener art of an act (a part opener) or a chapter: act.opener_asset_id / chapter.opener_asset_id and the caption
// beside it (#13, #65). The template draws the page; ManuscriptAssembler hands it the image.
[RegisterSingleton]
public sealed class OpenerCommands(NpgsqlDataSource dataSource, IBookAccess access, ICurrentUser currentUser, IBookEvents events)
{
    private sealed record OpenerRow(Guid? AssetId, string? Caption);

    // Sets or (AssetId null) clears the opener. 404 unknown or archived act or chapter, or an unknown image; 400 an
    // image outside the book and its series.
    public Task<OpenerDto> SetActAsync(Guid actId, SetOpenerRequest request, CancellationToken cancellationToken)
        => SetAsync(BookItem.Act, actId, request, cancellationToken);

    public Task<OpenerDto> SetChapterAsync(Guid chapterId, SetOpenerRequest request, CancellationToken cancellationToken)
        => SetAsync(BookItem.Chapter, chapterId, request, cancellationToken);

    // The act's or chapter's opener as it is now. 404 unknown or archived.
    public async Task<OpenerDto> GetAsync(BookItem kind, Guid id, CancellationToken cancellationToken)
    {
        await access.RequireAsync(kind, id, BookRole.View, cancellationToken, $"{kind} not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<OpenerRow>(new CommandDefinition(
            kind == BookItem.Act
                ? "SELECT opener_asset_id AS AssetId, opener_caption AS Caption FROM act WHERE id = @id AND archived_at IS NULL"
                : "SELECT opener_asset_id AS AssetId, opener_caption AS Caption FROM chapter WHERE id = @id AND archived_at IS NULL",
            new { id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"{kind} not found.");
        return new OpenerDto(id, Kind(kind), row.AssetId, row.Caption);
    }

    private async Task<OpenerDto> SetAsync(BookItem kind, Guid id, SetOpenerRequest request, CancellationToken cancellationToken)
    {
        var bookId = (await access.RequireAsync(kind, id, BookRole.Write, cancellationToken, $"{kind} not found."))!.Value;
        var caption = request.AssetId is null || string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            if (request.AssetId is { } assetId)
            {
                await PlaceableAsset.RequireAsync(connection, bookId, assetId, currentUser.Id, cancellationToken);
            }

            var updated = await connection.ExecuteAsync(new CommandDefinition(
                kind == BookItem.Act
                    ? "UPDATE act SET opener_asset_id = @assetId, opener_caption = @caption, updated_at = now() WHERE id = @id AND archived_at IS NULL"
                    : "UPDATE chapter SET opener_asset_id = @assetId, opener_caption = @caption, updated_at = now() WHERE id = @id AND archived_at IS NULL",
                new { id, assetId = request.AssetId, caption }, cancellationToken: cancellationToken));
            if (updated == 0)
            {
                throw ApiException.NotFound($"{kind} not found.");
            }
        }

        await events.PublishAsync(new StructureChanged(bookId, Kind(kind)));
        return new OpenerDto(id, Kind(kind), request.AssetId, caption);
    }

    private static string Kind(BookItem kind) => kind == BookItem.Act ? "act" : "chapter";
}
