using Codex.Application.Features.Ai;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.WebApi.Tests.Ai;

[TestFixture]
public class AnthropicDraftStopTests : SchemaApiFixture
{
    private static readonly AnthropicStubHandler Handler = AnthropicStubHandler.Streaming(["The ", "door ", "creaked."], new TaskCompletionSource().Task);
    private static readonly FinishSignallingGenerator Generator = new(new AnthropicProseGenerator(Handler, AnthropicDraftRows.Key, NullLogger<AnthropicProseGenerator>.Instance));

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    [OneTimeTearDown]
    public void DisposeHandler() => Handler.Dispose();

    [Test]
    public async Task Generate_WhenTheClientStopsMidStream_ShouldKeepThePartialTextAsAPendingDraft()
    {
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var db = await OpenDatabaseAsync();
        var beatId = await AnthropicDraftRows.BeatAsync(db);

        var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(null, AnthropicDraftRows.Model, null, null), patience.Token);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(patience.Token));
        var started = (await DraftStreams.NextAsync(reader, patience.Token))?.Started.ShouldNotBeNull();
        (await DraftStreams.NextAsync(reader, patience.Token))?.Text.ShouldBe("The ");
        reader.Dispose();
        response.Dispose();
        await AnthropicDraftRows.WithinPatienceAsync(Generator.Finished.Task);

        var row = await db.QuerySingleAsync<(string Status, string Output, int? OutputTokens)>(
            "SELECT status, output, output_tokens FROM ai_generation WHERE id = @id", new { id = started!.GenerationId });
        row.ShouldBe(("pending", "The ", null));
    }
}
