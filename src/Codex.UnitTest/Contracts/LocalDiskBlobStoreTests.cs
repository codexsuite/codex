using Codex.Application.Storage;
using Codex.Contracts.Storage;
using Codex.TestSupport.Contracts;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class LocalDiskBlobStoreTests : BlobStoreContract
{
    private string _root = null!;

    [SetUp]
    public void CreateRoot() => _root = Path.Combine(Path.GetTempPath(), "codex-unit-blobs", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    protected override IBlobStore Create() => new LocalDiskBlobStore(_root);

    [Test]
    public async Task PutAsync_WhenKeyTraverses_ShouldNotWriteOutsideTheRoot()
    {
        var store = Create();
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "escaped.txt");

        await Should.ThrowAsync<ArgumentException>(async () => await store.PutAsync(
            "../escaped.txt", new MemoryStream([1]), "text/plain", CancellationToken.None));

        File.Exists(outside).ShouldBeFalse();
    }
}
