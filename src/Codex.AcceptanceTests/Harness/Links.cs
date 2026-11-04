namespace Codex.AcceptanceTests.Harness;

// App URLs built from strings, mirroring plan decision D8 and design/README.md "Moving between screens":
// `/`, `/#series=<id>`, `/books/:id/{plan,write,references,layout,settings}` plus hash and query deep links.
// The client's TypeScript link builders cannot be called from C#, so this file must be kept in step with
// them and with docs/navigation.md. Pass the result to Page.GotoAsync.
public static class Links
{
    public static string Library() => "/";

    public static string Series(string seriesId) => "/" + Hash(("series", seriesId));

    public static string Plan(string bookId, string? hash = null, string? query = null) => Book(bookId, "plan", hash, query);

    public static string Write(string bookId, string? hash = null, string? query = null) => Book(bookId, "write", hash, query);

    public static string References(string bookId, string? hash = null, string? query = null) => Book(bookId, "references", hash, query);

    public static string Layout(string bookId, string? hash = null, string? query = null) => Book(bookId, "layout", hash, query);

    public static string Settings(string bookId, string? hash = null, string? query = null) => Book(bookId, "settings", hash, query);

    // Deep-link hash: Hash(("scene", "5.2"), ("ref", "s:abc")) -> "#scene=5.2&ref=s%3Aabc".
    // A null value makes a bare key, for the flag-style links: Hash(("new", null)) -> "#new",
    // Hash(("citations", null)) -> "#citations" (Settings sections: book, series, writing, citations, rules, letters,
    // layout, sharing, archive), and References' "#<source>" -> Hash((sourceId, null)). Settings is never recorded as codex.last.
    public static string Hash(params (string Key, string? Value)[] parameters) =>
        "#" + string.Join("&", parameters.Select(p => p.Value is null ? Escape(p.Key) : $"{Escape(p.Key)}={Escape(p.Value)}"));

    // Query string: Query(("return", Write(id, "#scene=5.2")), ("sel", "text")) -> "?return=%2Fbooks%2F…&sel=text"
    // (return must be a path inside Codex). Same null-value rule as Hash.
    public static string Query(params (string Key, string? Value)[] parameters) =>
        "?" + string.Join("&", parameters.Select(p => p.Value is null ? Escape(p.Key) : $"{Escape(p.Key)}={Escape(p.Value)}"));

    private static string Book(string bookId, string screen, string? hash, string? query) =>
        $"/books/{Escape(bookId)}/{screen}{query}{hash}";

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
