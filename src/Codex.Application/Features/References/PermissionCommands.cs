using System.Globalization;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Storage;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

[RegisterSingleton]
public sealed class PermissionCommands(
    NpgsqlDataSource dataSource,
    ISourceDetailQueries queries,
    IBlobStore blobs,
    ICurrentUser currentUser,
    TimeProvider clock,
    IBookAccess access) : IPermissionCommands
{
    public const long MaxLetterBytes = 10 * 1024 * 1024;

    private static readonly string[] LetterTypes =
    [
        "application/pdf", "image/png", "image/jpeg", "text/plain", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "message/rfc822",
    ];

    public async Task<SourceDetailDto> DraftAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.Source, sourceId, BookRole.Write, cancellationToken, $"Source {sourceId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var locked = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM reference WHERE id = @sourceId AND archived_at IS NULL FOR UPDATE", new { sourceId }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Source {sourceId} was not found.");
        var live = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM permission_request WHERE reference_id = @sourceId AND status IN ('draft', 'sent', 'granted'))",
            new { sourceId }, transaction, cancellationToken: cancellationToken));
        if (live)
        {
            throw ApiException.Conflict("This source already has a permission request.");
        }

        var letter = PermissionLetter.Draft(await FactsAsync(connection, transaction, locked, cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO permission_request (reference_id, to_address, letter, created_by) VALUES (@sourceId, '', @letter, @user)",
            new { sourceId, letter, user = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        await Record(connection, transaction, sourceId, SourceEvents.PermissionDrafted, cancellationToken, provenance: provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<SourceDetailDto> PatchAsync(Guid requestId, PatchPermissionRequest request, CancellationToken cancellationToken)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        DateTimeOffset? remindAt = null;
        var clearRemind = false;
        var remindOnText = request.RemindOn;
        var now = clock.GetUtcNow();
        var today = DisplayDate.Today(now, clock.LocalTimeZone);
        if (request.RemindInDays is { } days)
        {
            if (days is < 1 or > 365)
            {
                throw ApiException.Validation("Remind me in 1 to 365 days.");
            }

            remindOnText = today.AddDays(days)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (remindOnText is { } remindOn)
        {
            if (remindOn.Length == 0)
            {
                clearRemind = true;
            }
            else if (DateOnly.TryParseExact(remindOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                if (day < today)
                {
                    throw ApiException.Validation("The reminder date cannot be in the past.");
                }

                if (day > today.AddDays(365))
                {
                    throw ApiException.Validation("The reminder date cannot be more than 365 days away.");
                }

                remindAt = DisplayDate.StartOfDay(day, clock.LocalTimeZone);
            }
            else
            {
                throw ApiException.Validation("The reminder date must be written yyyy-MM-dd.");
            }
        }

        if (request.Body is not null && request.Body.Trim().Length == 0)
        {
            throw ApiException.Validation("The letter cannot be empty.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        if ((request.Body is not null || request.To is not null) && row.Status != "draft")
        {
            throw ApiException.Conflict("Only a draft can be edited: a request that was sent keeps its wording.");
        }

        if ((remindAt is not null || clearRemind) && row.Status is not ("draft" or "sent"))
        {
            throw ApiException.Conflict("A reminder only applies to a request that is waiting for an answer.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE permission_request
            SET letter = coalesce(@body, letter), to_address = coalesce(@to, to_address),
                remind_at = CASE WHEN @clearRemind THEN NULL ELSE coalesce(@remindAt, remind_at) END
            WHERE id = @requestId
            """,
            new { requestId, body = request.Body, to = request.To?.Trim(), remindAt, clearRemind }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> MarkSentAsync(Guid requestId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        Require(row, "draft", "Only a draft can be marked as sent.");
        if (row.Letter.Trim().Length == 0)
        {
            throw ApiException.Validation("Write the letter before marking it as sent.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE permission_request SET status = 'sent', sent_at = now(), remind_at = coalesce(remind_at, now() + interval '14 days') WHERE id = @requestId",
            new { requestId }, transaction, cancellationToken: cancellationToken));
        await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionSent, cancellationToken, provenance: provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> GrantAsync(
        Guid requestId, PermissionLetterUpload? letter, string? creditLine, string? terms, CancellationToken cancellationToken, decimal? feeAmount = null)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        ValidateLetter(letter);
        if (feeAmount is < 0)
        {
            throw ApiException.Validation("The fee cannot be negative.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        Require(row, "sent", "Only a request that was sent can be granted.");

        var key = $"books/{row.BookId}/permissions/{requestId}/{SafeName(letter!.FileName)}";
        var stored = false;
        try
        {
            await blobs.PutAsync(key, letter.Content, letter.ContentType, cancellationToken);
            stored = true;
            await InsertLetterAsync(connection, transaction, row, letter, key, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE permission_request
                SET status = 'granted', responded_at = now(), credit_line = @credit, terms = @terms, remind_at = NULL,
                    fee_amount = @fee, fee_currency = CASE WHEN @fee IS NULL THEN NULL ELSE 'USD' END
                WHERE id = @requestId
                """,
                new { requestId, credit = Blank(creditLine), terms = Blank(terms), fee = feeAmount }, transaction, cancellationToken: cancellationToken));
            await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionGranted, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (stored)
            {
                await blobs.DeleteAsync(key, CancellationToken.None);
            }

            throw;
        }

        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> MarkFollowedUpAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        Require(row, "sent", "Only a request that is waiting for an answer can be followed up.");
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE permission_request
            SET last_reminded_at = now(), remind_at = CASE WHEN remind_at IS NULL OR remind_at <= now() THEN now() + interval '7 days' ELSE remind_at END
            WHERE id = @requestId
            """,
            new { requestId }, transaction, cancellationToken: cancellationToken));
        await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionFollowedUp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> RemoveLetterAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        Require(row, "granted", "Only a granted request keeps a letter.");
        var kept = (await connection.QueryAsync<KeptLetter>(new CommandDefinition(
            """
            UPDATE reference_attachment SET archived_at = now()
            WHERE permission_request_id = @requestId AND kind = 'permission_letter' AND archived_at IS NULL
            RETURNING asset_id AS AssetId
            """,
            new { requestId }, transaction, cancellationToken: cancellationToken))).ToList();
        if (kept.Count == 0)
        {
            throw ApiException.NotFound($"Permission request {requestId} has no letter.");
        }

        // The letter's asset is archived with it and the stored file is kept (#85: nothing is hard-deleted).
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE asset SET archived_at = now() WHERE id = ANY (@ids) AND archived_at IS NULL",
            new { ids = kept.Where(k => k.AssetId is not null).Select(k => k.AssetId!.Value).ToArray() }, transaction, cancellationToken: cancellationToken));
        await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionLetterRemoved, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> AttachLetterAsync(Guid requestId, PermissionLetterUpload? letter, CancellationToken cancellationToken)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        ValidateLetter(letter);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        Require(row, "granted", "Only a granted request keeps a letter.");
        var has = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM reference_attachment WHERE permission_request_id = @requestId AND kind = 'permission_letter' AND archived_at IS NULL)",
            new { requestId }, transaction, cancellationToken: cancellationToken));
        if (has)
        {
            throw ApiException.Conflict("This request already keeps a letter: remove it first.");
        }

        var key = $"books/{row.BookId}/permissions/{requestId}/{Guid.CreateVersion7()}-{SafeName(letter!.FileName)}";
        var stored = false;
        try
        {
            await blobs.PutAsync(key, letter.Content, letter.ContentType, cancellationToken);
            stored = true;
            await InsertLetterAsync(connection, transaction, row, letter, key, cancellationToken);
            await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionLetterAttached, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (stored)
            {
                await blobs.DeleteAsync(key, CancellationToken.None);
            }

            throw;
        }

        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> RefuseAsync(Guid requestId, RefusePermissionRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        Require(row, "sent", "Only a request that was sent can be refused.");
        var note = Blank(request.Note);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE permission_request SET status = 'refused', responded_at = now(), terms = @note, remind_at = NULL WHERE id = @requestId",
            new { requestId, note }, transaction, cancellationToken: cancellationToken));
        await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionRefused, cancellationToken, provenance, new { note = note ?? string.Empty });
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<SourceDetailDto> WithdrawAsync(Guid requestId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireRequestAsync(requestId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var row = await LockAsync(connection, transaction, requestId, cancellationToken);
        if (row.Status is not ("draft" or "sent"))
        {
            throw ApiException.Conflict("Only a draft or a request waiting for an answer can be withdrawn.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE permission_request SET status = 'withdrawn', remind_at = NULL WHERE id = @requestId",
            new { requestId }, transaction, cancellationToken: cancellationToken));
        await Record(connection, transaction, row.ReferenceId, SourceEvents.PermissionWithdrawn, cancellationToken, provenance: provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, row.ReferenceId, cancellationToken);
    }

    public async Task<(BlobContent Content, string FileName)?> GetLetterAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await RequireRequestAsync(requestId, cancellationToken, BookRole.View);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM permission_request WHERE id = @requestId)", new { requestId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound($"Permission request {requestId} was not found.");
        }

        var asset = await connection.QuerySingleOrDefaultAsync<LetterRow>(new CommandDefinition(
            """
            SELECT s.filename AS FileName, s.storage_key AS StorageKey FROM reference_attachment t JOIN asset s ON s.id = t.asset_id
            WHERE t.permission_request_id = @requestId AND t.kind = 'permission_letter' AND t.archived_at IS NULL AND s.archived_at IS NULL ORDER BY t.created_at DESC LIMIT 1
            """,
            new { requestId }, cancellationToken: cancellationToken));
        if (asset is null)
        {
            return null;
        }

        var blob = await blobs.GetAsync(asset.StorageKey, cancellationToken);
        return blob is null ? null : (blob, asset.FileName);
    }

    private static void ValidateLetter(PermissionLetterUpload? letter)
    {
        if (letter is null)
        {
            throw ApiException.Validation("Attach the signed letter.");
        }

        if (letter.Length == 0)
        {
            throw ApiException.Validation("The letter file is empty.");
        }

        if (letter.Length > MaxLetterBytes)
        {
            throw ApiException.Validation("The letter file is larger than 10 MB.");
        }

        if (!LetterTypes.Contains(letter.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("Attach the letter as a PDF, image, Word document, email or text file.");
        }
    }

    private async Task InsertLetterAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, RequestRow row, PermissionLetterUpload letter, string key, CancellationToken cancellationToken)
    {
        var fileName = SafeName(letter.FileName);
        var assetId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO asset (owner_id, book_id, kind, filename, mime, bytes, storage_key)
            VALUES (@owner, @bookId, 'document', @fileName, @mime, @bytes, @key) RETURNING id
            """,
            new { owner = currentUser.Id, row.BookId, fileName, mime = letter.ContentType, bytes = letter.Length, key },
            transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO reference_attachment (reference_id, permission_request_id, kind, asset_id, created_by)
            VALUES (@referenceId, @requestId, 'permission_letter', @assetId, @user)
            """,
            new { referenceId = row.ReferenceId, requestId = row.Id, assetId, user = currentUser.Id }, transaction, cancellationToken: cancellationToken));
    }

    private static void Require(RequestRow row, string status, string message)
    {
        if (row.Status != status)
        {
            throw ApiException.Conflict(message);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The file name as kept in the blob key: no path, only letters, digits, dots, dashes and underscores.
    private static string SafeName(string fileName)
    {
        var name = new string(Path.GetFileName(fileName.Replace('\\', '/')).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray()).Trim('.');
        if (name.Length == 0)
        {
            return "letter";
        }

        return name.Length > 100 ? name[^100..] : name;
    }

    private async Task Record(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, string action, CancellationToken ct, EventProvenance? provenance = null, object? detail = null) =>
        await SourceEvents.RecordAsync(connection, transaction, sourceId, currentUser.Id, action, detail ?? new { }, ct, provenance);

    private static async Task<RequestRow> LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid requestId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.reference_id AS ReferenceId, r.book_id AS BookId, p.status AS Status, p.letter AS Letter
            FROM permission_request p JOIN reference r ON r.id = p.reference_id
            WHERE p.id = @requestId AND r.archived_at IS NULL FOR UPDATE OF p
            """,
            new { requestId }, transaction, cancellationToken: ct))
        ?? throw ApiException.NotFound($"Permission request {requestId} was not found.");

    private static async Task<PermissionLetterFacts> FactsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, CancellationToken ct)
    {
        var head = await connection.QuerySingleAsync<HeadRow>(new CommandDefinition(
            """
            SELECT r.title AS Title, r.authors::text AS AuthorsJson, r.year AS Year, r.publisher AS SourcePublisher,
                   r.rights_holder AS RightsHolder, b.title AS BookTitle, b.author_name AS BookAuthor,
                   st.quoted_words AS QuotedWords, st.lyric_words AS LyricWords, st.figure_count AS Figures,
                   ps.publisher_name AS Publisher, ps.planned_year AS PlannedYear, ps.print_run AS PrintRun,
                   to_json(ps.formats)::text AS FormatsJson, ps.territory AS Territory
            FROM reference r
            JOIN book b ON b.id = r.book_id
            JOIN reference_stats st ON st.reference_id = r.id
            JOIN book_publication_settings ps ON ps.book_id = r.book_id
            WHERE r.id = @sourceId
            """,
            new { sourceId }, transaction, cancellationToken: ct));
        var uses = (await connection.QueryAsync<UseFact>(new CommandDefinition(
            """
            SELECT o.chapter_number::integer AS Chapter, x.locator AS Locator
            FROM reference_use_outline o JOIN reference_use x ON x.id = o.use_id
            WHERE o.reference_id = @sourceId AND o.kind IN ('quote', 'lyric', 'figure')
            ORDER BY o.book_order
            """,
            new { sourceId }, transaction, cancellationToken: ct))).ToList();
        var formats = head.FormatsJson is null ? [] : JsonSerializer.Deserialize<string[]>(head.FormatsJson)!;
        return new PermissionLetterFacts(
            head.RightsHolder, head.Title, CslNames.Label(head.AuthorsJson), head.Year, head.SourcePublisher, head.BookTitle, head.BookAuthor,
            head.QuotedWords, head.LyricWords, head.Figures,
            uses.Select(u => u.Locator?.Trim()).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.Ordinal).Select(l => l!).ToList(),
            uses.Where(u => u.Chapter is not null).Select(u => u.Chapter!.Value).Distinct().Order().ToList(),
            head.Publisher, head.PlannedYear, head.PrintRun, formats, head.Territory);
    }

    private sealed record RequestRow(Guid Id, Guid ReferenceId, Guid BookId, string Status, string Letter);

    private sealed record KeptLetter(Guid? AssetId);

    private sealed record LetterRow(string FileName, string StorageKey);

    private sealed record UseFact(int? Chapter, string? Locator);

    private sealed record HeadRow(
        string Title, string AuthorsJson, int? Year, string? SourcePublisher, string? RightsHolder, string BookTitle, string? BookAuthor,
        int QuotedWords, int LyricWords, int Figures, string? Publisher, int? PlannedYear, int? PrintRun, string? FormatsJson, string? Territory);

    private Task<Guid?> RequireRequestAsync(Guid requestId, CancellationToken cancellationToken, BookRole minimum = BookRole.Write)
        => access.RequireAsync(BookItem.PermissionRequest, requestId, minimum, cancellationToken, $"Permission request {requestId} was not found.");
}
