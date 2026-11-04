using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J28 · Share a book (#80, Tests → Playwright journey): JD shares Hollow Throne with Ana as Can view from its Sharing card; Ana, in her own
// window, sees it in her Library as shared by Joel Dickson and cannot type in Write; JD makes her Can edit and her open
// tab becomes editable without a reload; JD removes her and her tab goes back to the Library. JD is this fixture's
// default user (no cookie); Ana signs in through /login in a second browser context, so the hub knows her tabs.
[TestFixture, Category("J28")]
public class J28_ShareABook : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.HollowThrone.ToString();
    private const string AnaEmail = "ana@codex.local";
    private const string DevPassword = "codex-dev-password";
    private const string Opening = "The throne had been empty for a generation.";

    private IBrowserContext _anaContext = null!;
    private IPage _ana = null!;
    private Guid _scene;

    [OneTimeTearDown]
    public async Task CloseAnasWindowAsync()
    {
        if (_anaContext != null)
        {
            await _anaContext.CloseAsync();
        }
    }

    // Hollow Throne has no chapters in the seed: give it one scene with prose, as JD, so Write has text to (not) type in.
    private async Task WriteAnOpeningSceneAsync()
    {
        var act = await Page.APIRequest.PostAsync($"/api/books/{BookId}/acts", new() { DataObject = new { title = "Act one", afterId = (string?)null } });
        act.Ok.ShouldBeTrue(await act.TextAsync());
        var created = await act.JsonAsync();
        _scene = created!.Value.GetProperty("chapters")[0].GetProperty("scenes")[0].GetProperty("id").GetGuid();
        var prose = await Page.APIRequest.PutAsync($"/api/scenes/{_scene}/prose", new() { DataObject = new { body = Opening + "\n", version = 0 } });
        prose.Ok.ShouldBeTrue(await prose.TextAsync());
    }

    private Task<string?> AnasRoleAsync() => ScalarSqlAsync<string>(
        "SELECT role FROM book_grant WHERE book_id = @book AND user_id = @user AND archived_at IS NULL",
        ("book", SeedIds.Books.HollowThrone), ("user", SeedIds.Users.Ana));

    [Test, Order(1)]
    public async Task Step1_WhenJdSharesTheBookAsCanView_ShouldGrantAnaView()
    {
        await WriteAnOpeningSceneAsync();

        var settings = new SettingsScreen(Page);
        await Page.GotoAsync(Links.Settings(BookId, Links.Hash(("sharing", null))));
        await settings.ShareThisBookAsync(AnaEmail.ToUpperInvariant(), "view");

        await Expect(settings.SharingPerson("Ana Kowalczyk").GetByTestId(SettingsTestIds.SharingRole)).ToHaveValueAsync("view");
        await Expect(Page.GetByTestId(SettingsTestIds.Toast).Last).ToContainTextAsync("Shared Hollow Throne with Ana Kowalczyk · Can view");
        (await AnasRoleAsync()).ShouldBe("view");
    }

    [Test, Order(2)]
    public async Task Step2_WhenAnaOpensIt_ShouldSeeItSharedByJdAndNotBeAbleToType()
    {
        _anaContext = await Page.Context.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        });
        _ana = await _anaContext.NewPageAsync();
        await _ana.GotoAsync("/login");
        await new LoginScreen(_ana).SignInAsync(AnaEmail, DevPassword);
        await Expect(new LibraryShelf(_ana).Root).ToBeVisibleAsync();

        // Only Hollow Throne of JD's Ashen Crown is shared with her: the series screen shows it alone, who shared it, and why
        // the other books are missing.
        await _ana.GotoAsync(Links.Series(SeedIds.Series.AshenCrown.ToString()));
        await Expect(_ana.GetByTestId(LibraryTestIds.SeriesShared)).ToContainTextAsync("Shared by Joel Dickson");
        await Expect(_ana.GetByTestId(LibraryTestIds.PartlySharedNote)).ToHaveTextAsync(
            "Joel Dickson shared one book in this series with you. Only that book appears here.");
        await Expect(new LibraryShelf(_ana).Tile(BookId).GetByTestId(LibraryTestIds.RoleChip)).ToHaveTextAsync("View only");
        await Expect(new LibraryShelf(_ana).Tile(SeedIds.Books.EmberOath.ToString())).ToHaveCountAsync(0);

        var joined = WatchJoinAsync(_ana);
        var write = new WritePageObject(_ana);
        await _ana.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(_scene)).ToContainTextAsync(Opening);
        await Within(joined, "Ana's tab to join the book");
        var bar = new BookBar(_ana);
        await Expect(bar.RoleChip).ToHaveTextAsync("View only");
        await Expect(bar.RoleChip).ToHaveAttributeAsync("title", new Regex("Joel Dickson shared this book with you to read"));
        await Expect(write.ProseEditor(_scene)).ToHaveAttributeAsync("contenteditable", "false");

        await write.ProseEditor(_scene).ClickAsync();
        await _ana.Keyboard.TypeAsync(" Ana was here.");

        await Expect(write.ProseEditor(_scene)).Not.ToContainTextAsync("Ana was here.");
    }

    [Test, Order(3)]
    public async Task Step3_WhenJdMakesHerCanEdit_ShouldMakeHerOpenTabEditableWithoutAReload()
    {
        var write = new WritePageObject(_ana);
        await _ana.EvaluateAsync("() => { window.__sameLoad = true; }");

        await Page.GotoAsync(Links.Settings(BookId, Links.Hash(("sharing", null))));
        await new SettingsScreen(Page).SharingPerson("Ana Kowalczyk").GetByTestId(SettingsTestIds.SharingRole).SelectOptionAsync("write");

        await Expect(write.ProseEditor(_scene)).ToHaveAttributeAsync("contenteditable", "true");
        await Expect(new BookBar(_ana).RoleChip).ToHaveCountAsync(0);
        await Expect(_ana.GetByTestId(WriteTestIds.Toast).Last).ToContainTextAsync("You can edit this book now. Typing works again.");
        (await _ana.EvaluateAsync<bool>("() => window.__sameLoad === true")).ShouldBeTrue("the tab was reloaded");

        await write.ProseEditor(_scene).ClickAsync();
        await _ana.AfterEditorClickAsync();
        await _ana.Keyboard.PressAsync("Control+End");
        await _ana.Keyboard.TypeAsync(" Ana was here.");
        await write.Manuscript.FocusAsync();
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        ((await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", _scene))) ?? "")
            .ShouldContain("Ana was here.");
    }

    [Test, Order(4)]
    public async Task Step4_WhenJdRemovesHer_ShouldTakeHerTabBackToTheLibrary()
    {
        await Page.GotoAsync(Links.Settings(BookId, Links.Hash(("sharing", null))));
        await new SettingsScreen(Page).SharingPerson("Ana Kowalczyk").GetByTestId(SettingsTestIds.SharingRemove).ClickAsync();

        await Expect(new LibraryShelf(_ana).Root).ToBeVisibleAsync();
        await Expect(_ana).ToHaveURLAsync(new Regex("/$"));
        await Expect(_ana.GetByTestId(WriteTestIds.Toast).Last).ToContainTextAsync("Joel Dickson stopped sharing “Hollow Throne” with you.");
        await Expect(new LibraryShelf(_ana).Tile(BookId)).ToHaveCountAsync(0);
        (await AnasRoleAsync()).ShouldBeNull();
    }

    // Fails the test when `task` has not finished in 15 s.
    private static async Task Within(Task task, string what)
    {
        using var timeout = new CancellationTokenSource();
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15), timeout.Token)) != task)
        {
            throw new TimeoutException($"Timed out waiting for {what}.");
        }
        await timeout.CancelAsync();
        await task;
    }

    // Resolves once the page's first hub invocation (JoinBook) has been answered: from then on AccessChanged reaches it.
    // Registered before navigating, because the socket opens during page load.
    private static Task WatchJoinAsync(IPage page)
    {
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.WebSocket += (_, socket) => socket.FrameReceived += (_, frame) =>
        {
            if (frame.Text?.Contains("\"type\":3", StringComparison.Ordinal) == true
                && frame.Text.Contains("\"invocationId\":\"0\"", StringComparison.Ordinal))
            {
                joined.TrySetResult();
            }
        };
        return joined.Task;
    }
}
