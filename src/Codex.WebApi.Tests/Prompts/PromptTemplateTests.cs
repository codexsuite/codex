using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Prompts;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fakes;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Tests.Ai;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Prompts;

// Settings -> AI, Prompt templates (J22): the user's own templates, and a draft that uses one through the user's AI
// integration (#36), with "Show what was sent" read back from the stored request.
[TestFixture]
public class PromptTemplateTests : SchemaApiFixture
{
    private const string Key = "sk-ant-template-secret-a1b2";
    private static readonly Guid UserB = new("0199c0de-0000-7000-8000-0000000000c3");
    private static readonly RecordingAiProviders Providers = new();

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IAiProviders>(Providers);

    [OneTimeSetUp]
    public async Task CreateRowsAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "ai-b@example.test", "User B", id: UserB);
        await using var db = await OpenDatabaseAsync();
        var protector = Host.Services.GetRequiredService<IAiCredentialProtector>();
        await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "My gateway", Key, "openai_compatible", "https://llm.example.test/v1",
            defaultModel: "llama3", isDefault: true);
    }

    private Task<AiTemplatesDto> TemplatesAsync() => DraftStreams.GetAsync<AiTemplatesDto>(Client, "/api/ai/templates");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
        => await Client.SendAsync(new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body) });

    private async Task<AiTemplateDto> CreateTemplateAsync(string name, string body = "Write {{beat}} in {{length}} words.")
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/ai/templates", new SaveTemplateRequest(name, null, body));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await DraftStreams.ReadAsync<AiTemplateDto>(response);
    }

    [Test]
    public async Task Get_ShouldListTheBuiltInsAsReadOnlyWithTheVariables()
    {
        var templates = await TemplatesAsync();

        templates.Templates.Where(t => t.BuiltIn).Select(t => t.Id).ShouldBe(["continue-writing", "joels-voice", "paraphrase"]);
        templates.Variables.ShouldBe(["beat", "context", "style_guide", "previous_prose", "length"]);
    }

    [Test]
    public async Task DuplicateTemplate_WhenCopyingABuiltIn_ShouldAddAnEditableCopyThatKeepsTheInstruction()
    {
        var builtIn = (await TemplatesAsync()).Templates[0];

        using var response = await SendAsync(HttpMethod.Post, $"/api/ai/templates/{builtIn.Id}/duplicate", new DuplicateTemplateRequest("Copy of built-in"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var copy = await DraftStreams.ReadAsync<AiTemplateDto>(response);
        copy.BuiltIn.ShouldBeFalse();
        copy.Body.ShouldContain(builtIn.Body);
        copy.Body.ShouldContain("{{beat}}");
        (await DraftStreams.GetAsync<PromptsDto>(Client, "/api/prompts")).Templates.ShouldContain(t => t.Id == copy.Id && !t.BuiltIn);
    }

    [Test]
    public async Task CreateTemplate_WhenTheNameIsTaken_ShouldBeAConflictAndAddNoRow()
    {
        await CreateTemplateAsync("Taken name");

        using var response = await SendAsync(HttpMethod.Post, "/api/ai/templates", new SaveTemplateRequest("taken NAME", null, "Write {{beat}}."));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TemplatesAsync()).Templates.Count(t => t.Name.Equals("taken name", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Test]
    public async Task DeleteTemplate_ShouldArchiveItFreeItsNameAndHideItEverywhere()
    {
        var template = await CreateTemplateAsync("Short lived");

        using var deleted = await SendAsync(HttpMethod.Delete, $"/api/ai/templates/{template.Id}");

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var db = await OpenDatabaseAsync())
        {
            (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM prompt_template WHERE id = @Id", new { Id = Guid.Parse(template.Id) })).ShouldBeTrue();
        }

        (await TemplatesAsync()).Templates.ShouldNotContain(t => t.Id == template.Id);
        (await DraftStreams.GetAsync<PromptsDto>(Client, "/api/prompts")).Templates.ShouldNotContain(t => t.Id == template.Id);
        using var again = await SendAsync(HttpMethod.Delete, $"/api/ai/templates/{template.Id}");
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var edit = await SendAsync(HttpMethod.Put, $"/api/ai/templates/{template.Id}", new SaveTemplateRequest("Short lived", null, "x {{beat}}"));
        edit.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var copy = await SendAsync(HttpMethod.Post, $"/api/ai/templates/{template.Id}/duplicate", new DuplicateTemplateRequest("Copy"));
        copy.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var reused = await CreateTemplateAsync("short LIVED");
        reused.Id.ShouldNotBe(template.Id);
    }

    [Test]
    public async Task CreateTemplate_WhenTheBodyIsEmpty_ShouldBeRefused()
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/ai/templates", new SaveTemplateRequest("Empty body", null, "   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("The prompt cannot be empty");
    }

    [Test]
    public async Task UpdateTemplate_WhenItIsBuiltIn_ShouldBeRefusedWithTheWayOut()
    {
        var builtIn = (await TemplatesAsync()).Templates[0];

        using var response = await SendAsync(HttpMethod.Put, $"/api/ai/templates/{builtIn.Id}", new SaveTemplateRequest("Renamed", null, "x {{beat}}"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Duplicate");
    }

    [Test]
    public async Task UpdateTemplate_WhenItBelongsToAnotherUser_ShouldBeNotFound()
    {
        var mine = await CreateTemplateAsync("Only mine");
        using var other = Host.CreateClient().ActingAs(UserB);

        using var response = await other.PutAsJsonAsync($"/api/ai/templates/{mine.Id}", new SaveTemplateRequest("Stolen", null, "x {{beat}}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<AiTemplatesDto>("/api/ai/templates", System.Text.Json.JsonSerializerOptions.Web))!.Templates.ShouldNotContain(t => t.Id == mine.Id);
    }

    [Test]
    public async Task Generate_WithAnotherUsersTemplate_ShouldBeAnUnknownTemplate()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{beatId}\"}}\n");
        var theirs = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO prompt_template (user_id, name, body) VALUES (@UserB, 'Theirs', 'x {{beat}}') RETURNING id", new { UserB });

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(theirs.ToString(), "", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Generate_WithAUserTemplate_ShouldSendTheFilledTemplateThroughTheIntegrationAndRecordWhatWasSent()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{beatId}\"}}\n");
        var template = await CreateTemplateAsync("Joel's voice (nonfiction)", "Be brisk.\nBeat: {{beat}}\nAbout {{length}} words.");
        var generator = new FakeProseGenerator(["Some words."]);
        Providers.Next = generator;

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(template.Id, "", 150, null));
        var events = await DraftStreams.ReadAllAsync(response);

        var final = events[^1].Final.ShouldNotBeNull();
        var sentToProvider = generator.Requests.Single();
        (sentToProvider.Model, sentToProvider.Instruction, sentToProvider.Prompt).ShouldBe(("llama3", "", "Be brisk.\nBeat: beat\nAbout 150 words."));
        var sent = await DraftStreams.GetAsync<DraftSentDto>(Client, $"/api/generations/{final.GenerationId}/sent");
        (sent.Template, sent.Model, sent.Integration).ShouldBe(("Joel's voice (nonfiction)", "llama3", "My gateway"));
        (sent.InputTokens, sent.OutputTokens).ShouldBe((final.InputTokens, final.OutputTokens));
        (sent.Instruction, sent.Prompt).ShouldBe(("", sentToProvider.Prompt));
        (await Client.GetStringAsync($"/api/generations/{final.GenerationId}/sent")).ShouldNotContain(Key);
    }

    [Test]
    public async Task Generate_WithABuiltInTemplate_ShouldRecordTheInstructionAndPromptSent()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{beatId}\"}}\n");
        var generator = new FakeProseGenerator(["Some words."]);
        Providers.Next = generator;

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest("paraphrase", "", null, null));
        var final = (await DraftStreams.ReadAllAsync(response))[^1].Final.ShouldNotBeNull();

        var sentToProvider = generator.Requests.Single();
        var sent = await DraftStreams.GetAsync<DraftSentDto>(Client, $"/api/generations/{final.GenerationId}/sent");
        (sent.Template, sent.Instruction, sent.Prompt).ShouldBe(("Paraphrase", sentToProvider.Instruction, sentToProvider.Prompt));
        sent.Instruction.ShouldNotBeEmpty();
    }

    [Test]
    public async Task Generate_WhenATemplateIsEditedAfterwards_ShouldKeepTheOldRequestSnapshot()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{beatId}\"}}\n");
        var template = await CreateTemplateAsync("Edited later", "Original line. {{beat}}");
        Providers.Next = new FakeProseGenerator(["Some words."]);
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(template.Id, "", null, null));
        var generation = (await DraftStreams.ReadAllAsync(response))[^1].Final!.GenerationId;

        using var edit = await SendAsync(HttpMethod.Put, $"/api/ai/templates/{template.Id}", new SaveTemplateRequest("Edited later", null, "Changed line. {{beat}}"));

        edit.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DraftStreams.GetAsync<DraftSentDto>(Client, $"/api/generations/{generation}/sent")).Prompt.ShouldContain("Original line.");
    }
}
