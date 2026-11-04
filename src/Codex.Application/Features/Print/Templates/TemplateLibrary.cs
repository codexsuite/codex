using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Templates;

// The user's own layout templates (issue #13 phase 6): fork a version, upload a package as a new template or as a new
// version of one, download a version's package, archive a template, and the picker thumbnail's sample. A template is
// scoped to its owner: another user's template or version is not_found (built-ins are everyone's to fork, download and
// preview, never to change). A user's slug is unique among their templates and never a built-in's. Every version is
// published as it is created and stored as a .zip on its row; uploads are checked by TemplateArchive (zip safety)
// and TemplateLoader (the package itself), and a package with problems is refused with them (400 validation).
// User templates are code: their Lua filters and TeX only ever run in the same codex-sandbox as the built-ins
// (docs/print.md).
public interface ITemplateLibrary
{
    // Copies a version (a built-in's or the user's) into a new template of the user's at 1.0.0, named Name or
    // "<name> (fork)", slug "<slug>-fork" (made unique). 404 unknown or another user's version.
    Task<LayoutTemplateVersionDto> ForkAsync(Guid versionId, ForkTemplateRequest request, CancellationToken cancellationToken);

    // A .zip package as a new template of the user's (templateId null) or a new version of the user's template (the
    // manifest's slug must be the template's and its version newer than every existing one). 404 unknown or another
    // user's template; 400 not a zip, an unsafe or oversized zip, a package with problems, a built-in template, or a
    // version that is not newer; 409 a slug the user or a built-in already has. openPackage reads the upload; it is
    // called after the ownership check, so another user's template is 404 whatever was sent.
    Task<LayoutTemplateVersionDto> UploadAsync(Guid? templateId, Func<CancellationToken, Task<Stream>> openPackage, CancellationToken cancellationToken);

    // The version's package as a .zip and a file name for it ("<slug>-<version>.zip"). 404 unknown or another user's version.
    Task<(Stream Content, string FileName)> DownloadAsync(Guid versionId, CancellationToken cancellationToken);

    // Hides the user's template from the picker. 404 unknown or another user's; 400 a built-in; 409 while a book uses it.
    Task ArchiveAsync(Guid templateId, CancellationToken cancellationToken);

    // sample.md through the template's filters as html5, with its preview.css and the first trim's page. 404 unknown or
    // another user's version, or a package without sample.md; 503 without pandoc.
    Task<LayoutTemplateSampleDto> SampleAsync(Guid versionId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class TemplateLibrary(
    NpgsqlDataSource dataSource,
    ITemplateStore store,
    ITemplateLoader loader,
    IPandocRunner pandoc,
    RenderWorkFolders folders,
    ICurrentUser currentUser,
    TimeProvider clock) : ITemplateLibrary
{
    public const string SampleFile = "sample.md";
    private const int MaxNameLength = 120;
    private const int MaxSlugLength = 60;

    private static readonly JsonDocumentOptions ManifestReading = TemplatePackage.ManifestReading;
    private static readonly JsonSerializerOptions ManifestWriting = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<string, LayoutTemplateSampleDto> _samples = new(StringComparer.Ordinal);

    public async Task<LayoutTemplateVersionDto> ForkAsync(Guid versionId, ForkTemplateRequest request, CancellationToken cancellationToken)
    {
        var source = await VisibleVersionAsync(versionId, cancellationToken);
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} (fork)" : request.Name.Trim();
        if (name.Length > MaxNameLength)
        {
            throw ApiException.Validation($"A template name is at most {MaxNameLength} characters.");
        }

        var package = await store.OpenAsync(versionId, cancellationToken);
        var job = folders.Create();
        try
        {
            var slug = await FreeSlugAsync(source.Slug, cancellationToken);
            var copy = Path.Combine(job, slug);
            CopyFolder(package.Directory, copy);
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(copy, TemplatePackage.ManifestFile), cancellationToken), null, ManifestReading)!.AsObject();
            manifest["slug"] = slug;
            manifest["name"] = name;
            manifest["version"] = "1.0.0";
            manifest["changelog"] = new JsonArray($"Forked from {source.Name} v{source.Version}");
            await File.WriteAllTextAsync(Path.Combine(copy, TemplatePackage.ManifestFile), manifest.ToJsonString(ManifestWriting), cancellationToken);

            var fork = loader.Load(copy);
            if (!fork.CanCompile)
            {
                throw ApiException.Validation($"{source.Name} v{source.Version} has problems and cannot be forked: {string.Join(" ", fork.Problems)}");
            }

            return await CreateAsync(null, fork, source.Description, versionId, cancellationToken);
        }
        finally
        {
            folders.TryDelete(job);
        }
    }

    public async Task<LayoutTemplateVersionDto> UploadAsync(
        Guid? templateId, Func<CancellationToken, Task<Stream>> openPackage, CancellationToken cancellationToken)
    {
        var target = templateId is { } id ? await OwnTemplateAsync(id, cancellationToken) : null;
        MemoryStream zip;
        await using (var content = await openPackage(cancellationToken))
        {
            zip = await ReadCappedAsync(content, cancellationToken);
        }

        using var _ = zip;
        var job = folders.Create();
        try
        {
            // "_upload" is never a slug (no underscores), so the rename below always moves the folder.
            var unpacked = Path.Combine(job, "_upload");
            TemplateArchive.Extract(zip, unpacked);
            var slug = await ManifestSlugAsync(unpacked, cancellationToken);
            if (target is not null && slug != target.Slug)
            {
                throw ApiException.Validation($"The package's slug is '{slug}', but this is a new version of '{target.Slug}'. Set \"slug\": \"{target.Slug}\" in template.json.");
            }

            var folder = Path.Combine(job, slug);
            Directory.Move(unpacked, folder);
            var package = loader.Load(folder);
            if (!package.CanCompile)
            {
                throw ApiException.Validation($"The template package has problems: {string.Join(" ", package.Problems)}");
            }

            return await CreateAsync(target, package, null, null, cancellationToken);
        }
        finally
        {
            folders.TryDelete(job);
        }
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await VisibleVersionAsync(versionId, cancellationToken);
        var fileName = $"{version.Slug}-{version.Version}.zip";
        if (version.HasZip)
        {
            return (new MemoryStream(await store.ReadZipAsync(versionId, cancellationToken)), fileName);
        }

        var package = await store.OpenAsync(versionId, cancellationToken);
        return (new MemoryStream(TemplateArchive.Zip(package.Directory)), fileName);
    }

    public async Task ArchiveAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await OwnTemplateAsync(templateId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var books = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT b.title FROM book_layout bl JOIN book b ON b.id = bl.book_id WHERE bl.template_id = @templateId ORDER BY b.title",
            new { templateId }, cancellationToken: cancellationToken))).ToList();
        if (books.Count > 0)
        {
            throw ApiException.Conflict(
                $"{template.Name} is the template of {string.Join(", ", books)}. Choose another template for {(books.Count == 1 ? "that book" : "those books")} first.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE layout_template SET archived_at = @now, updated_at = @now WHERE id = @templateId AND archived_at IS NULL",
            new { templateId, now = clock.GetUtcNow().UtcDateTime }, cancellationToken: cancellationToken));
    }

    public async Task<LayoutTemplateSampleDto> SampleAsync(Guid versionId, CancellationToken cancellationToken)
    {
        await VisibleVersionAsync(versionId, cancellationToken);
        var package = await store.OpenAsync(versionId, cancellationToken);
        var key = $"{versionId:N}:{package.ContentHash}";
        if (_samples.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!File.Exists(Path.Combine(package.Directory, SampleFile)))
        {
            throw ApiException.NotFound($"{package.Name} has no {SampleFile} to draw a thumbnail from.");
        }

        var job = folders.Create();
        try
        {
            foreach (var file in package.Filters.Append(SampleFile))
            {
                var copy = Path.Combine(job, file);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(Path.Combine(package.Directory, file), copy);
            }

            var converted = await pandoc.ConvertAsync(job, SampleFile, "html5", null, package.Filters, "sample.html", cancellationToken, from: "markdown");
            if (converted.TimedOut || converted.ExitCode != 0)
            {
                throw new InvalidOperationException($"The sample of {package.Name} did not convert: {converted.Stderr}");
            }

            var css = package.HasPreviewCss
                ? await File.ReadAllTextAsync(Path.Combine(package.Directory, TemplatePackage.PreviewCssFile), cancellationToken)
                : null;
            var trim = package.Trims[0];
            var (width, height) = TrimSizes.Page(trim);
            var page = package.Page;
            var sample = new LayoutTemplateSampleDto(
                await File.ReadAllTextAsync(Path.Combine(job, "sample.html"), cancellationToken), css, trim, width, height,
                page.Top, page.Bottom, page.Inner, page.Outer, page.Columns);
            _samples[key] = sample;
            return sample;
        }
        finally
        {
            folders.TryDelete(job);
        }
    }

    // A new template (target null) or a new version of target, with the package stored under the new version's key.
    private async Task<LayoutTemplateVersionDto> CreateAsync(
        OwnTemplate? target, TemplatePackage package, string? description, Guid? forkedFrom, CancellationToken cancellationToken)
    {
        var templateId = target?.Id ?? Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var zip = TemplateArchive.Zip(package.Directory);
        var parts = package.Version.Split('.').Select(int.Parse).ToArray();
        var manifest = package.ManifestJson();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (target is null)
        {
            var taken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM layout_template WHERE slug = @Slug AND (owner_id IS NULL OR owner_id = @owner))",
                new { package.Slug, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
            if (taken)
            {
                throw ApiException.Conflict(
                    $"A template with the slug '{package.Slug}' already exists (a built-in or one of yours, archived ones included). "
                    + "Upload this as a new version of yours, or change \"slug\" in template.json.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO layout_template (id, owner_id, slug, name, short_name, description, forked_from_version_id)
                VALUES (@templateId, @owner, @Slug, @Name, @Name, @description, @forkedFrom)
                """,
                new { templateId, owner = currentUser.Id, package.Slug, package.Name, description, forkedFrom }, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            var latest = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                """
                SELECT version FROM layout_template_version
                WHERE template_id = @templateId AND (major, minor, patch) >= (@major, @minor, @patch)
                ORDER BY major DESC, minor DESC, patch DESC LIMIT 1
                """,
                new { templateId, major = parts[0], minor = parts[1], patch = parts[2] }, transaction, cancellationToken: cancellationToken));
            if (latest is not null)
            {
                throw ApiException.Validation(
                    $"Version {package.Version} is not newer than v{latest} of {target.Name}. Raise \"version\" in template.json.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE layout_template SET name = @Name, short_name = @Name, updated_at = now() WHERE id = @templateId",
                new { templateId, package.Name }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO layout_template_version
                (id, template_id, major, minor, patch, engine, manifest, changelog, package_zip, package_sha256, published_at, created_by)
            VALUES (@versionId, @templateId, @major, @minor, @patch, @Engine, @manifest::jsonb, @changelog, @zip, @ContentHash, now(), @user)
            """,
            new
            {
                versionId, templateId, major = parts[0], minor = parts[1], patch = parts[2], package.Engine, manifest,
                changelog = package.Changelog.ToArray(), zip, package.ContentHash, user = currentUser.Id,
            },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(versionId, cancellationToken);
    }

    private async Task<LayoutTemplateVersionDto> DescribeAsync(Guid versionId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<DescribedRow>(new CommandDefinition(
            """
            SELECT t.id AS TemplateId, v.id AS VersionId, t.slug AS Slug, t.name AS Name, v.version AS Version,
                   t.owner_id IS NOT NULL AS IsUserTemplate, src.name || ' v' || sv.version AS ForkedFrom, to_jsonb(v.changelog)::text AS Changelog
            FROM layout_template_version v
            JOIN layout_template t ON t.id = v.template_id
            LEFT JOIN layout_template_version sv ON sv.id = t.forked_from_version_id
            LEFT JOIN layout_template src ON src.id = sv.template_id
            WHERE v.id = @versionId
            """,
            new { versionId }, cancellationToken: cancellationToken));
        return new LayoutTemplateVersionDto(
            row.TemplateId, row.VersionId, row.Slug, row.Name, row.Version, row.IsUserTemplate, row.ForkedFrom,
            JsonSerializer.Deserialize<string[]>(row.Changelog) ?? []);
    }

    // A version of a built-in or of the current user's template; another user's is not_found.
    private async Task<VisibleVersion> VisibleVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<VisibleVersion>(new CommandDefinition(
            """
            SELECT t.slug AS Slug, t.name AS Name, t.description AS Description, v.version AS Version, v.package_zip IS NOT NULL AS HasZip
            FROM layout_template_version v JOIN layout_template t ON t.id = v.template_id
            WHERE v.id = @versionId AND (t.owner_id IS NULL OR t.owner_id = @owner)
            """,
            new { versionId, owner = currentUser.Id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Template version {versionId} was not found.");
    }

    // The current user's template; another user's is not_found, a built-in cannot be changed.
    private async Task<OwnTemplate> OwnTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Slug, string Name, bool BuiltIn)?>(new CommandDefinition(
            """
            SELECT id AS Id, slug AS Slug, name AS Name, owner_id IS NULL AS BuiltIn FROM layout_template
            WHERE id = @templateId AND (owner_id IS NULL OR owner_id = @owner)
            """,
            new { templateId, owner = currentUser.Id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Template {templateId} was not found.");
        if (row.BuiltIn)
        {
            throw ApiException.Validation($"{row.Name} is built in and cannot be changed. Fork it to make your own copy.");
        }

        return new OwnTemplate(row.Id, row.Slug, row.Name);
    }

    // "<slug>-fork", or "-fork-2", "-fork-3"…: the first that neither a built-in nor the user's templates use.
    private async Task<string> FreeSlugAsync(string source, CancellationToken cancellationToken)
    {
        var stem = source.Length > MaxSlugLength - 8 ? source[..(MaxSlugLength - 8)].TrimEnd('-') : source;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var taken = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT slug FROM layout_template WHERE (owner_id IS NULL OR owner_id = @owner) AND slug LIKE @prefix",
            new { owner = currentUser.Id, prefix = stem + "-fork%" }, cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range(1, int.MaxValue - 1)
            .Select(n => n == 1 ? $"{stem}-fork" : $"{stem}-fork-{n}")
            .First(slug => !taken.Contains(slug));
    }

    private static async Task<string> ManifestSlugAsync(string folder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, TemplatePackage.ManifestFile);
        if (!File.Exists(path))
        {
            throw ApiException.Validation($"The template package has no {TemplatePackage.ManifestFile} at its top level.");
        }

        TemplateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<TemplateManifest>(await File.ReadAllTextAsync(path, cancellationToken), TemplateManifest.Json);
        }
        catch (JsonException e)
        {
            throw ApiException.Validation($"{TemplatePackage.ManifestFile} is not valid JSON: {e.Message}");
        }

        var slug = manifest?.Slug;
        if (slug is null || slug.Length > MaxSlugLength || !TemplateLoader.IsSlug(slug))
        {
            throw ApiException.Validation(
                $"{TemplatePackage.ManifestFile} needs a \"slug\" of lower-case letters, digits and hyphens (at most {MaxSlugLength}).");
        }

        return slug;
    }

    private static async Task<MemoryStream> ReadCappedAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > TemplateArchive.MaxZipBytes)
            {
                await buffer.DisposeAsync();
                throw ApiException.Validation($"A template package is at most {TemplateArchive.MaxZipBytes / (1024 * 1024)} MB.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private sealed record VisibleVersion(string Slug, string Name, string? Description, string Version, bool HasZip);

    private sealed record OwnTemplate(Guid Id, string Slug, string Name);

    private sealed record DescribedRow(
        Guid TemplateId, Guid VersionId, string Slug, string Name, string Version, bool IsUserTemplate, string? ForkedFrom, string Changelog);
}
