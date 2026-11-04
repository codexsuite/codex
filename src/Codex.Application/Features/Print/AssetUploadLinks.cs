using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print;

// Short-lived, single-use upload links (#65): how an image reaches Codex from an agent on remote /mcp without its bytes
// passing through the model. create_image_upload mints a link for one book (or series) as the current user, under the
// personal access token (#32) its call used; the agent PUTs the file to it (curl -T), and the upload runs through
// IAssetService.UploadAsync as that user, so the limits, probing and renditions are the same as the Assets panel's. The
// link is the only credential the PUT carries, and it stops working when that token is revoked or expires, or the user
// is disabled.
public interface IAssetUploadLinks
{
    // apiTokenId: the token the asking call ran under (null for the local user). 404 unknown book or series; 400 a type
    // other than PNG, JPEG or WebP, or a size of 0 or over 25 MB.
    Task<AssetUploadLink> CreateAsync(Guid bookId, AssetUploadLinkRequest request, Guid? apiTokenId, CancellationToken cancellationToken);

    // The PUT. 400 a declared length over 25 MB or other than the size the link was made for (before any of the body is
    // read), or the asset checks; 404 a link that is unknown, used or expired, or whose token or user no longer works.
    // A failed upload gives the link back.
    Task<AssetDto> UploadAsync(string token, long? contentLength, Stream content, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class AssetUploadLinks(
    NpgsqlDataSource dataSource, IAssetService assets, IBookAccess access, ICurrentUser currentUser, TimeProvider clock) : IAssetUploadLinks
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private static readonly string[] Types = ["image/png", "image/jpeg", "image/webp"];

    private sealed record LinkRow(Guid Id, Guid OwnerId, Guid BookId, string Filename, string Mime, long Bytes, string Meta);

    public async Task<AssetUploadLink> CreateAsync(Guid bookId, AssetUploadLinkRequest request, Guid? apiTokenId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var mime = request.ContentType.Trim().ToLowerInvariant();
        if (!Types.Contains(mime))
        {
            throw ApiException.Validation("Upload a PNG, JPEG or WebP image.");
        }
        if (request.Bytes <= 0 || request.Bytes > AssetService.MaxUploadBytes)
        {
            throw ApiException.Validation($"Images can be at most {AssetService.MaxUploadBytes / (1024 * 1024)} MB.");
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow();
        var meta = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["source"] = request.Source, ["prompt"] = request.Prompt, ["licence"] = request.Licence,
        }.Where(n => !string.IsNullOrWhiteSpace(n.Value)).ToDictionary());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO asset_upload (token_hash, owner_id, api_token_id, book_id, filename, mime, bytes, meta, created_at, expires_at)
            VALUES (@hash, @owner, @apiTokenId, @bookId, @filename, @mime, @bytes, @meta::jsonb, @now, @expires)
            """,
            new
            {
                hash = Hash(token), owner = currentUser.Id, apiTokenId, bookId, filename = request.FileName, mime, bytes = request.Bytes, meta,
                now = now.UtcDateTime, expires = (now + Lifetime).UtcDateTime,
            },
            cancellationToken: cancellationToken));
        return new AssetUploadLink(token, now + Lifetime);
    }

    public async Task<AssetDto> UploadAsync(string token, long? contentLength, Stream content, CancellationToken cancellationToken)
    {
        if (contentLength > AssetService.MaxUploadBytes)
        {
            throw ApiException.Validation($"Images can be at most {AssetService.MaxUploadBytes / (1024 * 1024)} MB.");
        }

        LinkRow link;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            link = await connection.QuerySingleOrDefaultAsync<LinkRow>(new CommandDefinition(
                """
                UPDATE asset_upload u SET used_at = @now
                FROM app_user usr
                WHERE u.token_hash = @hash AND u.used_at IS NULL AND u.expires_at > @now
                  AND usr.id = u.owner_id AND usr.disabled_at IS NULL
                  AND (u.api_token_id IS NULL OR EXISTS (
                      SELECT 1 FROM api_token t
                      WHERE t.id = u.api_token_id AND t.revoked_at IS NULL AND (t.expires_at IS NULL OR t.expires_at > @now)))
                RETURNING u.id, u.owner_id AS OwnerId, u.book_id AS BookId, u.filename, u.mime, u.bytes, u.meta::text AS Meta
                """,
                new { hash = Hash(token), now = clock.GetUtcNow().UtcDateTime }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("This upload link is unknown, already used or expired. Ask for a new one.");
        }

        try
        {
            if (contentLength is { } length && length != link.Bytes)
            {
                throw ApiException.Validation($"This link is for a file of {link.Bytes} bytes; the request says {length}.");
            }

            var notes = JsonSerializer.Deserialize<Dictionary<string, string>>(link.Meta) ?? [];
            AssetDto asset;
            using (CurrentUser.ActAs(link.OwnerId))
            {
                asset = await assets.UploadAsync(link.BookId, new AssetUpload(
                    link.Filename, link.Mime, link.Bytes, content,
                    notes.GetValueOrDefault("source"), notes.GetValueOrDefault("prompt"), notes.GetValueOrDefault("licence")), cancellationToken);
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE asset_upload SET asset_id = @assetId WHERE id = @id", new { assetId = asset.Id, id = link.Id }, cancellationToken: cancellationToken));
            return asset;
        }
        catch
        {
            await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE asset_upload SET used_at = NULL WHERE id = @id AND asset_id IS NULL", new { id = link.Id }, cancellationToken: CancellationToken.None));
            throw;
        }
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
