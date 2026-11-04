using Codex.Contracts.Sources;
using Codex.TestSupport.Contracts;
using Codex.TestSupport.Fakes;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class FakeSourceLookupTests : SourceLookupContract
{
    protected override ISourceLookup Create() => new FakeSourceLookup();
}
