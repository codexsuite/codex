namespace Codex.Contracts.Sources;

// What a lookup knows about a source; the References form fills its fields from this.
public sealed record SourceMetadata(
    string Title,
    IReadOnlyList<string> Authors,
    int? Year,
    string? Publisher,
    string? Url);
