using Codex.Contracts.Storage;

namespace Codex.Application.Storage;

// Blobs as files under one root folder. The content type sits beside each blob in "<key>.content-type".
public sealed class LocalDiskBlobStore : IBlobStore
{
    private const string ContentTypeSuffix = ".content-type";

    private readonly string _root;

    public LocalDiskBlobStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        await File.WriteAllTextAsync(path + ContentTypeSuffix, contentType, cancellationToken);
    }

    public async Task<BlobContent?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }
        var contentType = File.Exists(path + ContentTypeSuffix)
            ? await File.ReadAllTextAsync(path + ContentTypeSuffix, cancellationToken)
            : "application/octet-stream";
        return new BlobContent(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true), contentType);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        var existed = File.Exists(path);
        File.Delete(path);
        File.Delete(path + ContentTypeSuffix);
        return Task.FromResult(existed);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(PathFor(key)));

    private string PathFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Contains('\\', StringComparison.Ordinal)
            || key.Contains('\0', StringComparison.Ordinal)
            || key.StartsWith('/')
            || key.EndsWith(ContentTypeSuffix, StringComparison.Ordinal)
            || key.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException($"'{key}' is not a valid blob key.", nameof(key));
        }
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{key}' is not a valid blob key.", nameof(key));
        }
        return full;
    }
}
