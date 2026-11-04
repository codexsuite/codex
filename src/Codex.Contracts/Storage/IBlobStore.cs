namespace Codex.Contracts.Storage;

// Files by key (covers, imports, exports). A key is a relative, slash-separated path such as
// "covers/<id>.png". Keys that are empty, rooted, or climb out with ".." throw ArgumentException.
public interface IBlobStore
{
    // Stores (or replaces) the blob with its content type.
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    // Null when the key holds nothing.
    Task<BlobContent?> GetAsync(string key, CancellationToken cancellationToken);

    // True when something was removed.
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}
