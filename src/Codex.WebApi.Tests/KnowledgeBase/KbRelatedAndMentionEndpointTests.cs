using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// Related links, Make mention and rename-keeps-alias, in order, on one cloned schema.
[TestFixture]
public class KbRelatedAndMentionEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _anna;
    private Guid _ben;
    private Guid _outsider;
    private Guid _scene;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        var other = await AddBookAsync("Elsewhere");
        var people = await AddCategoryAsync(_book, "People");
        _anna = await AddEntryAsync(_book, people, "Anna", "a0", ["Annie"]);
        _ben = await AddEntryAsync(_book, people, "Ben", "a1");
        _outsider = await AddEntryAsync(other, await AddCategoryAsync(other, "People"), "Stranger");
        _scene = await AddSceneAsync(_book, await AddChapterAsync(_book), "a0", "She met `Anna` and [Ben](kb:" + _ben + ").\n\nThen Annie waved; Anna smiled.");
    }

    [Test, Order(1)]
    public async Task Related_WhenAdded_ShouldShowOnBothEntries()
    {
        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/related", new AddKbRelatedRequest(_ben));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<KbEntryDto>())!.Related.Select(r => r.Name).ShouldBe(["Ben"]);
        (await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{_ben}"))!.Related.Select(r => r.Name).ShouldBe(["Anna"]);
        // Linking again from the other side is the same pair.
        (await Client.PostAsJsonAsync($"/api/kb/entries/{_ben}/related", new AddKbRelatedRequest(_anna))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link")).ShouldBe(1);
    }

    [Test, Order(2)]
    public async Task Related_WhenSelfOutOfScopeOrUnknown_ShouldReject()
    {
        var self = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/related", new AddKbRelatedRequest(_anna));
        var outside = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/related", new AddKbRelatedRequest(_outsider));
        var unknown = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/related", new AddKbRelatedRequest(Guid.NewGuid()));

        (await ApiErrors.ReadApiErrorAsync(self, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(outside, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(unknown, HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link")).ShouldBe(1);
    }

    [Test, Order(3)]
    public async Task Unlink_ShouldRemoveThePairFromBothEntries()
    {
        var response = await Client.DeleteAsync($"/api/kb/entries/{_ben}/related/{_anna}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<KbEntryDto>())!.Related.ShouldBeEmpty();
        (await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{_anna}"))!.Related.ShouldBeEmpty();
        (await Client.DeleteAsync($"/api/kb/entries/{_ben}/related/{_anna}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // The pair's row is kept, archived.
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link WHERE archived_at IS NOT NULL")).ShouldBe(1);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link WHERE archived_at IS NULL")).ShouldBe(0);
    }

    [Test, Order(4)]
    public async Task MakeMention_ShouldWrapTheFirstPlainUseOutsideCode()
    {
        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/make-mention", new MakeKbMentionRequest(_scene));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene}/prose"))!;
        prose.Body.ShouldBe($"She met `Anna` and [Ben](kb:{_ben}).\n\nThen Annie waved; [Anna](kb:{_anna}) smiled.");
        prose.Version.ShouldBe(2);
    }

    [Test, Order(5)]
    public async Task MakeMention_WhenNothingPlainIsLeft_ShouldReturn400AndLeaveProse()
    {
        // "Annie" is an alias with one plain use left.
        (await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/make-mention", new MakeKbMentionRequest(_scene))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/make-mention", new MakeKbMentionRequest(_scene));
        var unknownScene = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/make-mention", new MakeKbMentionRequest(Guid.NewGuid()));

        (await ApiErrors.ReadApiErrorAsync(again, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(unknownScene, HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene}/prose"))!.Version.ShouldBe(3);
    }

    [Test, Order(6)]
    public async Task Rename_ShouldKeepTheOldNameAsAnAlias()
    {
        var response = await Client.PatchAsJsonAsync($"/api/kb/entries/{_anna}", new PatchKbEntryRequest("Anna Vey", null, null, null, null, null, null, null));

        var entry = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        entry.Name.ShouldBe("Anna Vey");
        entry.Aliases.ShouldBe(["Annie", "Anna"]);
        entry.Slug.ShouldBe("anna");
    }

    [Test, Order(7)]
    public async Task Rename_WhenOldNameBelongsToAnotherEntry_ShouldSkipTheAliasAndRenamingBackDropsIt()
    {
        var back = await Client.PatchAsJsonAsync($"/api/kb/entries/{_anna}", new PatchKbEntryRequest("Anna", null, null, null, null, null, null, null));
        (await back.Content.ReadFromJsonAsync<KbEntryDto>())!.Aliases.ShouldBe(["Annie", "Anna Vey"]);

        // Ben answers to the name Anna is about to leave: no alias for it, and no error.
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE kb_entry SET aliases = ARRAY['Anna'] WHERE id = @id", new { id = _ben });
        var renamed = await Client.PatchAsJsonAsync($"/api/kb/entries/{_anna}", new PatchKbEntryRequest("Ann", null, null, null, null, null, null, null));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await renamed.Content.ReadFromJsonAsync<KbEntryDto>())!.Aliases.ShouldBe(["Annie", "Anna Vey"]);
    }

    [Test, Order(8)]
    public async Task Related_WhenAddedAgainAfterRemove_ShouldReviveTheSameRow()
    {
        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/related", new AddKbRelatedRequest(_ben));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<KbEntryDto>())!.Related.Select(r => r.Id).ShouldBe([_ben]);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link")).ShouldBe(1);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link WHERE archived_at IS NULL")).ShouldBe(1);
    }
}
