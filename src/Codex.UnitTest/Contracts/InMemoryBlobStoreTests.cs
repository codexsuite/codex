using Codex.Contracts.Storage;
using Codex.TestSupport.Contracts;
using Codex.TestSupport.Fakes;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class InMemoryBlobStoreTests : BlobStoreContract
{
    protected override IBlobStore Create() => new InMemoryBlobStore();
}
