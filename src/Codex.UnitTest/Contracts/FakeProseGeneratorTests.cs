using Codex.Contracts.Ai;
using Codex.TestSupport.Contracts;
using Codex.TestSupport.Fakes;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class FakeProseGeneratorTests : ProseGeneratorContract
{
    protected override IProseGenerator Create(ProseScript script)
        => new FakeProseGenerator(script.Chunks, script.Gate, script.FailWith);
}
