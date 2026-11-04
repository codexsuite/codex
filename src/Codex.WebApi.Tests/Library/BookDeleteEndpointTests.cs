using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.WebApi.Tests.Shell;
using Dapper;

namespace Codex.WebApi.Tests.Library;

[TestFixture]
public class BookDeleteEndpointTests : SchemaApiFixture
{
    private async Task<Guid> ArchivedBookAsync(string title, bool archived = true)
    {
        await using var db = await OpenDatabaseAsync();
        var id = await TestRows.ShelfBookAsync(db, title, words: 40, archived: archived);
        await TestRows.SourceAsync(db, id, "A source");
        return id;
    }

    private async Task<long> CountAsync(string table, Guid book)
    {
        await using var db = await OpenDatabaseAsync();
        var column = table == "book" ? "id" : "book_id";
        return await db.ExecuteScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {column} = @book", new { book });
    }

    [Test]
    public async Task Impact_WhenBookExists_ShouldCountChaptersScenesWordsAndSources()
    {
        var book = await ArchivedBookAsync("Impact");

        var impact = await Client.GetFromJsonAsync<BookDeleteImpactDto>($"/api/books/{book}/delete-impact");

        impact.ShouldNotBeNull();
        (impact.Archived, impact.Chapters, impact.Scenes, impact.Words, impact.Sources).ShouldBe((true, 1, 1, 40, 1));
    }

    [Test]
    public async Task Delete_WhenArchived_ShouldMarkTheBookDeletedAndKeepItsRows()
    {
        var book = await ArchivedBookAsync("Gone");

        var response = await Client.DeleteAsync($"/api/books/{book}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        foreach (var table in new[] { "book", "act", "chapter", "scene", "reference" })
        {
            (await CountAsync(table, book)).ShouldBeGreaterThan(0, table);
        }
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<bool>("SELECT deleted_at IS NOT NULL AND archived_at IS NOT NULL FROM book WHERE id = @book", new { book })).ShouldBeTrue();
    }

    [Test]
    public async Task Delete_WhenArchived_ShouldHideTheBookEverywhere()
    {
        var book = await ArchivedBookAsync("Hidden");
        (await Client.GetLibraryAsync("?status=archived")).Standalone.ShouldContain(b => b.Id == book);

        (await Client.DeleteAsync($"/api/books/{book}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.GetAsync($"/api/books/{book}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/api/books/{book}/delete-impact")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.DeleteAsync($"/api/books/{book}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetLibraryAsync("?status=archived")).Standalone.ShouldNotContain(b => b.Id == book);
        (await Client.GetLibraryAsync()).Standalone.ShouldNotContain(b => b.Id == book);
    }

    // A book with art placed in it: a figure, an act and a chapter opener, and a chosen layout.
    private async Task<(Guid Book, Guid Asset)> ArchivedBookWithArtAsync(string title)
    {
        await using var db = await OpenDatabaseAsync();
        var book = await TestRows.ShelfBookAsync(db, title, words: 40, archived: true);
        var asset = await TestRows.AssetAsync(db, book);
        var act = await TestRows.FirstActAsync(db, book);
        var chapter = await db.ExecuteScalarAsync<Guid>("SELECT id FROM chapter WHERE book_id = @book LIMIT 1", new { book });
        var scene = await db.ExecuteScalarAsync<Guid>("SELECT id FROM scene WHERE book_id = @book LIMIT 1", new { book });
        await db.ExecuteAsync(
            """
            INSERT INTO figure (scene_id, asset_id) VALUES (@scene, @asset);
            UPDATE act SET opener_asset_id = @asset WHERE id = @act;
            UPDATE chapter SET opener_asset_id = @asset WHERE id = @chapter;
            INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size)
            SELECT @book, t.id, v.id, '5.5x8.5' FROM layout_template t JOIN layout_template_version v ON v.template_id = t.id
            WHERE t.slug = 'novel-6x9' AND t.owner_id IS NULL;
            INSERT INTO book_layout (book_id, template_id) SELECT @book, template_id FROM book_layout_template WHERE book_id = @book;
            """,
            new { book, asset, act, chapter, scene });
        return (book, asset);
    }

    [Test]
    public async Task Delete_WhenBookHasFiguresOpenerArtAndALayout_ShouldKeepThemAll()
    {
        var (book, asset) = await ArchivedBookWithArtAsync("Illustrated");

        var response = await Client.DeleteAsync($"/api/books/{book}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<bool>("SELECT deleted_at IS NOT NULL FROM book WHERE id = @book", new { book })).ShouldBeTrue();
        (await check.ExecuteScalarAsync<long>("SELECT count(*) FROM asset WHERE id = @asset", new { asset })).ShouldBe(1);
        (await check.ExecuteScalarAsync<long>("SELECT count(*) FROM book_layout WHERE book_id = @book", new { book })).ShouldBe(1);
    }

    [Test]
    public async Task Delete_WhenNotArchived_ShouldAnswer409AndKeepTheBook()
    {
        var book = await ArchivedBookAsync("Live", archived: false);

        var response = await Client.DeleteAsync($"/api/books/{book}");

        var error = await response.ReadApiErrorAsync(HttpStatusCode.Conflict);
        error.Code.ShouldBe(ApiErrorCodes.Conflict);
        (await CountAsync("book", book)).ShouldBe(1);
    }

    [Test]
    public async Task Delete_WhenCopiedFromThisBook_ShouldKeepTheCopiesPointer()
    {
        var original = await ArchivedBookAsync("Original");
        Guid copy;
        await using (var db = await OpenDatabaseAsync())
        {
            copy = await TestRows.BookAsync(db, "Copy");
            await db.ExecuteAsync("UPDATE book SET copied_from_id = @original WHERE id = @copy", new { original, copy });
        }

        (await Client.DeleteAsync($"/api/books/{original}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<Guid?>("SELECT copied_from_id FROM book WHERE id = @copy", new { copy })).ShouldBe(original);
    }

    [Test]
    public async Task Delete_WhenUnknown_ShouldAnswer404()
    {
        var response = await Client.DeleteAsync($"/api/books/{Guid.NewGuid()}");

        var error = await response.ReadApiErrorAsync(HttpStatusCode.NotFound);
        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
