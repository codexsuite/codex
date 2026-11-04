namespace Codex.TestSupport.Fakes;

// One request a stub handler received: method, absolute URL, body text and headers (case-insensitive, request headers only).
public sealed record StubbedRequest(string Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);
