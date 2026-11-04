using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print;

// Uploaded images and their derived files. The one entry point for the print lane: the layout view, proof and
// export read assets, renditions and effective dpi through here. A book id may also be a series id. Every entry point
// applies the access rule first (IBookAccess): a book or asset the user cannot open is not_found, as an unknown id is.
// An image in a book is checked through the book (#80): view to read it, write to upload or requeue. asset.owner_id
// stays the uploader. The file an export or proof made is read like the export itself, with write.
public interface IAssetService
{
    // Stores the original through IBlobStore, inserts the asset row and queues its renditions (the worker makes web,
    // proxy and print). 404 unknown book; 400 for no file, an empty or oversize (25 MB) file, a type other than
    // PNG, JPEG or WebP, bytes that do not decode as one, or more than 64 megapixels.
    Task<AssetDto> UploadAsync(Guid bookId, AssetUpload upload, CancellationToken cancellationToken);

    // 404 unknown or archived. Status "ready" also covers a duplicated book's copies, which have renditions but no job. A trim adds Dpi (columns, placement and the 300 or 150 target default to 1, "column", 300).
    Task<AssetDto> GetAsync(Guid assetId, AssetDpiRequest? dpi, CancellationToken cancellationToken);

    // The images a book can place: its own and its series'. 404 unknown book.
    Task<IReadOnlyList<AssetDto>> ListAsync(Guid bookId, AssetDpiRequest? dpi, CancellationToken cancellationToken);

    // Effective dpi of the asset's original. 400 for an asset without pixel size or an unknown trim/placement.
    Task<AssetDpiDto> DpiAsync(Guid assetId, AssetDpiRequest dpi, CancellationToken cancellationToken);

    // The original (purpose null) or a rendition's bytes. 404 unknown asset, or a rendition not made yet.
    Task<AssetFile> OpenAsync(Guid assetId, string? purpose, CancellationToken cancellationToken);

    // Queues the renditions again (after a failure, or to regenerate). 409 while queued or running.
    Task<AssetDto> RequeueAsync(Guid assetId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class AssetService(
    NpgsqlDataSource dataSource,
    IBlobStore blobs,
    IBookAccess access,
    IAssetIntake intake,
    AssetRenditionSignal signal,
    TimeProvider clock) : IAssetService
{
    public const long MaxUploadBytes = 25 * 1024 * 1024;
    public const long MaxPixels = ImageRenditioner.MaxPixels;

    private static readonly string[] Purposes = ["web", "proxy", "print"];

    public async Task<AssetDto> UploadAsync(Guid bookId, AssetUpload upload, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);

        // No connection is held while the file is read, probed and written.
        var staged = await intake.StageAsync(
            bookId, new StagedAssetFile(upload.FileName, upload.ContentType, upload.Length, upload.Content, upload.Source, upload.Prompt, upload.Licence),
            cancellationToken);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await intake.InsertAsync(connection, transaction, staged, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await intake.DiscardAsync(staged, CancellationToken.None);
            throw;
        }

        intake.Notify();
        return await GetAsync(staged.Id, null, cancellationToken);
    }

    public async Task<AssetDto> GetAsync(Guid assetId, AssetDpiRequest? dpi, CancellationToken cancellationToken)
    {
        await RequireReadableAssetAsync(assetId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await LoadAsync(connection, assetId, null, dpi, cancellationToken);
        return rows.FirstOrDefault() ?? throw ApiException.NotFound($"Asset {assetId} was not found.");
    }

    public async Task<IReadOnlyList<AssetDto>> ListAsync(Guid bookId, AssetDpiRequest? dpi, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await LoadAsync(connection, null, bookId, dpi, cancellationToken);
    }

    public async Task<AssetDpiDto> DpiAsync(Guid assetId, AssetDpiRequest dpi, CancellationToken cancellationToken)
    {
        await RequireReadableAssetAsync(assetId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SizeRow>(new CommandDefinition(
            "SELECT width_px AS Width, height_px AS Height FROM asset WHERE id = @assetId AND archived_at IS NULL", new { assetId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            throw ApiException.NotFound($"Asset {assetId} was not found.");
        }
        if (row.Width is null || row.Height is null)
        {
            throw ApiException.Validation("Only images have an effective dpi.");
        }

        return ComputeDpi(row.Width.Value, row.Height.Value, dpi);
    }

    public async Task<AssetFile> OpenAsync(Guid assetId, string? purpose, CancellationToken cancellationToken)
    {
        await RequireReadableAssetAsync(assetId, cancellationToken);
        if (purpose is not null && !Purposes.Contains(purpose))
        {
            throw ApiException.NotFound($"There is no '{purpose}' rendition. Use web, proxy or print.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var file = purpose is null
            ? await connection.QuerySingleOrDefaultAsync<FileRow>(new CommandDefinition(
                "SELECT filename AS FileName, storage_key AS StorageKey FROM asset WHERE id = @assetId AND archived_at IS NULL",
                new { assetId }, cancellationToken: cancellationToken))
            : await connection.QuerySingleOrDefaultAsync<FileRow>(new CommandDefinition(
                """
                SELECT a.filename AS FileName, r.storage_key AS StorageKey
                FROM asset a JOIN asset_rendition r ON r.asset_id = a.id AND r.purpose = @purpose
                WHERE a.id = @assetId AND a.archived_at IS NULL
                """,
                new { assetId, purpose }, cancellationToken: cancellationToken));
        if (file is null)
        {
            throw ApiException.NotFound(purpose is null
                ? $"Asset {assetId} was not found."
                : $"Asset {assetId} has no '{purpose}' rendition yet.");
        }

        var blob = await blobs.GetAsync(file.StorageKey, cancellationToken)
                   ?? throw ApiException.NotFound($"The stored file of asset {assetId} is missing.");
        return new AssetFile(blob.Content, blob.ContentType, file.FileName);
    }

    public async Task<AssetDto> RequeueAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await RequireAssetAsync(assetId, BookRole.Write, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var kind = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT kind FROM asset WHERE id = @assetId AND archived_at IS NULL", new { assetId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Asset {assetId} was not found.");
        if (kind != "image")
        {
            throw ApiException.Validation("Only images have renditions.");
        }

        var queued = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO asset_rendition_job (asset_id, created_at) VALUES (@assetId, @now)
            ON CONFLICT (asset_id) DO UPDATE
               SET status = 'queued', attempts = 0, error = NULL, created_at = EXCLUDED.created_at, started_at = NULL, finished_at = NULL
               WHERE asset_rendition_job.status IN ('succeeded', 'failed')
            """,
            new { assetId, now = clock.GetUtcNow().UtcDateTime }, cancellationToken: cancellationToken));
        if (queued == 0)
        {
            throw ApiException.Conflict("This asset's renditions are already being made.");
        }

        signal.Notify();
        return await GetAsync(assetId, null, cancellationToken);
    }

    private Task<Guid?> RequireAssetAsync(Guid assetId, BookRole minimum, CancellationToken cancellationToken)
        => access.RequireAsync(BookItem.Asset, assetId, minimum, cancellationToken, $"Asset {assetId} was not found.");

    // View reads an image; an export's or proof's output file needs write, as the export itself does (#80 Q1).
    private async Task RequireReadableAssetAsync(Guid assetId, CancellationToken cancellationToken)
    {
        if (await RequireAssetAsync(assetId, BookRole.View, cancellationToken) is null)
        {
            return;
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exported = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM export_job WHERE output_asset_id = @assetId)", new { assetId }, cancellationToken: cancellationToken));
        if (exported)
        {
            await RequireAssetAsync(assetId, BookRole.Write, cancellationToken);
        }
    }

    private static AssetDpiDto ComputeDpi(int width, int height, AssetDpiRequest request)
        => EffectiveDpi.Compute(width, height, request.Trim, request.Columns, request.Placement, request.TargetDpi);

    private async Task<IReadOnlyList<AssetDto>> LoadAsync(NpgsqlConnection connection, Guid? assetId, Guid? bookId, AssetDpiRequest? dpi, CancellationToken cancellationToken)
    {
        // Validate the dpi query up front so a bad trim is a 400 even when nothing matches.
        if (dpi is not null)
        {
            ComputeDpi(1, 1, dpi);
        }

        // One asset (assetId) or a book's images, its own and its series' (bookId).
        var assets = (await connection.QueryAsync<AssetRow>(new CommandDefinition(
            """
            SELECT a.id, a.book_id AS BookId,
                   CASE WHEN a.book_id IS NULL THEN 'user' ELSE b.kind END AS Scope,
                   a.kind, a.filename, a.mime, a.bytes, a.width_px AS WidthPx, a.height_px AS HeightPx,
                   CASE j.status WHEN 'succeeded' THEN 'ready' WHEN 'failed' THEN 'failed' WHEN 'queued' THEN 'processing'
                                 WHEN 'running' THEN 'processing'
                                 ELSE CASE WHEN EXISTS (SELECT 1 FROM asset_rendition r WHERE r.asset_id = a.id)
                                           THEN 'ready' ELSE 'none' END END AS Status,
                   j.error, a.created_at AS CreatedAt
            FROM asset a
            LEFT JOIN book b ON b.id = a.book_id
            LEFT JOIN asset_rendition_job j ON j.asset_id = a.id
            WHERE a.archived_at IS NULL
              AND (@assetId::uuid IS NULL OR a.id = @assetId)
              AND (@bookId::uuid IS NULL
                   OR (a.kind = 'image' AND a.book_id IN (SELECT scope_id FROM book_scope WHERE book_id = @bookId)))
            ORDER BY a.created_at DESC, a.id DESC
            """,
            new { assetId, bookId }, cancellationToken: cancellationToken))).ToList();
        if (assets.Count == 0)
        {
            return [];
        }

        var ids = assets.Select(a => a.Id).ToArray();
        var renditions = (await connection.QueryAsync<RenditionRow>(new CommandDefinition(
            """
            SELECT asset_id AS AssetId, purpose, mime, width_px AS WidthPx, height_px AS HeightPx, bytes
            FROM asset_rendition WHERE asset_id = ANY(@ids)
            ORDER BY array_position(ARRAY['web', 'proxy', 'print'], purpose)
            """,
            new { ids }, cancellationToken: cancellationToken))).ToLookup(r => r.AssetId);

        return assets.Select(a => new AssetDto(
            a.Id, a.BookId, a.Scope, a.Kind, a.Filename, a.Mime, a.Bytes, a.WidthPx, a.HeightPx, a.Status, a.Error,
            $"/api/assets/{a.Id}",
            renditions[a.Id].Select(r => new AssetRenditionDto(r.Purpose, r.Mime, r.WidthPx, r.HeightPx, r.Bytes, $"/api/assets/{a.Id}/renditions/{r.Purpose}")).ToList(),
            dpi is not null && a.WidthPx is { } w && a.HeightPx is { } h ? ComputeDpi(w, h, dpi) : null,
            a.CreatedAt, DisplayDate.Relative(a.CreatedAt, clock))).ToList();
    }

    private sealed record SizeRow(int? Width, int? Height);

    private sealed record FileRow(string FileName, string StorageKey);

    private sealed record AssetRow(
        Guid Id, Guid? BookId, string Scope, string Kind, string Filename, string Mime, long Bytes,
        int? WidthPx, int? HeightPx, string Status, string? Error, DateTimeOffset CreatedAt);

    private sealed record RenditionRow(Guid AssetId, string Purpose, string Mime, int WidthPx, int HeightPx, long Bytes);
}
