using Codex.Application.Features.Ai;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.WebApi.Tests.Ai;

// Shared setup for the Anthropic-provider draft tests: the named "anthropic" HttpClient is stubbed with a hand-written
// SSE transcript (Fixtures/ai), so the stream lines, the ai_generation row and the token counts are the production ones.
internal static class AnthropicDraftRows
{
    public const string Model = "claude-sonnet-5-5";
    public const string Key = "sk-ant-test-key";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // The real DI path: the generator is built over the factory's named client, whose primary handler is the stub.
    public static Action<IServiceCollection> Provider(AnthropicStubHandler handler) => services =>
    {
        services.AddHttpClient(AnthropicProseGenerator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IProseGenerator>(sp => new AnthropicProseGenerator(
            sp.GetRequiredService<IHttpClientFactory>(), Key, sp.GetRequiredService<ILogger<AnthropicProseGenerator>>()));
    };

    public static async Task<Guid> BeatAsync(NpgsqlConnection db)
    {
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{beatId}\"}}\n");
        return beatId;
    }

    // Waits for a signal, failing (instead of hanging the run) if it never comes.
    public static async Task WithinPatienceAsync(Task signal)
    {
        if (await Task.WhenAny(signal, Task.Delay(Patience)) != signal)
        {
            throw new TimeoutException($"The server did not finish the stream within {Patience.TotalSeconds:0} s.");
        }
    }
}
