using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Contracts;
using Codex.Contracts.Import;
using Codex.Contracts.Jobs;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Print;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// J20: the seeded Product Engineering book is backed up (Export source), the zip is imported as a Codex backup, and the
// restored book matches it: structure, words, Knowledge base, labels, and References with their sign-offs and permission
// requests, so clearance reads the same. The source book is not touched. Files that are not a backup are refused.
[TestFixture]
public class BackupRoundTripTests : ExportApiFixture
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;

    private byte[] _zip = [];
    private Guid _restored;

    [Test, Order(1)]
    public async Task ExportSource_WhenQueued_ShouldWriteTheBackupFilesAndSayWhatItLeftOut()
    {
        using var queue = await Client.PostAsync($"/api/books/{Book}/export-source", null);
        var job = (await queue.Content.ReadFromJsonAsync<JobDto>())!;

        var export = await SettledAsync(job.Id);

        export.Status.ShouldBe("succeeded", export.Error);
        export.Notes.ShouldNotBeNull().ShouldContain("1 pending AI draft is not included");
        export.Notes.ShouldContain("0 images not included");
        _zip = await Client.GetByteArrayAsync(export.FileUrl);
        using var zip = new ZipArchive(new MemoryStream(_zip));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        foreach (var file in new[] { "book.json", "structure.json", "knowledge-base.json", "references.json", "labels.json", "manifest.json" })
        {
            names.ShouldContain(file);
        }

        names.Count(n => n.StartsWith("manuscript/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)).ShouldBe(15);
        names.ShouldContain(n => n.StartsWith("manuscript/act-01-", StringComparison.Ordinal) && n.Contains("/chapter-01-", StringComparison.Ordinal) && n.EndsWith("/scene-01.md", StringComparison.Ordinal));
    }

    [Test, Order(2)]
    public async Task Preview_WhenTheBackupIsPicked_ShouldCountWhatTheBookHolds()
    {
        using var form = ImportForms.Form([("product-engineering.zip", _zip)], source: "codex_zip");
        var response = await Client.PostAsync("/api/imports/preview", form);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var preview = (await response.Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        preview.BookTitle.ShouldBe("Product Engineering");
        preview.Words.ShouldBe(13703);
        Counts(preview).ShouldBe(new Dictionary<string, int>
        {
            ["Chapters"] = 10, ["Scenes"] = 15, ["Knowledge base entries"] = 12, ["Sources"] = 12, ["Cited sources"] = 10, ["Labels"] = 4,
        }, ignoreOrder: true);
    }

    [Test, Order(3)]
    public async Task Import_WhenTheBackupIsRestored_ShouldCreateAnEqualBookTitledRestored()
    {
        await using var db = await OpenDatabaseAsync();
        var before = await Fingerprint(db, Book);
        using var form = ImportForms.Form([("product-engineering.zip", _zip)], source: "codex_zip");

        var response = await Client.PostAsync("/api/imports", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        _restored = summary.BookId;
        (summary.Chapters, summary.Scenes, summary.Words, summary.Entries, summary.Sources, summary.CitedSources, summary.Labels)
            .ShouldBe((10, 15, 13703, 12, 12, 10, 4));
        (await db.ExecuteScalarAsync<string>("SELECT title FROM book WHERE id = @_restored", new { _restored })).ShouldBe("Product Engineering (restored)");
        (await Fingerprint(db, Book)).ShouldBe(before, "the source book is unchanged");
        var stats = await db.QuerySingleAsync<(int Words, int Chapters, int Scenes)>(
            """
            SELECT (SELECT word_count FROM book_stats WHERE book_id = @_restored),
                   (SELECT count(*)::int FROM chapter WHERE book_id = @_restored),
                   (SELECT count(*)::int FROM scene WHERE book_id = @_restored AND archived_at IS NULL)
            """,
            new { _restored });
        stats.ShouldBe((13703, 10, 15));
    }

    [Test, Order(4)]
    public async Task Import_WhenRestored_ShouldKeepStructureBeatsKnowledgeBaseAndLabels()
    {
        await using var db = await OpenDatabaseAsync();

        foreach (var (name, sql) in new[]
        {
            ("act titles", "SELECT coalesce(string_agg(title, '|' ORDER BY position), '') FROM act WHERE book_id = @id"),
            ("chapter titles and slugs", "SELECT string_agg(c.title || ':' || c.slug || ':' || c.is_numbered, '|' ORDER BY a.position, c.position) FROM chapter c JOIN act a ON a.id = c.act_id WHERE c.book_id = @id"),
            ("scene status", "SELECT string_agg(coalesce(title, '') || ':' || status || ':' || coalesce(summary, ''), '|' ORDER BY title, status, summary) FROM scene WHERE book_id = @id"),
            ("beats", "SELECT string_agg(b.body || ':' || coalesce(b.target_word_count, 0), '|' ORDER BY b.body) FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = @id AND b.archived_at IS NULL"),
            ("kb entries", "SELECT string_agg(e.name || ':' || array_to_string(e.aliases, ',') || ':' || e.fields::text, '|' ORDER BY e.name) FROM kb_entry e WHERE e.book_id = @id AND e.archived_at IS NULL"),
            ("kb categories", "SELECT string_agg(name, '|' ORDER BY position) FROM kb_category WHERE book_id = @id"),
            ("scene kb links", "SELECT count(*)::text FROM scene_kb x JOIN scene s ON s.id = x.scene_id WHERE s.book_id = @id AND x.archived_at IS NULL"),
            ("kb additions", "SELECT count(*)::text FROM kb_progression p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @id AND p.archived_at IS NULL"),
            ("labels", "SELECT string_agg(l.name || ':' || (SELECT count(*) FROM scene_label x WHERE x.label_id = l.id AND x.archived_at IS NULL), '|' ORDER BY l.name) FROM label l WHERE l.book_id = @id AND l.archived_at IS NULL"),
            ("prose", "SELECT string_agg(md5(regexp_replace(p.body, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', 'ID', 'g')), '|' ORDER BY s.title, s.summary, p.word_count) FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @id AND p.body <> ''"),
        })
        {
            var original = await db.ExecuteScalarAsync<string?>(sql, new { id = Book });
            var restored = await db.ExecuteScalarAsync<string?>(sql, new { id = _restored });
            restored.ShouldBe(original, name);
        }
    }

    [Test, Order(5)]
    public async Task Import_WhenRestored_ShouldReadClearanceAsTheOriginalDoesExceptForTheUsesOfThePendingDraft()
    {
        await using var db = await OpenDatabaseAsync();
        const string clearance =
            """
            SELECT r.title AS Title, c.status || ':' || c.reason || ':' || c.use_count AS Verdict
            FROM reference_clearance c JOIN reference r ON r.id = c.reference_id WHERE c.book_id = @id
            """;
        var original = (await db.QueryAsync<(string Title, string Verdict)>(clearance, new { id = Book })).ToDictionary(r => r.Title, r => r.Verdict);
        var restored = (await db.QueryAsync<(string Title, string Verdict)>(clearance, new { id = _restored })).ToDictionary(r => r.Title, r => r.Verdict);

        // The pending draft in Ch 5 · Sc 2 is not in a backup, and three sources were used by it: the DevOps report only
        // there, and Accelerate and the Monetary Management paper once more than the manuscript does.
        var draft = new[] { "2024 State of DevOps Report", "Accelerate", "Problems of Monetary Management: The U.K. Experience" };
        foreach (var title in original.Keys.Except(draft))
        {
            restored[title].ShouldBe(original[title], title);
        }

        restored["2024 State of DevOps Report"].ShouldBe("not_cited:no_markers:0");
        restored["Accelerate"].ShouldBe("cleared:signed_off:1");
        restored["Problems of Monetary Management: The U.K. Experience"].ShouldBe("needs_check:ready_to_sign_off:1");
        var summary = await db.QuerySingleAsync<(int Cleared, int Risks, int Cited, int Flags)>(
            "SELECT cleared_count, risk_count, source_count - not_cited_count, open_quote_flags FROM book_reference_summary WHERE book_id = @id", new { id = _restored });
        summary.ShouldBe((5, 3, 10, 2));
        (await db.ExecuteScalarAsync<string>(
            "SELECT string_agg(p.status, ',' ORDER BY p.status) FROM permission_request p JOIN reference r ON r.id = p.reference_id WHERE r.book_id = @id", new { id = _restored }))
            .ShouldBe(await db.ExecuteScalarAsync<string>(
                "SELECT string_agg(p.status, ',' ORDER BY p.status) FROM permission_request p JOIN reference r ON r.id = p.reference_id WHERE r.book_id = @id", new { id = Book }));
    }

    [Test, Order(6)]
    public async Task Import_WhenRestored_ShouldPointEveryMarkerAtAUseOfTheNewBook()
    {
        await using var db = await OpenDatabaseAsync();

        var dangling = await db.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM (
                SELECT (regexp_matches(p.body, '\(ref:([0-9a-f-]{36})\)', 'g'))[1]::uuid AS use_id
                FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @id) m
            WHERE NOT EXISTS (SELECT 1 FROM reference_use u WHERE u.id = m.use_id AND u.book_id = @id)
            """,
            new { id = _restored });
        var markers = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @id AND p.body LIKE '%(ref:%'", new { id = _restored });

        dangling.ShouldBe(0);
        markers.ShouldBeGreaterThan(0);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose_revision r JOIN scene s ON s.id = r.scene_id WHERE s.book_id = @id AND r.source <> 'import'", new { id = _restored })).ShouldBe(0);
    }

    [Test, Order(7)]
    public async Task Import_WhenTheTitleExistsAgain_ShouldRestoreAnotherCopy()
    {
        using var form = ImportForms.Form([("again.zip", _zip)], source: "codex_zip");

        var response = await Client.PostAsync("/api/imports", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        summary.BookId.ShouldNotBe(_restored);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book WHERE title = 'Product Engineering (restored)'")).ShouldBe(2);
    }

    [Test, Order(8)]
    public async Task Import_WhenTheZipIsNotABackup_ShouldAnswer400AndWriteNothing()
    {
        await using var db = await OpenDatabaseAsync();
        var books = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");

        var notBackup = await Client.PostAsync("/api/imports", ImportForms.Form([("notes.zip", TestZip.Of(("notes/one.md", "# One")))], source: "codex_zip"));
        var newer = await Client.PostAsync("/api/imports", ImportForms.Form([("newer.zip", TestZip.Of(("book.json", """{"formatVersion":99,"title":"X"}""")))], source: "codex_zip"));
        var preview = await Client.PostAsync("/api/imports/preview", ImportForms.Form([("notes.zip", TestZip.Of(("notes/one.md", "# One")))], source: "codex_zip"));

        (await notBackup.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldStartWith("This is not a Codex backup");
        (await newer.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldStartWith("This backup was made by a newer Codex");
        (await preview.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldStartWith("This is not a Codex backup");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book")).ShouldBe(books);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book_import WHERE source = 'codex_zip' AND status = 'importing'")).ShouldBe(0);
    }

    [Test, Order(9)]
    public async Task ExportSource_WhenOneForTheBookIsRunning_ShouldConflict()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("INSERT INTO export_job (book_id, kind, status, step, started_at) VALUES (@Book, 'source', 'running', 'Running', now())", new { Book });
        }

        using var response = await Client.PostAsync($"/api/books/{Book}/export-source", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
    }

    [Test, Order(10)]
    public void ExportSource_WhenTheBackupIsWritten_ShouldCarryEveryInstantAtOffsetZero()
    {
        using var zip = new ZipArchive(new MemoryStream(_zip));
        using var reader = new StreamReader(zip.GetEntry("references.json")!.Open());
        var instants = Regex.Matches(Regex.Unescape(reader.ReadToEnd()), "\"[a-z_]+_at\": \"([^\"]+)\"");

        instants.Count.ShouldBeGreaterThan(0);
        foreach (var instant in instants.Select(m => m.Groups[1].Value))
        {
            instant.ShouldEndWith("+00:00");
        }
    }

    [TestCase("""{"sources":[{"id":"a","accessed_on":"19/10/2026"}]}""", "sources.accessed_on")]
    [TestCase("""{"sources":[{"id":"a","accessed_on":"2026-10-19T00:00:00Z"}]}""", "sources.accessed_on")]
    [TestCase("""{"sources":[{"id":"a","signed_off_at":"2026-10-19T08:00:00"}]}""", "sources.signed_off_at")]
    [TestCase("""{"sources":[{"id":"a","fair_use_decided_at":"yesterday"}]}""", "sources.fair_use_decided_at")]
    [TestCase("""{"permission_requests":[{"id":"a","sent_at":"2026-10-19T08:00:00+0700"}]}""", "permission_requests.sent_at")]
    [TestCase("""{"sources":[{"id":"a","created_at":12}]}""", "sources.created_at")]
    public async Task Preview_WhenADateFieldIsNotValid_ShouldAnswer400NamingTheField(string references, string field)
    {
        var zip = TestZip.Of(("book.json", """{"formatVersion":1,"title":"X"}"""), ("references.json", references));

        var response = await Client.PostAsync("/api/imports/preview", ImportForms.Form([("bad.zip", zip)], source: "codex_zip"));

        var error = (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error;
        error.ShouldContain(field);
    }

    [Test]
    public async Task Preview_WhenDatesAreExactDatesAndInstantsWithOffsets_ShouldAccept()
    {
        var references = """
            {"sources":[{"id":"a","accessed_on":"2026-10-19","created_at":"2026-10-19T08:12:33.123456+00:00","signed_off_at":"2026-10-19T08:12:33Z","fair_use_decided_at":null,"checked_at":"2026-10-19T15:12:33+07:00"}]}
            """;
        var zip = TestZip.Of(("book.json", """{"formatVersion":1,"title":"X"}"""), ("references.json", references));

        var response = await Client.PostAsync("/api/imports/preview", ImportForms.Form([("good.zip", zip)], source: "codex_zip"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, int> Counts(ImportPreviewDto preview) =>
        preview.Creates.Where(c => c.Item is "Chapters" or "Scenes" or "Knowledge base entries" or "Sources" or "Cited sources" or "Labels")
            .ToDictionary(c => c.Item, c => c.Count);

    // Everything the restore must not change in the source book.
    private static async Task<string> Fingerprint(Npgsql.NpgsqlConnection db, Guid book) =>
        await db.ExecuteScalarAsync<string>(
            """
            SELECT md5(coalesce((SELECT string_agg(p.scene_id::text || p.version || md5(p.body), ',' ORDER BY p.scene_id) FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @book), '')
                    || (SELECT count(*) FROM reference WHERE book_id = @book)::text || (SELECT count(*) FROM reference_use WHERE book_id = @book)::text
                    || (SELECT count(*) FROM kb_entry WHERE book_id = @book)::text)
            """,
            new { book }) ?? string.Empty;
}
