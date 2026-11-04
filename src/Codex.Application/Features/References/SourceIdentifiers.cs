using System.Text.RegularExpressions;

namespace Codex.Application.Features.References;

// Canonical forms of the identifiers a source is matched on. Each returns null for text that is not that identifier.
public static partial class SourceIdentifiers
{
    // ISBN-10 or ISBN-13, hyphens and spaces allowed, checksum checked. The result is the ISBN-13 digits.
    public static string? NormaliseIsbn(string? raw)
    {
        var text = new string((raw ?? string.Empty).Where(c => c is not ('-' or ' ')).ToArray()).ToUpperInvariant();
        if (text.StartsWith("ISBN", StringComparison.Ordinal))
        {
            text = text[4..].TrimStart(':');
        }

        return text.Length switch
        {
            13 when text.All(char.IsAsciiDigit) && Isbn13Check(text[..12]) == text[12] => text,
            10 when text[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(text[9]) || text[9] == 'X') && Isbn10Valid(text) => ToIsbn13(text),
            _ => null,
        };
    }

    // "https://doi.org/10.1000/ABC", "doi:10.1000/abc" and "10.1000/abc" all give "10.1000/abc" (case kept: matching ignores it).
    public static string? NormaliseDoi(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        text = DoiPrefix().Replace(text, string.Empty, 1).Trim();

        return DoiPattern().IsMatch(text) ? text : null;
    }

    public static string? NormaliseUrl(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0
            ? text
            : null;
    }

    // Lenient form for fields saved as typed: ISBN-13 when it is a valid ISBN, otherwise the text without hyphens and spaces.
    public static string? CleanIsbn(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : NormaliseIsbn(raw) ?? raw.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static char Isbn13Check(string twelve)
    {
        var sum = twelve.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    private static bool Isbn10Valid(string text)
    {
        var sum = text.Select((c, i) => (c == 'X' ? 10 : c - '0') * (10 - i)).Sum();
        return sum % 11 == 0;
    }

    private static string ToIsbn13(string isbn10)
    {
        var twelve = "978" + isbn10[..9];
        return twelve + Isbn13Check(twelve);
    }

    [GeneratedRegex(@"^(https?://(dx\.)?doi\.org/|doi:)", RegexOptions.IgnoreCase)]
    private static partial Regex DoiPrefix();

    [GeneratedRegex(@"^10\.\d{4,9}/\S+$")]
    private static partial Regex DoiPattern();
}
