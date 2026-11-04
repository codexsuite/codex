using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// J21 on the API, in order on one cloned schema: rename and alias collisions, alias check, merge, category rename and delete.
[TestFixture]
public class KbMaintenanceEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _laws;
    private Guid _research;
    private Guid _goodhart;
    private Guid _conway;
    private Guid _dora;
    private Guid _fourKeys;
    private Guid _scene1;
    private Guid _scene2;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        _laws = await AddCategoryAsync(_book, "Laws");
        _research = await AddCategoryAsync(_book, "Research", "a1");
        _goodhart = await AddEntryAsync(_book, _laws, "Goodhart's Law");
        _conway = await AddEntryAsync(_book, _laws, "Conway's Law", "a1", ["Conway"]);
        _dora = await AddEntryAsync(_book, _research, "DORA metrics", "a0", ["DM"]);
        _fourKeys = await AddEntryAsync(_book, _research, "DORA Four Keys", "a1", ["DORA"]);
        var chapter = await AddChapterAsync(_book);
        _scene1 = await AddSceneAsync(_book, chapter, "a0", $"See [metrics](kb:{_dora}) and a law of law.");
        _scene2 = await AddSceneAsync(_book, chapter, "a1", $"Keys: [DORA](kb:{_fourKeys}).");
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("INSERT INTO scene_kb VALUES (@s1, @dora), (@s1, @keys), (@s2, @keys)", new { s1 = _scene1, s2 = _scene2, dora = _dora, keys = _fourKeys });
        await db.ExecuteAsync("UPDATE kb_entry SET body = 'Four metrics.' WHERE id = @dora", new { dora = _dora });
    }

    private Task<HttpResponseMessage> PatchEntryAsync(Guid id, PatchKbEntryRequest request) =>
        Client.PatchAsJsonAsync($"/api/kb/entries/{id}", request);

    [Test, Order(1)]
    public async Task Patch_WhenRenamed_ShouldKeepOldNameAsAliasAndSlug()
    {
        var response = await PatchEntryAsync(_goodhart, new PatchKbEntryRequest("Goodhart's Law (metrics)", null, null, null, null, null, null, null));

        var entry = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        (entry.Name, entry.Slug).ShouldBe(("Goodhart's Law (metrics)", "goodharts-law"));
        entry.Aliases.ShouldContain("Goodhart's Law");
    }

    [Test, Order(2)]
    public async Task Patch_WhenRenamedWithoutKeepingOldName_ShouldNotAddAlias()
    {
        var response = await PatchEntryAsync(_conway, new PatchKbEntryRequest("Conway's Law (teams)", null, null, null, null, null, null, null, KeepOldName: false));

        var entry = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        entry.Aliases.ShouldBe(["Conway"]);
        await PatchEntryAsync(_conway, new PatchKbEntryRequest("Conway's Law", null, null, null, null, null, null, null, KeepOldName: false));
    }

    [Test, Order(3)]
    public async Task Patch_WhenRenamedToAnotherEntrysName_ShouldConflictUnlessAllowed()
    {
        var blocked = await ApiErrors.ReadApiErrorAsync(
            await PatchEntryAsync(_goodhart, new PatchKbEntryRequest("Conway's Law", null, null, null, null, null, null, null)), HttpStatusCode.Conflict);
        var allowed = await PatchEntryAsync(_goodhart, new PatchKbEntryRequest("Conway's Law", null, null, null, null, null, null, null, AllowDuplicate: true));

        blocked.Error.ShouldContain("Conway's Law");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        await PatchEntryAsync(_goodhart, new PatchKbEntryRequest("Goodhart's Law (metrics)", null, null, null, null, null, null, null, AllowDuplicate: true));
    }

    [Test, Order(4)]
    public async Task Patch_WhenAliasCollidesAndAllowed_ShouldStoreIt()
    {
        var aliases = new[] { "Goodhart's Law", "Conway" };
        var blocked = await ApiErrors.ReadApiErrorAsync(
            await PatchEntryAsync(_goodhart, new PatchKbEntryRequest(null, null, null, null, null, aliases, null, null)), HttpStatusCode.Conflict);
        var allowed = await PatchEntryAsync(_goodhart, new PatchKbEntryRequest(null, null, null, null, null, aliases, null, null, AllowDuplicate: true));

        blocked.Error.ShouldContain("Conway's Law");
        (await allowed.Content.ReadFromJsonAsync<KbEntryDto>())!.Aliases.ShouldContain("Conway");
        await PatchEntryAsync(_goodhart, new PatchKbEntryRequest(null, null, null, null, null, ["Goodhart's Law"], null, null));
    }

    [Test, Order(4)]
    public async Task Create_WhenNameIsAnotherEntrysAlias_ShouldConflictUnlessAllowed()
    {
        var blocked = await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(_research, "DORA")), HttpStatusCode.Conflict);
        var allowed = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(_research, "DORA", true));

        blocked.Error.ShouldContain("DORA Four Keys");
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE kb_entry SET archived_at = now() WHERE name = 'DORA' AND archived_at IS NULL AND id <> @_fourKeys", new { _fourKeys });
    }

    [Test, Order(5)]
    public async Task AliasCheck_ShouldReportCollisionAndWarnWhenShortOrCommon()
    {
        var taken = (await Client.GetFromJsonAsync<KbAliasCheckDto>($"/api/kb/entries/{_goodhart}/alias-check?alias=Conway"))!;
        var law = (await Client.GetFromJsonAsync<KbAliasCheckDto>($"/api/kb/entries/{_goodhart}/alias-check?alias=law"))!;
        var fine = (await Client.GetFromJsonAsync<KbAliasCheckDto>($"/api/kb/entries/{_goodhart}/alias-check?alias=Goodhart"))!;

        taken.CollidesWith.ShouldBe("Conway's Law");
        law.ShouldBe(new KbAliasCheckDto(null, 2, true));
        fine.ShouldBe(new KbAliasCheckDto(null, 0, false));
        (await Client.GetAsync($"/api/kb/entries/{_goodhart}/alias-check?alias=%20")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test, Order(6)]
    public async Task MergeImpact_ShouldCountWhatMoves()
    {
        var impact = (await Client.GetFromJsonAsync<KbMergeImpactDto>($"/api/kb/entries/{_dora}/merge-impact?intoId={_fourKeys}"))!;

        // Scene 1 already has the target in its links? No: it has both, so no link moves; DORA metrics and DM become aliases.
        (impact.IntoName, impact.SceneLinks, impact.Additions, impact.Related).ShouldBe(("DORA Four Keys", 0, 0, 0));
        impact.NewAliases.ShouldBe(["DORA metrics", "DM"]);
    }

    [Test, Order(7)]
    public async Task Merge_WhenIntoItselfOrUnknown_ShouldBeRefused()
    {
        var self = await Client.PostAsJsonAsync($"/api/kb/entries/{_dora}/merge", new KbMergeRequest(_dora));
        var unknown = await Client.PostAsJsonAsync($"/api/kb/entries/{_dora}/merge", new KbMergeRequest(Guid.NewGuid()));

        self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(8)]
    public async Task Merge_ShouldRepointLinksAddAliasesAppendBodyAndArchive()
    {
        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_dora}/merge", new KbMergeRequest(_fourKeys));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        target.Aliases.ShouldBe(["DORA", "DORA metrics", "DM"]);
        target.Body.ShouldBe("\n\n**Merged from DORA metrics**\n\nFour metrics.");
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!.Body.ShouldContain($"kb:{_fourKeys}");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @_dora", new { _dora })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM scene_kb WHERE kb_entry_id = @_dora AND archived_at IS NULL", new { _dora })).ShouldBe(0);
        // The duplicate's link is kept, archived with it.
        (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM scene_kb WHERE kb_entry_id = @_dora AND archived_at IS NOT NULL", new { _dora })).ShouldBe(1);
        (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM scene_kb WHERE kb_entry_id = @_fourKeys", new { _fourKeys })).ShouldBe(2);
        (await Client.GetAsync($"/api/kb/entries/{_dora}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(9)]
    public async Task PatchCategory_ShouldRenameRecolourAndRefuseDuplicates()
    {
        var ok = await Client.PatchAsJsonAsync($"/api/kb/categories/{_research}", new PatchKbCategoryRequest("Study", "category-story"));
        var duplicate = await Client.PatchAsJsonAsync($"/api/kb/categories/{_research}", new PatchKbCategoryRequest("laws", null));
        var colour = await Client.PatchAsJsonAsync($"/api/kb/categories/{_research}", new PatchKbCategoryRequest(null, "magenta"));
        var empty = await Client.PatchAsJsonAsync($"/api/kb/categories/{_research}", new PatchKbCategoryRequest(null, null));

        var category = (await ok.Content.ReadFromJsonAsync<KbCategoryDto>())!;
        (category.Name, category.Colour).ShouldBe(("Study", "category-story"));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        colour.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test, Order(10)]
    public async Task DeleteCategory_WhenItHasEntries_ShouldConflictWithTheCount()
    {
        var impact = (await Client.GetFromJsonAsync<KbCategoryDeleteImpactDto>($"/api/kb/categories/{_laws}/delete-impact"))!;
        var error = await ApiErrors.ReadApiErrorAsync(await Client.DeleteAsync($"/api/kb/categories/{_laws}"), HttpStatusCode.Conflict);

        impact.Entries.ShouldBe(2);
        error.Error.ShouldBe("Move or delete its 2 entries first.");
    }

    [Test, Order(11)]
    public async Task DeleteCategory_WhenEmpty_ShouldArchiveItAndLeaveTheKnowledgeBase()
    {
        var temp = await CreateCategoryAsync("Temp");

        var response = await Client.DeleteAsync($"/api/kb/categories/{temp.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"/api/kb/categories/{temp.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_category WHERE id = @id", new { id = temp.Id })).ShouldBeTrue();
        var kb = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!;
        kb.Categories.Select(c => c.Name).ShouldNotContain("Temp");
    }

    [Test, Order(12)]
    public async Task DeleteCategory_WhenDeleted_ShouldFreeTheNameAndKeepItsArchivedEntries()
    {
        var doomed = await CreateCategoryAsync("Doomed");
        var entry = await AddEntryAsync(_book, doomed.Id, "Gone Thing");
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE kb_entry SET archived_at = now() WHERE id = @entry", new { entry });

        (await Client.DeleteAsync($"/api/kb/categories/{doomed.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/categories", new CreateKbCategoryRequest("Doomed", "category-law"));
        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await db.QuerySingleAsync<(bool Archived, Guid Category)>(
            "SELECT archived_at IS NOT NULL, category_id FROM kb_entry WHERE id = @entry", new { entry })).ShouldBe((true, doomed.Id));
    }

    [Test, Order(13)]
    public async Task DeletedCategory_ShouldBeUnknownToRenameCreateAndMove()
    {
        var gone = await CreateCategoryAsync("Gone");
        (await Client.DeleteAsync($"/api/kb/categories/{gone.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rename = await Client.PatchAsJsonAsync($"/api/kb/categories/{gone.Id}", new PatchKbCategoryRequest("Back", null));
        var create = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(gone.Id, "Nothing"));
        var move = await PatchEntryAsync(_conway, new PatchKbEntryRequest(null, null, null, null, null, null, null, gone.Id));

        rename.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        create.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        move.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(14)]
    public async Task DuplicateBook_ShouldKeepADeletedCategoryDeleted()
    {
        await using var db = await OpenDatabaseAsync();

        var copy = await db.ExecuteScalarAsync<Guid>("SELECT duplicate_book(@book, 'Tides (copy)')", new { book = _book });

        var live = (await db.QueryAsync<string>("SELECT name FROM kb_category WHERE book_id = @copy AND archived_at IS NULL", new { copy })).ToList();
        live.ShouldContain("Laws");
        live.ShouldNotContain("Gone");
        (await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM kb_category WHERE book_id = @copy AND name = 'Gone' AND archived_at IS NOT NULL)", new { copy })).ShouldBeTrue();
    }

    private async Task<KbCategoryDto> CreateCategoryAsync(string name) =>
        (await (await Client.PostAsJsonAsync($"/api/books/{_book}/kb/categories", new CreateKbCategoryRequest(name, "category-person"))).Content
            .ReadFromJsonAsync<KbCategoryDto>())!;
}
