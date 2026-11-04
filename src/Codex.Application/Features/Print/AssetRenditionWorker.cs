using Codex.Application.Telemetry;
using Codex.Contracts.Storage;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Features.Print;

// Makes the web, proxy and print renditions of queued assets. Runs in the app process (the worker image runs the same
// host); claims a job with FOR UPDATE SKIP LOCKED so two processes never take one.
//   - A running job refreshes started_at every HeartbeatInterval while it renders. One whose started_at is older than
//     StaleAfter belonged to a process that stopped, and is taken over, unless it has already had MaxAttempts tries:
//     then it is marked failed instead, so a file that kills the process is not retried forever.
//   - Stopping the host mid-render puts the job back to queued (and does not count the try).
//   - Each run writes its files under keys no earlier run or other asset (a duplicated book shares its source's rendition
//     rows) uses, swaps the rows in one transaction, and only then deletes old files that no row names any more.
//   - A failed job keeps its message on asset_rendition_job.error and is retried only by IAssetService.RequeueAsync.
// No database connection is held while the image is read, rendered or written.
public sealed class AssetRenditionWorker(
    NpgsqlDataSource dataSource,
    IBlobStore blobs,
    AssetRenditionSignal signal,
    TimeProvider clock,
    ILogger<AssetRenditionWorker> logger) : BackgroundService
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);

    private bool _failing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool more;
                do
                {
                    more = await ProcessNextAsync(stoppingToken);
                }
                while (more);
                _failing = false;
                await signal.WaitAsync(PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Nothing here may stop the web host. Log the first failure of a run at Error and the repeats at Debug.
                if (_failing)
                {
                    logger.LogDebug(exception, "Asset rendition worker loop still failing");
                }
                else
                {
                    logger.LogError(exception, "Asset rendition worker loop failed; retrying every {Seconds} s", PollInterval.TotalSeconds);
                }
                _failing = true;
                await WaitQuietlyAsync(stoppingToken);
            }
        }
    }

    // Makes the renditions of the next waiting asset. False when nothing was waiting.
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await ClaimAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        // One span and one pair of metrics per job that ended; the polling loop and an idle poll record nothing.
        using var span = JobSpan.AssetRendition();
        try
        {
            var made = await RenderAsync(job, cancellationToken);
            logger.LogInformation("Made {Count} renditions of asset {AssetId}", made, job.AssetId);
        }
        catch (OperationCanceledException)
        {
            span.Abandon();
            await ExecuteQuietlyAsync(
                "UPDATE asset_rendition_job SET status = 'queued', attempts = greatest(attempts - 1, 0), started_at = NULL WHERE asset_id = @assetId AND status = 'running'",
                new { assetId = job.AssetId });
            throw;
        }
        catch (Exception exception)
        {
            span.Fail();
            logger.LogWarning(exception, "Renditions of asset {AssetId} failed", job.AssetId);
            await ExecuteQuietlyAsync(
                "UPDATE asset_rendition_job SET status = 'failed', error = @error, finished_at = @now WHERE asset_id = @assetId",
                new { assetId = job.AssetId, error = Message(exception), now = clock.GetUtcNow().UtcDateTime });
        }

        return true;
    }

    private async Task<JobRow?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var parameters = new { now = now.UtcDateTime, staleBefore = (now - StaleAfter).UtcDateTime, max = MaxAttempts };
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE asset_rendition_job
               SET status = 'failed', finished_at = @now,
                   error = 'Gave up after ' || @max || ' attempts: the worker stopped each time before finishing.'
             WHERE status = 'running' AND started_at < @staleBefore AND attempts >= @max
            """,
            parameters, cancellationToken: cancellationToken));
        return await connection.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition(
            """
            UPDATE asset_rendition_job j
               SET status = 'running', attempts = j.attempts + 1, error = NULL, started_at = @now, finished_at = NULL
              FROM asset a
             WHERE a.id = j.asset_id
               AND j.asset_id = (SELECT asset_id FROM asset_rendition_job
                                  WHERE status = 'queued' OR (status = 'running' AND started_at < @staleBefore AND attempts < @max)
                                  ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING j.asset_id AS AssetId, a.storage_key AS StorageKey, a.book_id AS BookId, a.owner_id AS OwnerId
            """,
            parameters, cancellationToken: cancellationToken));
    }

    private async Task<int> RenderAsync(JobRow job, CancellationToken cancellationToken)
    {
        using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = HeartbeatAsync(job.AssetId, stopHeartbeat.Token);
        try
        {
            var original = await blobs.GetAsync(job.StorageKey, cancellationToken)
                           ?? throw new FileNotFoundException("The original file is missing from storage.");
            byte[] content;
            await using (original.Content)
            {
                using var buffer = new MemoryStream();
                await original.Content.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }

            var renditions = await Task.Run(() => ImageRenditioner.Render(content), cancellationToken);
            return await StoreAsync(job, renditions, cancellationToken);
        }
        finally
        {
            await stopHeartbeat.CancelAsync();
            await heartbeat;
        }
    }

    private async Task<int> StoreAsync(JobRow job, IReadOnlyList<RenditionFile> renditions, CancellationToken cancellationToken)
    {
        var prefix = job.BookId is { } book ? $"books/{book}" : $"users/{job.OwnerId}";
        var run = Guid.CreateVersion7();
        var written = new List<(RenditionFile File, string Key)>();
        List<string> previous;
        try
        {
            foreach (var file in renditions)
            {
                var key = $"{prefix}/assets/{job.AssetId}/{run}-{file.Purpose}.{file.Extension}";
                await blobs.PutAsync(key, new MemoryStream(file.Content), file.Mime, cancellationToken);
                written.Add((file, key));
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            previous = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT storage_key FROM asset_rendition WHERE asset_id = @assetId", new { assetId = job.AssetId }, transaction, cancellationToken: cancellationToken))).ToList();
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM asset_rendition WHERE asset_id = @assetId", new { assetId = job.AssetId }, transaction, cancellationToken: cancellationToken));
            foreach (var (file, key) in written)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO asset_rendition (asset_id, purpose, mime, width_px, height_px, bytes, storage_key, created_at)
                    VALUES (@assetId, @purpose, @mime, @width, @height, @bytes, @key, @now)
                    """,
                    new
                    {
                        assetId = job.AssetId, purpose = file.Purpose, mime = file.Mime, width = file.WidthPx, height = file.HeightPx,
                        bytes = (long)file.Content.Length, key, now = clock.GetUtcNow().UtcDateTime,
                    },
                    transaction, cancellationToken: cancellationToken));
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE asset_rendition_job SET status = 'succeeded', error = NULL, finished_at = @now WHERE asset_id = @assetId",
                new { assetId = job.AssetId, now = clock.GetUtcNow().UtcDateTime }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            foreach (var (_, key) in written)
            {
                await blobs.DeleteAsync(key, CancellationToken.None);
            }
            throw;
        }

        await DeleteUnusedAsync(previous);
        return written.Count;
    }

    // The files the swapped-out rows named, unless another asset's row (a duplicated book shares them) still does.
    private async Task DeleteUnusedAsync(List<string> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        var stillUsed = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT storage_key FROM asset_rendition WHERE storage_key = ANY(@candidates)", new { candidates = candidates.ToArray() }))).ToHashSet(StringComparer.Ordinal);
        foreach (var key in candidates.Where(k => !stillUsed.Contains(k)))
        {
            await blobs.DeleteAsync(key, CancellationToken.None);
        }
    }

    // Keeps a long render from looking abandoned. A failed beat is only logged: the next one tries again.
    private async Task HeartbeatAsync(Guid assetId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await ExecuteQuietlyAsync(
                    "UPDATE asset_rendition_job SET started_at = @now WHERE asset_id = @assetId AND status = 'running'",
                    new { assetId, now = clock.GetUtcNow().UtcDateTime });
            }
        }
        catch (OperationCanceledException)
        {
            // the render finished
        }
    }

    // Bookkeeping that must not mask the error being handled; a database failure here is logged and the stale-job
    // takeover cleans up later.
    private async Task ExecuteQuietlyAsync(string sql, object parameters)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters));
        }
        catch (NpgsqlException exception)
        {
            logger.LogDebug(exception, "Asset rendition bookkeeping could not reach the database");
        }
    }

    private async Task WaitQuietlyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await signal.WaitAsync(PollInterval, clock, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // stopping
        }
    }

    private static string Message(Exception exception)
    {
        var message = exception is FileNotFoundException or InvalidOperationException
            ? exception.Message
            : "The image could not be processed.";
        return message.Length > 500 ? message[..500] : message;
    }

    private sealed record JobRow(Guid AssetId, string StorageKey, Guid? BookId, Guid OwnerId);
}
