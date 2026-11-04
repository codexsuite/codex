using System.Collections.Concurrent;
using Codex.Contracts.Storage;

namespace Codex.TestSupport.Fakes;

// A dictionary-backed IBlobStore for tests that do not need the disk.
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _blobs = new(StringComparer.Ordinal);

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        Check(key);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _blobs[key] = (buffer.ToArray(), contentType);
    }

    public Task<BlobContent?> GetAsync(string key, CancellationToken cancellationToken)
    {
        Check(key);
        return Task.FromResult(_blobs.TryGetValue(key, out var blob)
            ? new BlobContent(new MemoryStream(blob.Bytes), blob.ContentType)
            : null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Check(key);
        return Task.FromResult(_blobs.TryRemove(key, out _));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        Check(key);
        return Task.FromResult(_blobs.ContainsKey(key));
    }

    private static void Check(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Split('/').Any(s => s is "" or "." or ".."))
        {
            throw new ArgumentException($"'{key}' is not a valid blob key.", nameof(key));
        }
    }
}
