namespace Codex.Contracts.Sources;

// Outcome is "found" (Source set), "not_found", or "redirect" (RedirectTo set: look that value up instead).
public sealed record SourceLookupResult(string Outcome, SourceMetadata? Source, string? RedirectTo)
{
    public const string FoundOutcome = "found";
    public const string NotFoundOutcome = "not_found";
    public const string RedirectOutcome = "redirect";

    public static SourceLookupResult Found(SourceMetadata source) => new(FoundOutcome, source, null);

    public static SourceLookupResult NotFound() => new(NotFoundOutcome, null, null);

    public static SourceLookupResult Redirect(string to) => new(RedirectOutcome, null, to);
}
