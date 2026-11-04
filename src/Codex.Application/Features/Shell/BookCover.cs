using System.Globalization;

namespace Codex.Application.Features.Shell;

// Cover fields the DB does not store: initials are derived from the title, and a book with no
// cover_palette gets one of the generated-cover tokens (cover-1..cover-8, the set Cover.tsx renders) picked from its id.
// An uploaded cover (cover_asset_id) travels as a URL.
public static class BookCover
{
    public const int PaletteCount = 8;

    // The web rendition (WebP, ~1600 px) once the worker has made it; until then the original, which always exists, so
    // a cover shows straight after upload. Null for no uploaded cover (or an archived one): the generated cover.
    public static string? Url(Guid? assetId, bool hasWeb) => assetId switch
    {
        null => null,
        { } id when hasWeb => $"/api/assets/{id}/renditions/web",
        { } id => $"/api/assets/{id}",
    };

    private static readonly HashSet<string> MinorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "for", "and", "to", "in", "on",
    };

    public static string Palette(Guid id, string? stored)
    {
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }
        var bytes = id.ToByteArray();
        return $"cover-{1 + (bytes[^1] % PaletteCount)}";
    }

    // "The Velocity Trap" -> "VT", "Northbound" -> "NO". Minor words are skipped when other words exist.
    public static string Initials(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var significant = words.Where(w => !MinorWords.Contains(w)).ToArray();
        if (significant.Length == 0)
        {
            significant = words;
        }
        var letters = significant.Length switch
        {
            0 => "",
            1 => new string(significant[0].Where(char.IsLetterOrDigit).Take(2).ToArray()),
            _ => string.Concat(significant.Take(2).Select(w => w.FirstOrDefault(char.IsLetterOrDigit).ToString())),
        };
        return letters.ToUpper(CultureInfo.InvariantCulture);
    }
}
