using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;
using Codex.Contracts.Ai.Integrations;
using Codex.TestSupport.Contracts;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.UnitTest.Contracts;

// The S5 contract against the OpenRouter provider, replaying Fixtures/ai/openai/chat-stream-openrouter.sse (a keep-alive comment, usage on the last chunk).
[TestFixture]
public class OpenRouterProseGeneratorTests : ProseGeneratorContract
{
    protected override IProseGenerator Create(ProseScript script)
        => new ChatCompletionsProseGenerator(
            ChatCompletionsStubHandler.Streaming(script.Chunks, script.Gate, script.FailWith, "chat-stream-openrouter.sse"),
            new RecordingEndpointGuard(),
            new AiConnection(AiIntegrationKinds.OpenRouter, null, "sk-or-test-key"),
            NullLogger<ChatCompletionsProseGenerator>.Instance);
}
