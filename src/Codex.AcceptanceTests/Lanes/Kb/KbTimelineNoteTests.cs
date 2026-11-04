using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.Contracts;
using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.AcceptanceTests.Lanes.Kb;

// The entry window's Timeline tab on Goodhart's Law (seed note at scene 4.1): Add a note at… writes a Codex addition
// at the start of the chosen scene, a note's text can be edited in place, and an empty note is turned away.
[TestFixture]
public class KbTimelineNoteTests : SeededAcceptanceTest
{
    private const string NewNote = "By now every team has a velocity target.";
    private const string EditedNote = "First seen here, with a target attached.";

    private static string BookId => SeedIds.Books.ProductEngineering.ToString();

    private ClonedDatabase _clone = null!;

    private KbEntryWindow Window => new(Page);

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        _clone = await base.CloneDatabaseAsync();
        return _clone;
    }

    private async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _clone.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private async Task OpenTimelineAsync()
    {
        var entry = SeedIds.KbEntries.GoodhartsLaw.ToString();
        await Page.GotoAsync(Links.Plan(BookId, Links.Hash(("kb", entry), ("entry", entry))));
        await Expect(Window.Title).ToHaveTextAsync("Goodhart's Law");
        await Window.Tab("timeline").ClickAsync();
    }

    [Test, Order(1)]
    public async Task AddNote_WhenTextEmpty_ShouldBeRejectedWithoutWriting()
    {
        var posts = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.Contains("/kb-additions", StringComparison.Ordinal))
            {
                posts.Add(request.Url);
            }
        };
        await OpenTimelineAsync();
        await Window.TimelineAdd.ClickAsync();
        await Window.TimelineAddScene.SelectOptionAsync(SeedIds.Scenes.Ch5Sc1.ToString());

        await Window.TimelineAddSubmit.ClickAsync();

        await Expect(Window.TimelineError).ToHaveTextAsync("Write the note first.");
        await Expect(Window.TimelineNotes).ToHaveCountAsync(1);
        posts.ShouldBeEmpty();
    }

    [Test, Order(2)]
    public async Task AddNote_ShouldWriteAdditionLineAtSceneStartAndListItInBookOrder()
    {
        await OpenTimelineAsync();
        await Window.TimelineAdd.ClickAsync();
        await Window.TimelineAddScene.SelectOptionAsync(SeedIds.Scenes.Ch5Sc1.ToString());
        await Window.TimelineAddText.FillAsync(NewNote);

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch5Sc1}/kb-additions", "POST", () => Window.TimelineAddSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Window.TimelineNotes).ToHaveCountAsync(2);
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync("From scene 4.1");
        await Expect(Window.TimelineNotes.Nth(1)).ToContainTextAsync("From scene 5.1");
        await Expect(Window.TimelineNotes.Nth(1)).ToContainTextAsync(NewNote);
        var id = await ScalarAsync<Guid>("SELECT id FROM kb_progression WHERE body = @body AND archived_at IS NULL", ("body", NewNote));
        (await ScalarAsync<Guid>("SELECT kb_entry_id FROM kb_progression WHERE id = @id", ("id", id))).ShouldBe(SeedIds.KbEntries.GoodhartsLaw);
        (await ScalarAsync<Guid>("SELECT scene_id FROM kb_progression WHERE id = @id", ("id", id))).ShouldBe(SeedIds.Scenes.Ch5Sc1);
        var body = await ScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", SeedIds.Scenes.Ch5Sc1));
        body.ShouldNotBeNull().ShouldStartWith($"::kb-addition{{id=\"{id}\"}}");
        await Page.ReloadAsync();
        await Window.Tab("timeline").ClickAsync();
        await Expect(Window.TimelineNotes).ToHaveCountAsync(2);
    }

    [Test, Order(3)]
    public async Task EditNote_ShouldSaveTheNewTextAndRejectAnEmptyOne()
    {
        await OpenTimelineAsync();
        var noteId = await ScalarAsync<Guid>("SELECT id FROM kb_progression WHERE scene_id = @scene AND archived_at IS NULL", ("scene", SeedIds.Scenes.Ch4Sc1));
        await Window.TimelineEdit(noteId).ClickAsync();
        await Window.TimelineEditText(noteId).FillAsync("  ");
        await Window.TimelineEditSave(noteId).ClickAsync();
        await Expect(Window.TimelineError).ToHaveTextAsync("Write the note first.");

        await Window.TimelineEditText(noteId).FillAsync(EditedNote);
        var response = await Page.RunAndWaitForApiAsync($"/api/kb-additions/{noteId}", "PATCH", () => Window.TimelineEditSave(noteId).ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync(EditedNote);
        await Expect(Window.TimelineEditText(noteId)).ToHaveCountAsync(0);
        (await ScalarAsync<string>("SELECT body FROM kb_progression WHERE id = @id", ("id", noteId))).ShouldBe(EditedNote);
    }
}
