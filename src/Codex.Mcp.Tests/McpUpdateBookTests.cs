using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// update_book over stdio on the seed (#20 section C): fields, publication, series number and a series move, each with
// a dry run, its errors, and undo. The HTTP API shows what the agent wrote. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(120000)]
public class McpUpdateBookTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string HollowThrone = SeedIds.Books.HollowThrone.ToString();
    private JourneyServer _server = null!;
    private string _undoId = "";
    private string _styleBefore = "";
    private int _limitBefore;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        // The stdio process reads the built-in layout templates from the repo, as the web host does.
        _server = await JourneyServer.StartWithTemplatesAsync();
        var publication = await _server.ApiAsync($"/api/books/{Book}/publication-settings");
        _styleBefore = publication.GetProperty("style").GetProperty("value").GetString()!;
        _limitBefore = publication.GetProperty("quoteWordLimit").GetProperty("value").GetInt32();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task UpdateBook_WhenTheTitleMatchesTwoBooks_ShouldBeAmbiguous()
    {
        var error = await Client.ErrorAsync("update_book", new { book = "Product Engineering", fields = new { status = "writing" } });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        error.GetProperty("candidates").GetArrayLength().ShouldBe(2);
    }

    [TestCase("nothing")]
    [TestCase("status")]
    [TestCase("layout_template")]
    [TestCase("layout_option")]
    [TestCase("series_and_standalone")]
    public async Task UpdateBook_WhenTheArgumentsAreWrong_ShouldBeInvalid(string case_)
    {
        object arguments = case_ switch
        {
            "nothing" => new { book = Book },
            "status" => new { book = Book, fields = new { status = "done" } },
            "layout_template" => new { book = Book, layout = new { template = "no-such-template" } },
            "layout_option" => new { book = Book, layout = new { options = new Dictionary<string, object> { ["drop-caps"] = "yes" } } },
            _ => new { book = Book, series = "The Ashen Crown", make_standalone = true },
        };

        var error = await Client.ErrorAsync("update_book", arguments);

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    [Order(2)]
    public async Task UpdateBook_WhenDryRun_ShouldReportTheChangesAndWriteNothing()
    {
        var result = await Client.OkAsync("update_book", new
        {
            book = Book, dry_run = true, fields = new { target_word_count = 90000 }, publication = new { citation_style = "apa", quote_word_limit = 100 },
        });

        result.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        result.GetProperty("side_effects").EnumerateArray().Select(s => s.GetString()).ShouldContain(s => s!.Contains("citations and the bibliography use apa"));
        result.GetProperty("side_effects").EnumerateArray().Select(s => s.GetString()).ShouldContain(s => s!.Contains("Copyright risk"));
        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await _server.ApiAsync($"/api/books/{Book}/publication-settings")).GetProperty("style").GetProperty("value").GetString().ShouldBe(_styleBefore);
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("targetWords").GetInt32().ShouldNotBe(90000);
    }

    [Test]
    [Order(3)]
    public async Task UpdateBook_WhenApplied_ShouldChangeTheFieldsAndPublicationSettings()
    {
        var result = await Client.OkAsync("update_book", new
        {
            book = Book, fields = new { author_name = "J. Dickson", target_word_count = 90000 }, publication = new { citation_style = "apa", quote_word_limit = 250 },
        });

        _undoId = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("dry_run").GetBoolean().ShouldBeFalse();
        result.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{Book}/settings");
        result.GetProperty("affected").EnumerateArray().Select(a => a.GetProperty("field").GetString())
            .ShouldBe(["author_name", "target_word_count", "citation_style", "quote_word_limit"], ignoreOrder: true);
        var settings = await _server.ApiAsync($"/api/books/{Book}/settings");
        settings.GetProperty("authorName").GetString().ShouldBe("J. Dickson");
        settings.GetProperty("targetWords").GetInt32().ShouldBe(90000);
        var publication = await _server.ApiAsync($"/api/books/{Book}/publication-settings");
        publication.GetProperty("style").GetProperty("value").GetString().ShouldBe("apa");
        publication.GetProperty("quoteWordLimit").GetProperty("value").GetInt32().ShouldBe(250);
        (await ChangeSetToolAsync()).ShouldBe("update_book");
    }

    [Test]
    [Order(4)]
    public async Task UpdateBook_WhenAFieldFailsPartWay_ShouldPutBackWhatItAlreadyWrote()
    {
        // The settings patch lands, then the publication patch is refused (a limit of 0), so the first is rolled back.
        var error = await Client.ErrorAsync("update_book", new
        {
            book = Book, fields = new { target_word_count = 12345 }, publication = new { quote_word_limit = 0 },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("targetWords").GetInt32().ShouldBe(90000);
    }

    [Test]
    [Order(5)]
    public async Task Undo_WhenTheUpdateIsUndone_ShouldPutTheValuesBack()
    {
        await Client.OkAsync("undo", new { undo_id = _undoId });

        var publication = await _server.ApiAsync($"/api/books/{Book}/publication-settings");
        publication.GetProperty("style").GetProperty("value").GetString().ShouldBe(_styleBefore);
        publication.GetProperty("quoteWordLimit").GetProperty("value").GetInt32().ShouldBe(_limitBefore);
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("targetWords").GetInt32().ShouldNotBe(90000);
    }

    [Test]
    [Order(6)]
    public async Task UpdateBook_WhenChangingTheSeriesNumber_ShouldRenumberTheSeriesAndUndoShouldPutItBack()
    {
        var result = await Client.OkAsync("update_book", new { book = HollowThrone, series_number = 1 });

        (await _server.ApiAsync($"/api/books/{HollowThrone}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(1);
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await _server.ApiAsync($"/api/books/{HollowThrone}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(3);
    }

    [Test]
    [Order(7)]
    public async Task UpdateBook_WhenMovingASeriesWithoutConfirming_ShouldNeedConfirmationAndWriteNothing()
    {
        var error = await Client.ErrorAsync("update_book", new { book = Book, series = "The Ashen Crown" });

        error.GetProperty("code").GetString().ShouldBe("needs_confirmation");
        error.GetProperty("message").GetString()!.ShouldContain("Knowledge base");
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("seriesId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    [Order(8)]
    public async Task UpdateBook_WhenMovingASeriesAfterADryRun_ShouldJoinTheSeriesAndBeUnundoable()
    {
        var dry = await Client.OkAsync("update_book", new { book = Book, series = "The Ashen Crown", dry_run = true });
        dry.GetProperty("side_effects").EnumerateArray().Select(s => s.GetString()).ShouldContain(s => s!.Contains("Join The Ashen Crown"));

        var result = await Client.OkAsync("update_book", new { book = Book, series = "The Ashen Crown", confirm = true });

        var settings = await _server.ApiAsync($"/api/books/{Book}/settings");
        settings.GetProperty("seriesId").GetGuid().ShouldBe(SeedIds.Series.AshenCrown);
        settings.GetProperty("seriesNumber").GetInt32().ShouldBe(5);
        var error = await Client.ErrorAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("undo_unsupported");
    }

    [Test]
    [Order(9)]
    public async Task UpdateBook_WhenSettingSubtitleAndDescription_ShouldStoreThemAndUndoShouldPutThemBack()
    {
        var before = await _server.ApiAsync($"/api/books/{Book}/settings");

        var result = await Client.OkAsync("update_book", new { book = Book, fields = new { subtitle = "A field guide", description = "How teams ship." } });

        var after = await _server.ApiAsync($"/api/books/{Book}/settings");
        after.GetProperty("subtitle").GetString().ShouldBe("A field guide");
        after.GetProperty("description").GetString().ShouldBe("How teams ship.");
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        var back = await _server.ApiAsync($"/api/books/{Book}/settings");
        back.GetProperty("subtitle").ToString().ShouldBe(before.GetProperty("subtitle").ToString());
        back.GetProperty("description").ToString().ShouldBe(before.GetProperty("description").ToString());
    }

    [Test]
    [Order(10)]
    public async Task UpdateBook_WhenSettingTheLayout_ShouldDryRunThenApplyThroughTheLayoutServiceAndUndoShouldPutTheSelectionBack()
    {
        var layout = new { template = "novel-6x9", trim = "5.5x8.5", options = new Dictionary<string, object> { ["drop-caps"] = true } };

        var dry = await Client.OkAsync("update_book", new { book = Book, layout, dry_run = true });
        dry.GetProperty("side_effects").EnumerateArray().Select(c => c.GetString()).ShouldContain(c => c!.Contains("layout_trim: 6x9 -> 5.5x8.5"));
        (await _server.ApiAsync($"/api/books/{Book}/layout")).GetProperty("trim").GetString().ShouldBe("6x9");

        var result = await Client.OkAsync("update_book", new { book = Book, layout });

        var state = await _server.ApiAsync($"/api/books/{Book}/layout");
        state.GetProperty("template").GetString().ShouldBe("novel-6x9");
        state.GetProperty("trim").GetString().ShouldBe("5.5x8.5");
        DropCaps(state).ShouldBeTrue();
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        var back = await _server.ApiAsync($"/api/books/{Book}/layout");
        back.GetProperty("trim").GetString().ShouldBe("6x9");
        DropCaps(back).ShouldBeFalse();
    }

    private static bool DropCaps(System.Text.Json.JsonElement state) =>
        state.GetProperty("templates").EnumerateArray().Single(t => t.GetProperty("slug").GetString() == "novel-6x9")
            .GetProperty("options").EnumerateArray().Single(o => o.GetProperty("key").GetString() == "drop-caps").GetProperty("value").GetBoolean();

    private async Task<string?> ChangeSetToolAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string?>("SELECT tool FROM change_set ORDER BY created_at DESC LIMIT 1");
    }
}
