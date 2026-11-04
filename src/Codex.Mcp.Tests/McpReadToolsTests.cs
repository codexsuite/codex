using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace Codex.Mcp.Tests;

// The phase-1 read tools over stdio: the real Codex.Mcp process against a clone of the seed (#16 Part 1). Nothing here
// writes, so the tests share one server.
[TestFixture]
[CancelAfter(60000)]
public class McpReadToolsTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task ResumeWork_WhenCalledWithNoBook_ShouldReturnCh5Sc2WithItsVersionAndPendingDraftText()
    {
        var body = await Client.OkAsync("resume_work");

        body.GetProperty("book").GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.ProductEngineering);
        var last = body.GetProperty("last_scene");
        last.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch5Sc2);
        last.GetProperty("item").GetProperty("label").GetString().ShouldBe("Ch 5 · Sc 2 · Outcome metrics that don't rot");
        last.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{ProductEngineering}/write#scene=5.2");
        last.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ShouldBe(["Needs research"]);
        last.GetProperty("version").GetInt32().ShouldBe(await ProseVersionAsync(SeedIds.Scenes.Ch5Sc2));
        var draft = last.GetProperty("pending_draft");
        draft.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.AiGenerations.Ch5Sc2Pending);
        draft.GetProperty("model").GetString().ShouldBe("claude-opus-5-5");
        draft.GetProperty("attempt").GetInt32().ShouldBe(1);
        draft.GetProperty("paragraphs").GetInt32().ShouldBe(3);
        draft.GetProperty("text").GetString().ShouldBe(await DraftTextAsync(SeedIds.AiGenerations.Ch5Sc2Pending));
        var pending = body.GetProperty("pending_drafts").EnumerateArray().Single();
        pending.GetProperty("scene").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch5Sc2);
        pending.GetProperty("scene_version").GetInt32().ShouldBe(last.GetProperty("version").GetInt32());
        body.GetProperty("needs_attention").EnumerateArray().Select(a => a.GetProperty("item").GetProperty("number").GetString())
            .ShouldContain("3.3");
        body.GetProperty("references").GetProperty("risks").GetInt32().ShouldBe(3);
        body.GetProperty("references").GetProperty("print_blocked").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task GetOutline_WhenBookTitleMatchesTwoBooks_ShouldFailAmbiguousAndListBoth()
    {
        var error = await Client.ErrorAsync("get_outline", new { book = "Product Engineering" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        error.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.Books.ProductEngineering, SeedIds.Books.ProductEngineeringV1], ignoreOrder: true);
    }

    [Test]
    public async Task GetOutline_WhenBookIsSeriesAndNumber_ShouldReturnThatBooksOutlineWithSeriesKbIndex()
    {
        var body = await Client.OkAsync("get_outline", new { book = "The Ashen Crown/3", include = new[] { "kb_index" } });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.HollowThrone);
        var index = body.GetProperty("kb_index").EnumerateArray().ToList();
        index.Select(c => c.GetProperty("name").GetString()).ShouldBe(["Characters", "Places"], ignoreOrder: true);
        index.ShouldAllBe(c => c.GetProperty("scope").GetString() == "series");
    }

    [Test]
    public async Task ReadManuscript_WhenScopeRefIsUnknown_ShouldFailNotFound()
    {
        var error = await Client.ErrorAsync("read_manuscript", new { book = ProductEngineering, scope = "42.1" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task ReadManuscript_WhenTheBookIsLongerThanMaxWords_ShouldPageWithACursorThroughEveryScene()
    {
        const int maxWords = 2500;
        var sceneIds = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await Client.OkAsync("read_manuscript", new { book = ProductEngineering, max_words = maxWords, cursor });
            pages++;
            page.GetProperty("words").GetInt32().ShouldBeLessThanOrEqualTo(maxWords);
            foreach (var scene in page.GetProperty("scenes").EnumerateArray())
            {
                var id = scene.GetProperty("item").GetProperty("id").GetGuid();
                if (sceneIds.Count == 0 || sceneIds[^1] != id)
                {
                    sceneIds.Add(id);
                }
            }
            cursor = page.TryGetProperty("cursor", out var next) ? next.GetString() : null;
        }
        while (cursor != null && pages < 50);

        pages.ShouldBeGreaterThan(2);
        cursor.ShouldBeNull();
        sceneIds.ShouldBe(await LiveScenesInReadingOrderAsync(SeedIds.Books.ProductEngineering));
    }

    [Test]
    public async Task ReadManuscript_WhenSceneHasAPendingDraft_ShouldShowItInBothModes()
    {
        var draftText = await DraftTextAsync(SeedIds.AiGenerations.Ch5Sc2Pending);

        var annotated = (await Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "5.2" }))
            .GetProperty("scenes").EnumerateArray().Single();
        var markdown = (await Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "5.2", format = "markdown" }))
            .GetProperty("scenes").EnumerateArray().Single();

        var text = annotated.GetProperty("text").GetString()!;
        text.ShouldContain("[Beat · 400 words");
        text.ShouldContain("[Pending draft · claude-opus-5-5 · attempt 1 · General purpose (Nonfiction)");
        text.ShouldContain(draftText.Trim());
        markdown.GetProperty("body").GetString().ShouldBe(await ProseBodyAsync(SeedIds.Scenes.Ch5Sc2));
        markdown.GetProperty("version").GetInt32().ShouldBe(await ProseVersionAsync(SeedIds.Scenes.Ch5Sc2));
        var draft = markdown.GetProperty("pending_drafts").EnumerateArray().Single();
        draft.GetProperty("text").GetString().ShouldBe(draftText);
        draft.GetProperty("template").GetString().ShouldBe("General purpose (Nonfiction)");
    }

    [Test]
    public async Task GetDraft_WhenGivenThePendingDraft_ShouldReturnItsTextAndTheSceneVersion()
    {
        var body = await Client.OkAsync("get_draft", new { draft = SeedIds.AiGenerations.Ch5Sc2Pending });

        body.GetProperty("scene").GetProperty("number").GetString().ShouldBe("5.2");
        body.GetProperty("scene_version").GetInt32().ShouldBe(await ProseVersionAsync(SeedIds.Scenes.Ch5Sc2));
        body.GetProperty("draft").GetProperty("status").GetString().ShouldBe("pending");
        body.GetProperty("draft").GetProperty("text").GetString().ShouldBe(await DraftTextAsync(SeedIds.AiGenerations.Ch5Sc2Pending));
    }

    [Test]
    public async Task GetKbEntry_WhenRefIsAnAlias_ShouldReturnTheEntryAndItsAdditions()
    {
        var body = await Client.OkAsync("get_kb_entry", new { entry = "goodhart" });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.KbEntries.GoodhartsLaw);
        body.GetProperty("item").GetProperty("label").GetString().ShouldBe("Goodhart's Law");
        body.GetProperty("category").GetString().ShouldBe("Laws");
        body.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()).ShouldContain("Goodhart");
        var addition = body.GetProperty("additions").EnumerateArray().Single();
        addition.GetProperty("id").GetGuid().ShouldBe(SeedIds.KbProgressions.Ch4Sc1GoodhartsLaw);
        addition.GetProperty("scene").GetProperty("number").GetString().ShouldBe("4.1");
    }

    [Test]
    public async Task FindKbUsages_WhenEntryIsGoodhart_ShouldListCh4Sc1AndCh5Sc2WithKinds()
    {
        var body = await Client.OkAsync("find_kb_usages", new { entry = "Goodhart" });

        var uses = body.GetProperty("uses").EnumerateArray().ToList();
        uses.Select(u => u.GetProperty("item").GetProperty("number").GetString()).ShouldBe(["4.1", "5.2"]);
        uses[0].GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("kind").GetString()).ShouldContain("addition");
        uses[1].GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("kind").GetString()).ShouldContain("context");
        uses[1].GetProperty("item").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{ProductEngineering}/write#scene=5.2&kb={SeedIds.KbEntries.GoodhartsLaw}");
        body.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.GoodhartMonetary);
    }

    [Test]
    public async Task Search_WhenQueryIsPartOfATitle_ShouldFindTheBook()
    {
        var body = await Client.OkAsync("search", new { query = "under" });

        body.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("item").GetProperty("id").GetGuid())
            .ShouldContain(SeedIds.Books.Undertow);
    }

    [Test]
    public async Task Search_WhenQueryIsAQuotationInProse_ShouldFindTheSceneWithTheSentence()
    {
        var body = await Client.OkAsync("search",
            new { book = ProductEngineering, query = "The customer is not in the building", types = new[] { "prose" } });

        var hit = body.GetProperty("hits").EnumerateArray().Single();
        hit.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch6Sc1);
        hit.GetProperty("snippet").GetString()!.ShouldContain("The customer is not in the building");
    }

    [Test]
    public async Task GetBookSettings_WhenCalled_ShouldReturnStyleGuideAndPublicationSettings()
    {
        var body = await Client.OkAsync("get_book_settings", new { book = ProductEngineering });

        body.GetProperty("title").GetString().ShouldBe("Product Engineering");
        body.GetProperty("publication").GetProperty("quote_word_limit").GetProperty("value").GetInt32().ShouldBe(300);
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{ProductEngineering}/settings");
    }

    [Test]
    public async Task ListTools_WhenListed_ShouldOfferThePhaseOneToolsAllReadOnly()
    {
        var tools = await Client.ListToolsAsync(cancellationToken: McpCalls.Token);

        // L9.2, W4.6a and L9.3 add the write tools beside these (create_book, update_book, the References tools); the read tools stay read-only (get_job is L9.2's one read tool; get_clearance and export_references are L9.3's; list_images and get_image are #65's).
        // Over stdio, #65's upload is upload_image; create_image_upload is HTTP's alone (McpImageUploadHttpTests).
        string[] phaseOne =
        [
            "export_references", "find_kb_usages", "get_book_settings", "get_clearance", "get_draft", "get_image", "get_job", "get_kb_entry", "get_outline",
            "list_books", "list_images", "list_kb_entries", "list_revisions", "read_manuscript", "resume_work", "search",
        ];
        string[] writes =
        [
            "cite_source", "create_book", "draft_prose", "edit_outline", "edit_prose", "link_kb_entry", "manage_permission", "place_figure", "record_clearance_decision",
            "resolve_draft", "set_opener", "submit_draft", "undo", "update_book", "update_figure", "update_source", "update_source_use", "upload_image", "upsert_beat", "upsert_kb_entry",
            "draft_permission_letter", "merge_sources", "export_for_printer", "manage_kb_category", "delete_kb_entry", "merge_kb_entries", "create_series", "update_series",
            "add_source", "archive_source",
        ];
        // preview_layout reads, but kind "proof" queues a job, so it is not read-only; an unchanged chapter returns the cached proof, so it is idempotent.
        string[] previews = ["preview_layout"];
        tools.Select(t => t.Name).ShouldBe([.. phaseOne, .. writes, .. previews], ignoreOrder: true);
        tools.Where(t => phaseOne.Contains(t.Name)).ShouldAllBe(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true
                               && t.ProtocolTool.Annotations.DestructiveHint == false && t.ProtocolTool.Annotations.IdempotentHint == true);
        tools.Where(t => previews.Contains(t.Name)).ShouldAllBe(t => t.ProtocolTool.Annotations!.ReadOnlyHint == false
                               && t.ProtocolTool.Annotations.DestructiveHint == false && t.ProtocolTool.Annotations.IdempotentHint == true);
        tools.Where(t => writes.Contains(t.Name)).ShouldAllBe(t => t.ProtocolTool.Annotations!.ReadOnlyHint != true
                               && t.ProtocolTool.Annotations.IdempotentHint == false);
    }

    [Test]
    public void ServerCapabilities_WhenInitialized_ShouldNotAdvertiseResourceSubscriptions()
    {
        Client.ServerCapabilities.Resources?.Subscribe.ShouldNotBe(true);
    }

    [Test]
    public async Task ReadResource_WhenSceneIsRead_ShouldReturnItsStoredBodyAndVersion()
    {
        var result = await Client.ReadResourceAsync($"codex://scene/{SeedIds.Scenes.Ch5Sc2}", cancellationToken: McpCalls.Token);

        var body = JsonDocument.Parse(result.Contents.OfType<TextResourceContents>().Single().Text).RootElement;
        body.GetProperty("body").GetString().ShouldBe(await ProseBodyAsync(SeedIds.Scenes.Ch5Sc2));
        body.GetProperty("version").GetInt32().ShouldBe(await ProseVersionAsync(SeedIds.Scenes.Ch5Sc2));
    }

    [Test]
    public async Task GetPrompt_WhenResumeIsAsked_ShouldBriefFromResumeWorkAndOfferThePendingDraft()
    {
        var prompt = await Client.GetPromptAsync("resume", cancellationToken: McpCalls.Token);

        var text = prompt.Messages.Single().Content.ShouldBeOfType<TextContentBlock>().Text;
        text.ShouldContain(SeedIds.AiGenerations.Ch5Sc2Pending.ToString());
        text.ShouldContain("apply it, retry it or discard it");
    }

    private async Task<int> ProseVersionAsync(Guid sceneId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT version FROM prose WHERE scene_id = @sceneId", new { sceneId });
    }

    private async Task<string> ProseBodyAsync(Guid sceneId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @sceneId", new { sceneId }) ?? "";
    }

    private async Task<string> DraftTextAsync(Guid generationId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT output FROM ai_generation WHERE id = @generationId", new { generationId }) ?? "";
    }

    private async Task<List<Guid>> LiveScenesInReadingOrderAsync(Guid bookId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return (await connection.QueryAsync<Guid>(
            """
            SELECT s.id FROM scene s
            JOIN chapter c ON c.id = s.chapter_id AND c.archived_at IS NULL
            JOIN act a ON a.id = c.act_id AND a.archived_at IS NULL
            WHERE s.book_id = @bookId AND s.archived_at IS NULL
            ORDER BY a.position, c.position, c.id, s.position, s.id
            """,
            new { bookId })).ToList();
    }
}
