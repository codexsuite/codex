using Shouldly;
using System.Text;
using Codex.Contracts.Storage;

namespace Codex.TestSupport.Contracts;

// Every IBlobStore implementation inherits this and supplies Create().
public abstract class BlobStoreContract
{
    protected abstract IBlobStore Create();

    [Test]
    public async Task PutAsync_WhenKeyIsNew_ShouldStoreBytesAndContentType()
    {
        var store = Create();

        await store.PutAsync("covers/a.png", Bytes("png-bytes"), "image/png", CancellationToken.None);

        (await store.ExistsAsync("covers/a.png", CancellationToken.None)).ShouldBeTrue();
        var blob = (await store.GetAsync("covers/a.png", CancellationToken.None)).ShouldNotBeNull();
        blob.ContentType.ShouldBe("image/png");
        await using var content = blob.Content;
        (await new StreamReader(content).ReadToEndAsync()).ShouldBe("png-bytes");
    }

    [Test]
    public async Task PutAsync_WhenKeyExists_ShouldReplaceIt()
    {
        var store = Create();
        await store.PutAsync("a.txt", Bytes("one"), "text/plain", CancellationToken.None);

        await store.PutAsync("a.txt", Bytes("two"), "text/markdown", CancellationToken.None);

        var blob = (await store.GetAsync("a.txt", CancellationToken.None)).ShouldNotBeNull();
        blob.ContentType.ShouldBe("text/markdown");
        await using var content = blob.Content;
        (await new StreamReader(content).ReadToEndAsync()).ShouldBe("two");
    }

    [Test]
    public async Task GetAsync_WhenKeyIsMissing_ShouldReturnNull()
    {
        (await Create().GetAsync("nothing/here", CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task DeleteAsync_WhenKeyExists_ShouldRemoveItOnce()
    {
        var store = Create();
        await store.PutAsync("a.txt", Bytes("x"), "text/plain", CancellationToken.None);

        (await store.DeleteAsync("a.txt", CancellationToken.None)).ShouldBeTrue();
        (await store.DeleteAsync("a.txt", CancellationToken.None)).ShouldBeFalse();
        (await store.ExistsAsync("a.txt", CancellationToken.None)).ShouldBeFalse();
    }

    [TestCase("../outside.txt")]
    [TestCase("covers/../../outside.txt")]
    [TestCase("/etc/passwd")]
    [TestCase("")]
    public async Task PutAsync_WhenKeyTraverses_ShouldReject(string key)
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentException>(
            async () => await store.PutAsync(key, Bytes("x"), "text/plain", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(async () => await store.GetAsync(key, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(async () => await store.DeleteAsync(key, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(async () => await store.ExistsAsync(key, CancellationToken.None));
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));
}
