using Codex.Application.Features.Ai;
using Codex.Contracts.Ai;
using Codex.TestSupport.Contracts;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.UnitTest.Contracts;

// The S5 contract against the Anthropic provider, replaying the hand-written SSE stream (Fixtures/ai/anthropic-stream.sse,
// the documented event format with a leading thinking block) through a stub of the named "anthropic" HttpClient.
[TestFixture]
public class AnthropicProseGeneratorTests : ProseGeneratorContract
{
    protected override IProseGenerator Create(ProseScript script)
        => new AnthropicProseGenerator(
            AnthropicStubHandler.Streaming(script.Chunks, script.Gate, script.FailWith), "sk-ant-test-key", NullLogger<AnthropicProseGenerator>.Instance);
}
