using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Import;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Import;

// Writes a Codex backup (CodexBackupParser) as a new book in one transaction through IProseStore, like ImportWriter:
// every row gets a new id (BackupIds), the rows the prose points at are inserted before it is written, and each scene
// gets one 'import' revision (history is not in a backup). Sources come back with their uses, sign-offs, permission
// requests and quote flags, so clearance reads as it did. The book is titled as in the backup, with " (restored)"
// added when the author already has a book of that name. The JSON files are table-shaped, so each table is one
// jsonb_populate_recordset insert.
[RegisterSingleton]
public sealed class BackupWriter(IProseStore proseStore, ICurrentUser currentUser)
{
    public const string Suffix = " (restored)";

    private const string Origin = "import";

    public async Task<ImportSummaryDto> WriteAsync(string source, ImportPlan plan, ImportPreviewDto mapping, CancellationToken cancellationToken)
    {
        var data = plan.Backup!;
        var ids = BackupIds.From(data);
        var importId = Guid.CreateVersion7();
        var bookId = Guid.CreateVersion7();
        var edits = new List<(Guid, int, Func<string, string>)>();
        foreach (var scene in Rows(data.Structure, "scenes"))
        {
            var body = ids.Remap(ids.CleanBody(data.Bodies[Text(scene, "file")!]));
            if (body.Length > 0)
            {
                edits.Add((ids.New(Text(scene, "id")!), 0, _ => body));
            }
        }

        ImportSummaryDto? summary = null;
        var options = new ProseWriteOptions
        {
            BeforeWrites = (connection, transaction, ct) => InsertRowsAsync(connection, transaction, importId, bookId, source, plan, ids, mapping, ct),
            InTransaction = async (connection, transaction, ct) =>
                summary = await FinishAsync(connection, transaction, importId, bookId, source, plan, ct),
        };
        try
        {
            await proseStore.UpdateManyAsync(edits, Origin, options, cancellationToken);
        }
        catch (PostgresException e)
        {
            throw ApiException.Validation($"The backup could not be restored: {e.MessageText}");
        }

        return summary!;
    }

    private async Task InsertRowsAsync(
        DbConnection connection, DbTransaction transaction, Guid importId, Guid bookId, string source, ImportPlan plan, BackupIds ids,
        ImportPreviewDto mapping, CancellationToken ct)
    {
        var data = plan.Backup!;
        var owner = currentUser.Id;
        var title = plan.Book.Title;
        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE owner_id = @owner AND kind = 'book' AND deleted_at IS NULL AND title = @title)", new { owner, title }, transaction, cancellationToken: ct)))
        {
            title += Suffix;
        }

        // The backup brings its own acts, so the book trigger must not add the untitled one (a book keeps one act at
        // commit, not before).
        await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('codex.skip_default_act', 'on', true)", transaction: transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book (id, owner_id, title, subtitle, description, author_name, cover_palette, status, target_word_count, style_guide)
            VALUES (@bookId, @owner, @title, @subtitle, @description, @author, @palette, @status, @target, @guide)
            """,
            new
            {
                bookId, owner, title, subtitle = plan.Book.Subtitle, description = plan.Book.Description, author = plan.Book.Author,
                palette = Text(data.Book, "coverPalette"), status = Text(data.Book, "status") ?? "planning",
                target = data.Book["targetWordCount"] is JsonValue t && t.TryGetValue<int>(out var words) ? words : (int?)null, guide = plan.Book.StyleGuide,
            },
            transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('codex.skip_default_act', 'off', true)", transaction: transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book_import (id, book_id, source, file_name, status, mapping, created_by)
            VALUES (@importId, @bookId, @source, @fileName, 'importing', @mapping::jsonb, @owner)
            """,
            new { importId, bookId, source, fileName = plan.FileName, mapping = JsonSerializer.Serialize(mapping, JsonSerializerOptions.Web), owner },
            transaction, cancellationToken: ct));
        if (data.Book["publication"] is JsonObject publication)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO publication_settings (book_id, citation_style, rules_name, quote_word_limit, publisher_name, planned_year, print_run, formats, territory)
                SELECT @bookId, p.citation_style, p.rules_name, p.quote_word_limit, p.publisher_name, p.planned_year, p.print_run, p.formats, p.territory
                FROM jsonb_populate_record(NULL::publication_settings, @json::jsonb) p
                """,
                new { bookId, json = publication.ToJsonString() }, transaction, cancellationToken: ct));
        }

        async Task InsertAsync(string sql, JsonObject file, string property) =>
            await connection.ExecuteAsync(new CommandDefinition(
                sql, new { bookId, owner, json = ids.Remap(Array(file, property)) }, transaction, cancellationToken: ct));

        var structure = data.Structure;
        var knowledge = data.KnowledgeBase;
        var references = data.References;
        await InsertAsync(
            """
            INSERT INTO act (id, book_id, position, title, summary)
            SELECT a.id, @bookId, a.position, coalesce(a.title, ''), a.summary FROM jsonb_populate_recordset(NULL::act, @json::jsonb) a
            """, structure, "acts");
        await InsertAsync(
            """
            INSERT INTO chapter (id, book_id, act_id, position, title, slug, summary, notes, is_numbered)
            SELECT c.id, @bookId, c.act_id, c.position, c.title, c.slug, c.summary, c.notes, coalesce(c.is_numbered, true)
            FROM jsonb_populate_recordset(NULL::chapter, @json::jsonb) c
            """, structure, "chapters");
        await InsertAsync(
            """
            INSERT INTO scene (id, book_id, chapter_id, position, title, summary, plan_notes, status, import_ref)
            SELECT s.id, @bookId, s.chapter_id, s.position, s.title, s.summary, s.plan_notes, coalesce(s.status, 'outline'), s.import_ref
            FROM jsonb_populate_recordset(NULL::scene, @json::jsonb) s
            """, structure, "scenes");
        await InsertAsync(
            """
            INSERT INTO beat (id, scene_id, body, target_word_count, is_hidden, model)
            SELECT b.id, b.scene_id, coalesce(b.body, ''), b.target_word_count, coalesce(b.is_hidden, false), b.model
            FROM jsonb_populate_recordset(NULL::beat, @json::jsonb) b
            """, structure, "beats");
        await InsertAsync(
            """
            INSERT INTO kb_category (id, book_id, name, tag, color, icon, position, field_defs)
            SELECT k.id, @bookId, k.name, k.tag, k.color, k.icon, k.position, coalesce(k.field_defs, '[]')
            FROM jsonb_populate_recordset(NULL::kb_category, @json::jsonb) k
            """, knowledge, "categories");
        await InsertAsync(
            """
            INSERT INTO kb_entry (id, book_id, category_id, name, slug, position, aliases, tags, summary, body, fields, custom_fields,
                                  always_in_context, exclude_from_auto_context, import_url, import_archive_url, external_id)
            SELECT e.id, @bookId, e.category_id, e.name, e.slug, e.position, coalesce(e.aliases, '{}'), coalesce(e.tags, '{}'), e.summary,
                   coalesce(e.body, ''), coalesce(e.fields, '{}'), coalesce(e.custom_fields, '[]'),
                   coalesce(e.always_in_context, false), coalesce(e.exclude_from_auto_context, false), e.import_url, e.import_archive_url, e.external_id
            FROM jsonb_populate_recordset(NULL::kb_entry, @json::jsonb) e
            """, knowledge, "entries");
        await InsertAsync(
            """
            INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
            SELECT least(l.entry_a_id, l.entry_b_id), greatest(l.entry_a_id, l.entry_b_id)
            FROM jsonb_populate_recordset(NULL::kb_entry_link, @json::jsonb) l
            WHERE EXISTS (SELECT 1 FROM kb_entry WHERE id = l.entry_a_id AND book_id = @bookId)
              AND EXISTS (SELECT 1 FROM kb_entry WHERE id = l.entry_b_id AND book_id = @bookId)
            ON CONFLICT DO NOTHING
            """, knowledge, "links");
        await InsertAsync(
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id)
            SELECT x.scene_id, x.kb_entry_id FROM jsonb_populate_recordset(NULL::scene_kb, @json::jsonb) x
            WHERE EXISTS (SELECT 1 FROM kb_entry WHERE id = x.kb_entry_id AND book_id = @bookId)
            """, structure, "scene_kb");
        await InsertAsync(
            """
            INSERT INTO beat_kb (beat_id, kb_entry_id)
            SELECT x.beat_id, x.kb_entry_id FROM jsonb_populate_recordset(NULL::beat_kb, @json::jsonb) x
            WHERE EXISTS (SELECT 1 FROM kb_entry WHERE id = x.kb_entry_id AND book_id = @bookId)
            """, structure, "beat_kb");
        await InsertAsync(
            """
            INSERT INTO kb_progression (id, kb_entry_id, scene_id, body)
            SELECT p.id, p.kb_entry_id, p.scene_id, p.body FROM jsonb_populate_recordset(NULL::kb_progression, @json::jsonb) p
            WHERE EXISTS (SELECT 1 FROM kb_entry WHERE id = p.kb_entry_id AND book_id = @bookId)
            """, knowledge, "additions");
        await InsertAsync(
            """
            INSERT INTO label (id, book_id, name, color, position)
            SELECT l.id, @bookId, l.name, l.color, l.position FROM jsonb_populate_recordset(NULL::label, @json::jsonb) l
            """, data.Labels, "labels");
        await InsertAsync(
            """
            INSERT INTO scene_label (scene_id, label_id)
            SELECT x.scene_id, x.label_id FROM jsonb_populate_recordset(NULL::scene_label, @json::jsonb) x
            WHERE EXISTS (SELECT 1 FROM label WHERE id = x.label_id AND book_id = @bookId)
            """, data.Labels, "scene_labels");

        // Sources keep their sign-offs (signed by whoever restores) and dates, so clearance reads as it did.
        await InsertAsync(
            """
            INSERT INTO reference (id, book_id, kind, title, authors, year, publisher, container_title, volume, issue, page_range, edition, isbn, doi, url,
                                   accessed_on, licence, rights_holder, permissions_contact, fair_use_decided_at, fair_use_decided_by, fair_use_note,
                                   signed_off_at, signed_off_by, kb_entry_id, cite_key, csl_extra, external_provider, external_key, created_by, created_at)
            SELECT r.id, @bookId, r.kind, coalesce(r.title, ''), coalesce(r.authors, '[]'), r.year, r.publisher, r.container_title, r.volume, r.issue,
                   r.page_range, r.edition, r.isbn, r.doi, r.url, r.accessed_on, coalesce(r.licence, 'unknown'), r.rights_holder, r.permissions_contact,
                   r.fair_use_decided_at, CASE WHEN r.fair_use_decided_at IS NOT NULL THEN @owner END, r.fair_use_note,
                   r.signed_off_at, CASE WHEN r.signed_off_at IS NOT NULL THEN @owner END,
                   (SELECT e.id FROM kb_entry e WHERE e.id = r.kb_entry_id AND e.book_id = @bookId),
                   r.cite_key, coalesce(r.csl_extra, '{}'), r.external_provider, r.external_key, @owner, coalesce(r.created_at, now())
            FROM jsonb_populate_recordset(NULL::reference, @json::jsonb) r
            """, references, "sources");
        await InsertAsync(
            """
            INSERT INTO reference_use (id, book_id, reference_id, scene_id, kind, locator, excerpt, changed_at, created_by, created_at)
            SELECT u.id, @bookId, u.reference_id, u.scene_id, coalesce(u.kind, 'quote'), u.locator, coalesce(u.excerpt, ''),
                   coalesce(u.changed_at, now()), @owner, coalesce(u.created_at, now())
            FROM jsonb_populate_recordset(NULL::reference_use, @json::jsonb) u
            """, references, "uses");
        await InsertAsync(
            """
            INSERT INTO permission_request (id, reference_id, status, to_address, letter, sent_at, remind_at, last_reminded_at, responded_at,
                                            fee_amount, fee_currency, credit_line, terms, created_by, created_at)
            SELECT p.id, p.reference_id, p.status, p.to_address, p.letter, p.sent_at, p.remind_at, p.last_reminded_at, p.responded_at,
                   p.fee_amount, p.fee_currency, p.credit_line, p.terms, @owner, coalesce(p.created_at, now())
            FROM jsonb_populate_recordset(NULL::permission_request, @json::jsonb) p
            """, references, "permission_requests");
        await InsertAsync(
            """
            INSERT INTO quote_flag (id, book_id, scene_id, excerpt, reason, reference_id, match_score, locator, status, resolved_use_id, resolved_at, created_at)
            SELECT f.id, @bookId, f.scene_id, f.excerpt, f.reason,
                   (SELECT r.id FROM reference r WHERE r.id = f.reference_id AND r.book_id = @bookId), f.match_score, f.locator, f.status,
                   (SELECT u.id FROM reference_use u WHERE u.id = f.resolved_use_id AND u.book_id = @bookId), f.resolved_at, coalesce(f.created_at, now())
            FROM jsonb_populate_recordset(NULL::quote_flag, @json::jsonb) f
            ON CONFLICT DO NOTHING
            """, references, "quote_flags");
    }

    private static async Task<ImportSummaryDto> FinishAsync(
        DbConnection connection, DbTransaction transaction, Guid importId, Guid bookId, string source, ImportPlan plan, CancellationToken ct)
    {
        var counts = await connection.QuerySingleAsync<(int Acts, int Chapters, int Scenes, int Words, int Entries, int Sources, int Cited, int Labels)>(new CommandDefinition(
            """
            SELECT (SELECT count(*)::int FROM act WHERE book_id = @bookId),
                   (SELECT count(*)::int FROM chapter WHERE book_id = @bookId),
                   (SELECT count(*)::int FROM scene WHERE book_id = @bookId AND archived_at IS NULL),
                   (SELECT coalesce(sum(p.word_count), 0)::int FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @bookId),
                   (SELECT count(*)::int FROM kb_entry WHERE book_id = @bookId),
                   (SELECT count(*)::int FROM reference WHERE book_id = @bookId),
                   (SELECT count(DISTINCT reference_id)::int FROM reference_use WHERE book_id = @bookId AND archived_at IS NULL),
                   (SELECT count(*)::int FROM label WHERE book_id = @bookId AND archived_at IS NULL)
            """,
            new { bookId }, transaction, cancellationToken: ct));
        var summary = new ImportSummaryDto(
            importId, bookId, source, plan.FileName, counts.Acts, counts.Chapters, counts.Scenes, counts.Words, counts.Entries,
            plan.Skipped.Select(s => new ImportSkippedDto(s.Path, s.Reason)).ToList(), counts.Sources, counts.Cited, counts.Labels);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE book_import SET status = 'done', result = @result::jsonb, finished_at = now() WHERE id = @importId",
            new { importId, result = JsonSerializer.Serialize(summary, JsonSerializerOptions.Web) }, transaction, cancellationToken: ct));
        return summary;
    }

    private static string Array(JsonObject file, string property) => file[property] is JsonArray array ? array.ToJsonString() : "[]";

    private static IEnumerable<JsonObject> Rows(JsonObject file, string property) => file[property] is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string? Text(JsonObject row, string property) => row[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
