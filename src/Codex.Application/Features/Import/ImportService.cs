using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Import;
using Codex.Contracts.Print;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Features.Import;

// The import pipeline: parse (IImportParser for the source) -> preview (ImportPreviewDto, nothing written) -> write
// (ImportWriter, one transaction). Plus the Plan banner's reads and its Dismiss and Undo import. An import makes a new
// book owned by the importer. The banner reads with view on the book, Dismiss needs write, and Undo import, which
// deletes the book, needs the owner (#80).
[RegisterSingleton]
public sealed class ImportService(
    IEnumerable<IImportParser> parsers,
    ImportWriter writer,
    IAssetIntake assetIntake,
    IBookLayoutChoice layouts,
    BackupWriter backupWriter,
    NpgsqlDataSource dataSource,
    ICurrentUser currentUser,
    IBookAccess access,
    IBookEvents events,
    ILogger<ImportService> logger)
{
    // Written in since the import: a revision of another source (the conductor's rule), or prose saved again after its
    // import revision, or prose on a scene the import gave none (an autosave revision is cut at most every ten minutes,
    // so the revision rule alone misses a first edit).
    private const string EditedSinceImport =
        """
        EXISTS (SELECT 1 FROM prose_revision r JOIN scene s ON s.id = r.scene_id WHERE s.book_id = @bookId AND r.source <> 'import')
        OR EXISTS (SELECT 1 FROM prose p JOIN scene s ON s.id = p.scene_id
                   WHERE s.book_id = @bookId
                     AND (p.version > 1 OR NOT EXISTS (SELECT 1 FROM prose_revision r WHERE r.scene_id = p.scene_id AND r.source = 'import')))
        """;

    private const string FailedMessage = "The import could not be saved, so nothing was imported. Check the folder's files and try again.";

    public ImportPreviewDto Preview(ImportUpload upload)
    {
        var plan = ParserFor(upload.Source).Parse(upload);
        return ImportPreview.From(upload.Source, plan);
    }

    public async Task<ImportSummaryDto> ImportAsync(ImportUpload upload, CancellationToken cancellationToken)
    {
        var plan = ParserFor(upload.Source).Parse(upload);
        try
        {
            (plan, var assets) = await StoreImagesAsync(upload, plan, Guid.CreateVersion7(), cancellationToken);
            ImportSummaryDto summary;
            try
            {
                var mapping = ImportPreview.From(upload.Source, plan);
                summary = plan.Backup is null
                    ? await writer.WriteAsync(upload.Source, plan, mapping, assets, cancellationToken)
                    : await backupWriter.WriteAsync(upload.Source, plan, mapping, cancellationToken);
            }
            catch
            {
                await DiscardAsync(assets);
                throw;
            }
            if (assets.ByPath.Count > 0)
            {
                assetIntake.Notify();
            }
            await ChooseLayoutAsync(summary.BookId, plan, cancellationToken);
            await events.PublishAsync(new BookMetaChanged(summary.BookId, ["created"]));
            return summary;
        }
        catch (ApiException e)
        {
            // The book rolled back with the transaction. The run is recorded with the detail; the client gets a fixed
            // sentence, since a database rejection's text is not for the browser.
            logger.LogWarning(e, "Import of {FileName} ({Source}) was rejected and rolled back", plan.FileName, upload.Source);
            await RecordFailureAsync(upload.Source, plan.FileName, e.Message, CancellationToken.None);
            throw ApiException.Validation(FailedMessage);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await RecordFailureAsync(upload.Source, plan.FileName, e.Message, CancellationToken.None);
            throw;
        }
    }

    public async Task<BookImportDto> GetBannerAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var isBook = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')",
            new { bookId }, cancellationToken: cancellationToken));
        if (!isBook)
        {
            throw ApiException.NotFound("Book not found.");
        }
        var result = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT result::text FROM book_import
            WHERE book_id = @bookId AND status = 'done' AND banner_dismissed_at IS NULL AND result IS NOT NULL
            ORDER BY created_at DESC LIMIT 1
            """,
            new { bookId }, cancellationToken: cancellationToken));
        var edited = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT " + EditedSinceImport, new { bookId }, cancellationToken: cancellationToken));
        return new BookImportDto(result == null ? null : JsonSerializer.Deserialize<ImportSummaryDto>(result, JsonSerializerOptions.Web), !edited);
    }

    public async Task DismissAsync(Guid importId, CancellationToken cancellationToken)
    {
        await RequireImportBookAsync(importId, BookRole.Write, "Import not found.", cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE book_import i SET banner_dismissed_at = coalesce(i.banner_dismissed_at, now())
            FROM book b WHERE i.id = @importId AND b.id = i.book_id
            """,
            new { importId }, cancellationToken: cancellationToken));
        if (updated == 0)
        {
            throw ApiException.NotFound("Import not found.");
        }
    }

    // Undo import = delete the book (#10): deleted_at hides it, and its import rows stay with it.
    public async Task UndoAsync(Guid importId, CancellationToken cancellationToken)
    {
        await RequireImportBookAsync(importId, BookRole.Owner, "Import not found, or its book is already gone.", cancellationToken);
        // Stays on owner_id on purpose: deleting the book is the owner's alone.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT b.id FROM book_import i JOIN book b ON b.id = i.book_id
            WHERE i.id = @importId AND i.status = 'done' AND b.owner_id = @owner
            FOR UPDATE OF b
            """,
            new { importId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Import not found, or its book is already gone.");
        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT " + EditedSinceImport, new { bookId }, transaction, cancellationToken: cancellationToken)))
        {
            throw ApiException.Blocked("The book has been written in since the import, so Undo import is no longer offered: it would delete that work.");
        }
        // Soft delete (#85): the book stays, archived and deleted, and book_access hides it.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE book SET archived_at = coalesce(archived_at, now()), deleted_at = now() WHERE id = @bookId",
            new { bookId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        await events.PublishAsync(new BookMetaChanged(bookId, ["deleted"]));
    }

    // The import's book, checked at minimum (not found without access, forbidden below it).
    private async Task RequireImportBookAsync(Guid importId, BookRole minimum, string notFound, CancellationToken cancellationToken)
    {
        Guid? bookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM book_import WHERE id = @importId", new { importId }, cancellationToken: cancellationToken));
        }
        await access.RequireAsync(bookId ?? throw ApiException.NotFound(notFound), minimum, cancellationToken, notFound);
    }

    // Each image the plan places, read once from the upload and stored by the asset intake (its checks and limits)
    // under the new book's id. An image that was not sent, or that the intake refuses, is left out of the plan.
    private async Task<(ImportPlan, ImportAssets)> StoreImagesAsync(ImportUpload upload, ImportPlan plan, Guid bookId, CancellationToken cancellationToken)
    {
        var staged = new Dictionary<string, StagedAsset>(StringComparer.Ordinal);
        var failed = new List<ImportSkippedItem>();
        var assets = new ImportAssets(bookId, staged);
        var sent = (upload.Images ?? []).ToDictionary(i => i.Path, StringComparer.Ordinal);
        try
        {
            foreach (var path in plan.ImagePaths())
            {
                if (!sent.TryGetValue(path, out var image))
                {
                    failed.Add(new ImportSkippedItem(path, "Not uploaded with the import."));
                    continue;
                }
                try
                {
                    await using var content = image.Open();
                    staged[path] = await assetIntake.StageAsync(
                        bookId, new StagedAssetFile(path, ContentType(path), image.Length, content, path), cancellationToken);
                }
                catch (ApiException e) when (e.Code == ApiErrorCodes.Validation)
                {
                    failed.Add(new ImportSkippedItem(path, e.Message));
                }
            }
        }
        catch
        {
            await DiscardAsync(assets);
            throw;
        }
        return (ImportImages.Without(plan, failed), assets);
    }

    // The stored originals of an import that did not commit.
    private async Task DiscardAsync(ImportAssets assets)
    {
        foreach (var asset in assets.ByPath.Values)
        {
            await assetIntake.DiscardAsync(asset, CancellationToken.None);
        }
    }

    // The book exists now; a template choice that fails leaves the book on the default template.
    private async Task ChooseLayoutAsync(Guid bookId, ImportPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Layout == null)
        {
            return;
        }
        try
        {
            await layouts.ChooseAsync(bookId, plan.Layout, cancellationToken);
        }
        catch (ApiException e)
        {
            logger.LogWarning(e, "Import of {FileName} could not choose the {Template} layout", plan.FileName, plan.Layout.Template);
        }
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    private IImportParser ParserFor(string source) =>
        parsers.FirstOrDefault(p => p.Source == source)
        ?? throw ApiException.Validation($"Import from \"{source}\" is not available; the sources are {string.Join(", ", parsers.Select(p => p.Source))}.");

    private async Task RecordFailureAsync(string source, string fileName, string error, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book_import (source, file_name, status, error, created_by, finished_at)
            VALUES (@source, @fileName, 'failed', @error, @owner, now())
            """,
            new { source, fileName, error, owner = currentUser.Id }, cancellationToken: cancellationToken));
    }
}
