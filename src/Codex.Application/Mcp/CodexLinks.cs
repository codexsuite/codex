namespace Codex.Application.Mcp;

// The server's copy of the SPA's link builders (src/Codex.Core.Client/src/shell/links.ts, docs/navigation.md), so an
// MCP open_url is exactly the URL the UI builds for the same place (D8). Same paths, same key order, values escaped
// as encodeURIComponent does for ids and display numbers. CodexLinksTests pins the outputs against links.ts.
public static class CodexLinks
{
    public static string Series(Guid seriesId) => $"/#series={Enc(seriesId)}";

    public static string Plan(Guid bookId, string? scene = null, string? chapter = null, Guid? kb = null, Guid? entry = null) =>
        BookPath(bookId, "plan") + Hash(null, ("scene", scene), ("chapter", chapter), ("kb", Id(kb)), ("entry", Id(entry)));

    public static string Write(
        Guid bookId, string? scene = null, string? chapter = null, Guid? kb = null, Guid? entry = null, Guid? reference = null) =>
        BookPath(bookId, "write")
        + Hash(null, ("scene", scene), ("chapter", chapter), ("kb", Id(kb)), ("entry", Id(entry)), ("ref", Id(reference)));

    public static string References(Guid bookId, Guid? source = null) => BookPath(bookId, "references") + Hash(Id(source));

    public static string Layout(Guid bookId, string? scene = null, string? chapter = null) =>
        BookPath(bookId, "layout") + Hash(null, ("scene", scene), ("chapter", chapter));

    // section: book, series, writing, citations, rules, letters, layout, archive.
    public static string Settings(Guid bookId, string? section = null) => BookPath(bookId, "settings") + Hash(section);

    private static string BookPath(Guid bookId, string screen) => $"/books/{Enc(bookId)}/{screen}";

    private static string Hash(string? bare, params (string Key, string? Value)[] pairs)
    {
        var parts = new List<string>();
        if (bare != null)
        {
            parts.Add(Uri.EscapeDataString(bare));
        }
        parts.AddRange(pairs.Where(p => p.Value != null).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
        return parts.Count == 0 ? "" : "#" + string.Join('&', parts);
    }

    private static string? Id(Guid? id) => id?.ToString();

    private static string Enc(Guid id) => id.ToString();
}
