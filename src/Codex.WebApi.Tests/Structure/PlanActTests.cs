using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class PlanActTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _firstAct;
    private Guid _useId;
    private Guid _sourceId;
    private Guid _appended;
    private Guid _middle;
    private Guid _singleActBook;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _firstAct = await TestRows.FirstActAsync(db, _bookId);
        var chapter = await TestRows.ChapterAsync(db, _bookId, _firstAct, "a0", "One");
        var scene = await TestRows.SceneAsync(db, _bookId, chapter, "a0", "A");
        _sourceId = await TestRows.SourceAsync(db, _bookId, "The Velocity Trap");
        _useId = await TestRows.UseAsync(db, _bookId, _sourceId, scene, "a quoted line");
        await TestRows.ProseAsync(db, scene, $"Before [a quoted line](ref:{_useId}) after.");
        _singleActBook = await TestRows.BookAsync(db, "Alone");
    }

    [Test]
    [Order(1)]
    public async Task Create_WhenNoAfterId_ShouldAppendWithOneChapterHoldingOneEmptyScene()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/acts", new CreateActRequest(null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var act = (await response.Content.ReadFromJsonAsync<OutlineActDto>(JsonSerializerOptions.Web))!;
        act.Title.ShouldBe("");
        var chapter = act.Chapters.ShouldHaveSingleItem();
        (chapter.Title, chapter.Number).ShouldBe(("New chapter", 2));
        var scene = chapter.Scenes.ShouldHaveSingleItem();
        (scene.Title, scene.Summary, scene.Number).ShouldBe((null, null, "2.1"));
        _appended = act.Id;
        (await OutlineAsync()).Acts.Select(a => a.Id).ShouldBe([_firstAct, _appended]);
    }

    [Test]
    [Order(2)]
    public async Task Create_WhenAfterId_ShouldInsertAfterItAndRenumberChapters()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/acts", new CreateActRequest("  The Turn  ", _firstAct));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var act = (await response.Content.ReadFromJsonAsync<OutlineActDto>(JsonSerializerOptions.Web))!;
        act.Title.ShouldBe("The Turn");
        act.Chapters.ShouldHaveSingleItem().Number.ShouldBe(2);
        _middle = act.Id;
        var outline = await OutlineAsync();
        outline.Acts.Select(a => a.Id).ShouldBe([_firstAct, _middle, _appended]);
        outline.Acts[2].Chapters.ShouldHaveSingleItem().Number.ShouldBe(3);
    }

    [Test]
    [Order(3)]
    public async Task Create_WhenAfterIdIsNotAnActOfTheBook_400()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/acts", new CreateActRequest(null, Guid.NewGuid()));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(4)]
    public async Task Create_WhenTitleTooLong_400()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/acts", new CreateActRequest(new string('x', 201), null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(5)]
    public async Task Create_WhenBookUnknown_404()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/acts", new CreateActRequest(null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(6)]
    public async Task Rename_ShouldSetTheTrimmedTitle()
    {
        var response = await Client.PatchAsJsonAsync($"/api/acts/{_appended}", new PatchActRequest(" Endgame "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<OutlineActDto>(JsonSerializerOptions.Web))!.Title.ShouldBe("Endgame");
    }

    [Test]
    [Order(7)]
    public async Task Rename_WhenTitleEmpty_ShouldLeaveTheActUntitled()
    {
        var response = await Client.PatchAsJsonAsync($"/api/acts/{_middle}", new PatchActRequest(""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<OutlineActDto>(JsonSerializerOptions.Web))!.Title.ShouldBe("");
    }

    [Test]
    [Order(8)]
    public async Task Rename_WhenTitleMissing_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/acts/{_middle}", new PatchActRequest(null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(9)]
    public async Task Archive_ShouldTakeTheActOffTheOutlineAndNameItsUses()
    {
        var response = await Client.PostAsync($"/api/acts/{_firstAct}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveActResult>(JsonSerializerOptions.Web))!;
        result.ActId.ShouldBe(_firstAct);
        result.ArchivedUses.ShouldBe([new SceneUseDto(_useId, _sourceId, "The Velocity Trap")]);
        var outline = await OutlineAsync();
        outline.Acts.Select(a => a.Id).ShouldBe([_middle, _appended]);
        outline.Acts[0].Chapters.ShouldHaveSingleItem().Number.ShouldBe(1);
    }

    [Test]
    [Order(10)]
    public async Task Archive_WhenAlreadyArchived_409()
    {
        var response = await Client.PostAsync($"/api/acts/{_firstAct}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(11)]
    public async Task Rename_WhenArchived_404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/acts/{_firstAct}", new PatchActRequest("Back"));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(12)]
    public async Task Restore_WhenAnotherActTookItsPosition_ShouldPutItRightAfterThatOne()
    {
        await using var db = await OpenDatabaseAsync();
        var taker = await TestRows.ActAsync(db, _bookId, "a0", "Taker");

        var response = await Client.PostAsync($"/api/acts/{_firstAct}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var act = (await response.Content.ReadFromJsonAsync<OutlineActDto>(JsonSerializerOptions.Web))!;
        act.Chapters.ShouldHaveSingleItem().Scenes.ShouldHaveSingleItem().Number.ShouldBe("1.1");
        (await OutlineAsync()).Acts.Select(a => a.Id).ShouldBe([taker, _firstAct, _middle, _appended]);
    }

    [Test]
    [Order(13)]
    public async Task Restore_WhenActIsLive_409()
    {
        var response = await Client.PostAsync($"/api/acts/{_firstAct}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(14)]
    public async Task Archive_WhenItIsTheBooksLastAct_409()
    {
        await using var db = await OpenDatabaseAsync();
        var only = await TestRows.FirstActAsync(db, _singleActBook);

        var response = await Client.PostAsync($"/api/acts/{only}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(15)]
    public async Task Archive_WhenActUnknown_404()
    {
        var response = await Client.PostAsync($"/api/acts/{Guid.NewGuid()}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<OutlineDto> OutlineAsync()
        => (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!;
}
