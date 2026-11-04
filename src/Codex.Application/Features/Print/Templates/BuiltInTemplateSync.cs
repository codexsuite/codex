using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Features.Print.Templates;

// At startup, every built-in package under CODEX_TEMPLATES_ROOT gets its layout_template row (owner NULL, found by
// slug, so the seed's rows are reused) and a published layout_template_version row for its manifest's version, with the
// package stored on the row as a .zip so the version still renders after a newer one ships. A version that is new on disk adds a row, which book_layout_current then offers as the upgrade; books stay
// pinned to the version they have. The same version with changed files (a fix shipped without a version bump) keeps its
// row and points it at the new package. Idempotent, and serialized across processes by an advisory lock. A package
// with problems is skipped with a warning. Registered after the migration service (module order), so the columns exist.
public sealed class BuiltInTemplateSync(
    NpgsqlDataSource dataSource,
    ITemplateLoader loader,
    ILogger<BuiltInTemplateSync> logger) : IHostedService
{
    private const long LockKey = 0x636F6465785F7470; // "codex_tp"

    // A process that cannot sync (the worker started before the web host migrated) logs it and carries on: the web
    // host's sync, or the next start, does it.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncAsync(cancellationToken);
        }
        catch (NpgsqlException e)
        {
            logger.LogError(e, "The built-in layout templates were not synced");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Returns how many version rows it added or repointed.
    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var package in loader.ListBuiltIn())
        {
            if (!package.CanCompile)
            {
                logger.LogWarning("Built-in template {Slug} was not synced: {Problems}", package.Slug, string.Join(" ", package.Problems));
                continue;
            }

            changed += await SyncAsync(package, cancellationToken) ? 1 : 0;
        }

        return changed;
    }

    private async Task<bool> SyncAsync(TemplatePackage package, CancellationToken cancellationToken)
    {
        var parts = package.Version.Split('.').Select(int.Parse).ToArray();
        var manifest = package.ManifestJson();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@LockKey)", new { LockKey }, transaction, cancellationToken: cancellationToken));

        var templateId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM layout_template WHERE owner_id IS NULL AND slug = @Slug", new { package.Slug }, transaction, cancellationToken: cancellationToken))
            ?? await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO layout_template (slug, name) VALUES (@Slug, @Name) RETURNING id",
                new { package.Slug, package.Name }, transaction, cancellationToken: cancellationToken));

        var existing = await connection.QuerySingleOrDefaultAsync<(Guid Id, string? Sha)?>(new CommandDefinition(
            """
            SELECT id AS Id, package_sha256 AS Sha FROM layout_template_version
            WHERE template_id = @templateId AND major = @major AND minor = @minor AND patch = @patch
            """,
            new { templateId, major = parts[0], minor = parts[1], patch = parts[2] }, transaction, cancellationToken: cancellationToken));
        var changelog = package.Changelog.ToArray();
        int written;
        if (existing?.Sha == package.ContentHash)
        {
            written = 0;
        }
        else if (existing is null)
        {
            written = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO layout_template_version
                    (template_id, major, minor, patch, engine, manifest, changelog, package_zip, package_sha256, published_at)
                VALUES (@templateId, @major, @minor, @patch, @engine, @manifest::jsonb, @changelog, @zip, @sha, now())
                """,
                new { templateId, major = parts[0], minor = parts[1], patch = parts[2], engine = package.Engine, manifest, changelog, zip = TemplateArchive.Zip(package.Directory), sha = package.ContentHash },
                transaction, cancellationToken: cancellationToken));
        }
        else
        {
            // The seed's rows (no package yet) and a same-version fix land here. A version the seed made keeps its changelog.
            written = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE layout_template_version
                SET manifest = @manifest::jsonb, engine = @engine, package_zip = @zip, package_sha256 = @sha,
                    changelog = CASE WHEN cardinality(@changelog) > 0 THEN @changelog ELSE changelog END,
                    published_at = coalesce(published_at, now())
                WHERE id = @id
                """,
                new { id = existing.Value.Id, manifest, engine = package.Engine, zip = TemplateArchive.Zip(package.Directory), sha = package.ContentHash, changelog },
                transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        if (written > 0)
        {
            logger.LogInformation("Synced built-in template {Slug} v{Version} ({Hash})", package.Slug, package.Version, package.ContentHash[..16]);
        }

        return written > 0;
    }
}
