using System.Net;
using Codex.Application.Features.Ai;
using Codex.Application.Features.Ai.Providers;
using Codex.TestSupport.Fakes;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

// A user with no AI integration, and a key exported on the host that must change nothing (#36: no server-wide key).
[TestFixture]
public class DraftNoIntegrationTests : SchemaApiFixture
{
    private const string ExportedKey = "sk-ant-exported-on-the-host";
    private const string Variable = "CODEX_ANTHROPIC_API_KEY";

    private readonly string? _before;
    private Guid _beatId;

    // No network: the providers are a recording fake (this user never reaches them).
    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IAiProviders>(new RecordingAiProviders());

    public DraftNoIntegrationTests()
    {
        _before = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, ExportedKey);
    }

    [OneTimeTearDown]
    public void RestoreEnvironment() => Environment.SetEnvironmentVariable(Variable, _before);

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        _beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{_beatId}\"}}\n");
    }

    [Test]
    public void Host_WhateverTheExportedKeySays_ShouldRegisterTheIntegrationGenerator()
    {
        Environment.GetEnvironmentVariable(Variable).ShouldBe(ExportedKey);

        Host.Services.GetRequiredService<IProseGenerator>().ShouldBeOfType<IntegrationProseGenerator>();
    }

    [Test]
    public async Task Generate_WhenTheUserHasNoIntegration_ShouldReturn503NotConfiguredWithTheConnectMessageAndAFailedRow()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, "claude-sonnet-5-5", null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.ServiceUnavailable);
        error.Code.ShouldBe(ApiErrorCodes.NotConfigured);
        error.Error.ShouldBe("Connect an AI provider in Settings → AI to write drafts.");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE beat_id = @_beatId ORDER BY created_at DESC LIMIT 1", new { _beatId })).ShouldBe("failed");
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Generate_WhenThereIsNoModelAndNoDefaultModel_ShouldReturn400(string model)
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, model, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldBe("Choose a model for this draft.");
    }

    [TestCase("a-very-long-model-name")]
    [TestCase("bad\u0007model")]
    public async Task Generate_WhenTheModelNameIsNotValid_ShouldReturn400(string name)
    {
        var model = name.StartsWith("a-very", StringComparison.Ordinal) ? new string('m', 201) : name;

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, model, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
