namespace Codex.Contracts.Sources;

// Resolves an ISBN, DOI or URL to source metadata. `kind` is one of SourceLookupKinds.All; any other kind
// throws ArgumentException. A value nobody knows is a NotFound result, not an exception.
public interface ISourceLookup
{
    Task<SourceLookupResult> LookupAsync(string kind, string value, CancellationToken cancellationToken);
}
