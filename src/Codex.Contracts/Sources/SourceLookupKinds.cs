namespace Codex.Contracts.Sources;

public static class SourceLookupKinds
{
    public const string Isbn = "isbn";
    public const string Doi = "doi";
    public const string Url = "url";

    public static IReadOnlyList<string> All { get; } = [Isbn, Doi, Url];
}
