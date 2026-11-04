using System.Data.Common;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Dapper;

namespace Codex.Application.Features.Print;

// The one place an uploaded image is checked, probed and stored, and its asset row and rendition job written.
// IAssetService.UploadAsync runs these steps in a transaction of its own; the import runs InsertAsync in the
// transaction that creates the book.
[RegisterSingleton]
public sealed class AssetIntake(IBlobStore blobs, ICurrentUser currentUser, AssetRenditionSignal signal, TimeProvider clock) : IAssetIntake
{
    private static readonly string[] UploadTypes = ["image/png", "image/jpeg", "image/webp"];

    public async Task<StagedAsset> StageAsync(Guid bookId, StagedAssetFile file, CancellationToken cancellationToken)
    {
        var upload = file;
        var (content, probe) = await ReadAsync(upload, cancellationToken);
        var assetId = Guid.CreateVersion7();
        var key = $"books/{bookId}/assets/{assetId}/original.{probe.Extension}";
        var meta = JsonSerializer.Serialize(new Dictionary<string, string>(
            new[] { ("source", upload.Source), ("prompt", upload.Prompt), ("licence", upload.Licence) }
                .Where(note => !string.IsNullOrWhiteSpace(note.Item2))
                .Select(note => KeyValuePair.Create(note.Item1, Cap(note.Item2!.Trim())))));
        await blobs.PutAsync(key, new MemoryStream(content), probe.Mime, cancellationToken);
        return new StagedAsset(assetId, bookId, SafeName(upload.FileName, probe.Extension), probe.Mime, content.Length, probe.WidthPx, probe.HeightPx, key, meta);
    }

    public async Task InsertAsync(DbConnection connection, DbTransaction transaction, StagedAsset asset, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO asset (id, owner_id, book_id, kind, filename, mime, bytes, width_px, height_px, storage_key, meta, created_at)
            VALUES (@Id, @owner, @BookId, 'image', @FileName, @Mime, @Bytes, @WidthPx, @HeightPx, @StorageKey, @Meta::jsonb, @now)
            """,
            new { asset.Id, owner = currentUser.Id, asset.BookId, asset.FileName, asset.Mime, asset.Bytes, asset.WidthPx, asset.HeightPx, asset.StorageKey, asset.Meta, now },
            transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO asset_rendition_job (asset_id, created_at) VALUES (@Id, @now)",
            new { asset.Id, now }, transaction, cancellationToken: cancellationToken));
    }

    public Task DiscardAsync(StagedAsset asset, CancellationToken cancellationToken) => blobs.DeleteAsync(asset.StorageKey, cancellationToken);

    public void Notify() => signal.Notify();

    private static async Task<(byte[] Content, ImageProbe Probe)> ReadAsync(StagedAssetFile upload, CancellationToken cancellationToken)
    {
        if (upload.Length <= 0)
        {
            throw ApiException.Validation("Choose an image file to upload.");
        }
        if (upload.Length > AssetService.MaxUploadBytes)
        {
            throw ApiException.Validation($"Images can be at most {AssetService.MaxUploadBytes / (1024 * 1024)} MB.");
        }
        if (!UploadTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("Upload a PNG, JPEG or WebP image.");
        }

        // The declared length is only a hint: read at most one byte past the cap.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > AssetService.MaxUploadBytes)
            {
                throw ApiException.Validation($"Images can be at most {AssetService.MaxUploadBytes / (1024 * 1024)} MB.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        var content = buffer.ToArray();
        var probe = ImageRenditioner.Probe(content)
                    ?? throw ApiException.Validation("That file is not a PNG, JPEG or WebP image.");
        if ((long)probe.WidthPx * probe.HeightPx > AssetService.MaxPixels)
        {
            throw ApiException.Validation($"Images can be at most {AssetService.MaxPixels / 1_000_000} megapixels.");
        }

        return (content, probe);
    }

    private static string SafeName(string fileName, string extension)
    {
        var name = new string(fileName.Replace('\\', '/').Split('/')[^1].Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            return $"image.{extension}";
        }
        return name.Length > 200 ? name[..200] : name;
    }

    private static string Cap(string note) => note.Length > 2000 ? note[..2000] : note;
}
