using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;
using Codex.Contracts.Ai.Integrations;
using Codex.TestSupport.Contracts;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.UnitTest.Contracts;

// The S5 contract against the OpenAI-compatible provider, replaying Fixtures/ai/openai/chat-stream.sse through a stub of the named client.
[TestFixture]
public class ChatCompletionsProseGeneratorTests : ProseGeneratorContract
{
    protected override IProseGenerator Create(ProseScript script)
        => new ChatCompletionsProseGenerator(
            ChatCompletionsStubHandler.Streaming(script.Chunks, script.Gate, script.FailWith),
            new RecordingEndpointGuard(),
            new AiConnection(AiIntegrationKinds.OpenAiCompatible, new Uri("https://api.example.test/v1"), "sk-test-key"),
            NullLogger<ChatCompletionsProseGenerator>.Instance);
}
