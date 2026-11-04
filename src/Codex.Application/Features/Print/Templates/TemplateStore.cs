using System.Collections.Concurrent;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Templates;

// Template versions as packages on disk (issue #13 phase 6). Every layout_template_version keeps its files as one .zip
// on its row (package_zip). Opening a version gives the built-in folder under CODEX_TEMPLATES_ROOT when
// the version is that built-in's version on disk, and otherwise the stored zip unpacked once (TemplateArchive.Extract,
// with its zip-slip, link and size checks) into CODEX_TEMPLATE_CACHE/<version id>-<package hash>/<slug>. Versions are
// immutable, so a cached folder never goes stale; a built-in whose files changed without a version bump gets a new
// package hash from the startup sync and so a new folder.
// No ownership here: the render core and the job workers call it. Callers that take a template or version id from a
// user check it first (TemplateLibrary, and ResolveAsync, which only sees built-ins and the book owner's templates).
public interface ITemplateStore
{
    // The package of a published or draft version. 404 unknown version, or a package that is not stored.
    Task<TemplatePackage> OpenAsync(Guid versionId, CancellationToken cancellationToken);

    // The version a book renders with: versionId when given (a published version of a built-in or of the book owner's
    // template; it must belong to slug when both are given), else the template named by slug (built-in first, then the
    // owner's), else the book's chosen template, else the default built-in. A template the book used before renders at
    // its pinned version; one it never used, at its newest published version. 404 unknown book, template or version;
    // 400 a version of another template than slug.
    Task<ResolvedTemplate> ResolveAsync(Guid bookId, string? slug, Guid? versionId, CancellationToken cancellationToken);

    // The version's stored package .zip. 404 unknown version or no stored package.
    Task<byte[]> ReadZipAsync(Guid versionId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class TemplateStore(NpgsqlDataSource dataSource, ITemplateLoader loader, RenderSettings settings) : ITemplateStore
{
    // A book that has not picked a template in Settings → Layout renders with this one.
    public const string DefaultTemplate = "novel-6x9";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _unpacking = new(StringComparer.Ordinal);

    public async Task<TemplatePackage> OpenAsync(Guid versionId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
            """
            SELECT t.slug AS Slug, t.owner_id IS NULL AS BuiltIn, v.version AS Version, v.package_zip IS NOT NULL AS HasZip, v.package_sha256 AS PackageSha
            FROM layout_template_version v JOIN layout_template t ON t.id = v.template_id
            WHERE v.id = @versionId
            """,
            new { versionId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Template version {versionId} was not found.");

        if (row.BuiltIn && OnDisk(row.Slug) is { } disk && disk.Version == row.Version)
        {
            return disk;
        }

        if (!row.HasZip)
        {
            throw ApiException.NotFound($"The package of {row.Slug} v{row.Version} is not stored on this server.");
        }

        var hash = row.PackageSha is { Length: >= 16 } sha ? sha[..16] : "unhashed";
        var folder = Path.Combine(settings.TemplateCache, $"{versionId:N}-{hash}");
        var package = Path.Combine(folder, row.Slug);
        if (!Directory.Exists(package))
        {
            await UnpackAsync(versionId, folder, row.Slug, cancellationToken);
        }

        return loader.Load(package);
    }

    public async Task<ResolvedTemplate> ResolveAsync(Guid bookId, string? slug, Guid? versionId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var owner = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT owner_id FROM book WHERE id = @bookId", new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Book {bookId} was not found.");

        Guid templateId;
        if (versionId is { } id)
        {
            var version = await connection.QuerySingleOrDefaultAsync<(Guid TemplateId, string Slug)?>(new CommandDefinition(
                """
                SELECT t.id AS TemplateId, t.slug AS Slug
                FROM layout_template_version v JOIN layout_template t ON t.id = v.template_id
                WHERE v.id = @id AND v.published_at IS NOT NULL AND (t.owner_id IS NULL OR t.owner_id = @owner)
                """,
                new { id, owner }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Template version {id} was not found.");
            if (slug is not null && slug != version.Slug)
            {
                throw ApiException.Validation($"Template version {id} is a version of {version.Slug}, not {slug}.");
            }
            templateId = version.TemplateId;
        }
        else
        {
            var chosen = slug is null
                ? await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    "SELECT template_id FROM book_layout WHERE book_id = @bookId", new { bookId }, cancellationToken: cancellationToken))
                : null;
            templateId = chosen ?? await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                SELECT id FROM layout_template
                WHERE slug = @slug AND (owner_id IS NULL OR owner_id = @owner) AND archived_at IS NULL
                ORDER BY owner_id NULLS FIRST LIMIT 1
                """,
                new { slug = slug ?? DefaultTemplate, owner }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"There is no layout template '{slug ?? DefaultTemplate}'.");
        }

        var stored = await connection.QuerySingleOrDefaultAsync<StoredRow>(new CommandDefinition(
            """
            SELECT template_version_id AS VersionId, trim_size AS Trim, options::text AS Options
            FROM book_layout_template WHERE book_id = @bookId AND template_id = @templateId
            """,
            new { bookId, templateId }, cancellationToken: cancellationToken));
        var pinned = versionId ?? stored?.VersionId ?? await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM layout_template_version
            WHERE template_id = @templateId AND published_at IS NOT NULL
            ORDER BY major DESC, minor DESC, patch DESC LIMIT 1
            """,
            new { templateId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("That layout template has no published version.");

        var package = await OpenAsync(pinned, cancellationToken);
        return new ResolvedTemplate(templateId, pinned, package, stored?.Trim, stored?.Options);
    }

    public async Task<byte[]> ReadZipAsync(Guid versionId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<byte[]?>(new CommandDefinition(
            "SELECT package_zip FROM layout_template_version WHERE id = @versionId", new { versionId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Template version {versionId} has no stored package.");
    }

    private TemplatePackage? OnDisk(string slug)
    {
        try
        {
            return loader.GetBuiltIn(slug);
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.NotFound)
        {
            return null;
        }
    }

    // Unpacks into a staging folder beside the cache folder and renames it into place, so a reader never sees half a
    // package; one unpack per folder at a time in this process, and a second process's rename simply loses.
    private async Task UnpackAsync(Guid versionId, string folder, string slug, CancellationToken cancellationToken)
    {
        var gate = _unpacking.GetOrAdd(folder, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Directory.Exists(Path.Combine(folder, slug)))
            {
                return;
            }

            CreateOwnerOnly(settings.TemplateCache);
            var staging = Path.Combine(settings.TemplateCache, ".staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var zip = new MemoryStream(await ReadZipAsync(versionId, cancellationToken));
                CreateOwnerOnly(staging);
                TemplateArchive.Extract(zip, Path.Combine(staging, slug));

                try
                {
                    Directory.Move(staging, folder);
                }
                catch (IOException) when (Directory.Exists(Path.Combine(folder, slug)))
                {
                    // Another process unpacked the same version first; its folder is identical.
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static void CreateOwnerOnly(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnly);
        }
    }

    private sealed record VersionRow(string Slug, bool BuiltIn, string Version, bool HasZip, string? PackageSha);

    private sealed record StoredRow(Guid VersionId, string Trim, string Options);
}
