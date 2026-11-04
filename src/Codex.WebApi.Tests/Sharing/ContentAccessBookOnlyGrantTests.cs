using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Sharing;

// A grant on one book of a series (#80): write on Players Guide alone. The book is the user's to edit, but the
// series' shared knowledge base is only readable: changing a series entry changes every book in it, so it needs write
// on the series.
[TestFixture]
public class ContentAccessBookOnlyGrantTests : ApiFixture
{
    private static readonly Guid Editor = new("0199c0de-0000-7000-8000-0000000000e1");

    protected override TestUser SignedInAs => TestUser.Of(Editor);

    [OneTimeSetUp]
    public async Task GrantPlayersGuideAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "book-editor@example.test", "Book Editor", id: Editor);
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT INTO book_grant (book_id, user_id, role, granted_by) VALUES (@book, @Editor, 'write', @jd)",
            new { book = SeedIds.Books.PlayersGuide, Editor, jd = SeedIds.Users.Jd });
    }

    [Test]
    public async Task GetSeriesKbEntry_WhenOnlyABookOfTheSeriesIsShared_ShouldReturn200()
    {
        var entry = await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{SeedIds.KbEntries.Gravetouch}");

        entry!.Name.ShouldBe("Gravetouch");
    }

    [Test]
    public async Task EditSeriesKbEntry_WhenOnlyABookOfTheSeriesIsShared_ShouldReturn403Forbidden()
    {
        using var response = await Client.PatchAsJsonAsync(
            $"/api/kb/entries/{SeedIds.KbEntries.Gravetouch}", new PatchKbEntryRequest(null, "Edited.", null, null, null, null, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.Forbidden);
        (error.Code, error.Error).ShouldBe((ApiErrorCodes.Forbidden, "This book is shared with you to view only."));
    }

    [Test]
    public async Task CreateSeriesKbEntry_WhenOnlyABookOfTheSeriesIsShared_ShouldReturn403Forbidden()
    {
        using var response = await Client.PostAsJsonAsync(
            $"/api/books/{SeedIds.Series.ForsakenGods}/kb/entries", new CreateKbEntryRequest(SeedIds.KbCategories.ForsakenGodsSpells, "Ashfall"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
    }

    [Test]
    public async Task PromoteBookKbEntry_WhenOnlyABookOfTheSeriesIsShared_ShouldReturn403Forbidden()
    {
        // The book's own entry is the editor's to write, but moving it into the series writes to the series.
        await using var db = await OpenDatabaseAsync();
        var category = await TestRows.CategoryAsync(db, SeedIds.Books.PlayersGuide, "Rules");
        var entry = await TestRows.EntryAsync(db, SeedIds.Books.PlayersGuide, category, "Initiative");

        using var response = await Client.PostAsJsonAsync($"/api/kb/entries/{entry}/promote", new { });

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
        (await db.ExecuteScalarAsync<Guid>("SELECT book_id FROM kb_entry WHERE id = @entry", new { entry })).ShouldBe(SeedIds.Books.PlayersGuide);
    }

    [Test]
    public async Task SaveProse_WhenTheBookIsSharedToWrite_ShouldSave()
    {
        var scene = SeedIds.Scenes.PlayersGuideLatexScene;
        var before = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{scene}/prose"))!;

        using var response = await Client.PutAsJsonAsync(
            $"/api/scenes/{scene}/prose", new SaveProseRequest(before.Body + "\n\nThe editor adds a line.\n", before.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
