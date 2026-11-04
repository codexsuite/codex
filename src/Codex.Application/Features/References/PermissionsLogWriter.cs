using System.Text;

namespace Codex.Application.Features.References;

public sealed record PermissionsLogRow(
    string Title,
    string Authors,
    int? Year,
    string Kind,
    string Licence,
    string? RightsHolder,
    string? PermissionsContact,
    int Uses,
    int QuotedWords,
    string Status,
    string Reason,
    string? PermissionStatus,
    string? SentOn,
    string? CreditLine,
    string? Terms,
    string? Note);

// The permissions log a publisher asks for, as CSV: one line per source that needs permission or has a request.
public static class PermissionsLogWriter
{
    private static readonly string[] Headings =
    [
        "Source", "Authors", "Year", "Kind", "Licence", "Rights holder", "Permissions contact", "Uses",
        "Quoted words", "Clearance", "Reason", "Permission request", "Sent on", "Credit line", "Terms", "Note",
    ];

    public static string Write(IEnumerable<PermissionsLogRow> rows)
    {
        var text = new StringBuilder();
        Append(text, Headings);
        foreach (var row in rows)
        {
            Append(text,
            [
                row.Title, row.Authors, row.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                row.Kind, row.Licence, row.RightsHolder ?? string.Empty, row.PermissionsContact ?? string.Empty,
                row.Uses.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.QuotedWords.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Status, row.Reason, row.PermissionStatus ?? "none", row.SentOn ?? string.Empty,
                row.CreditLine ?? string.Empty, row.Terms ?? string.Empty, row.Note ?? string.Empty,
            ]);
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string[] cells)
    {
        text.Append(string.Join(',', cells.Select(Quote))).Append("\r\n");
    }

    private static string Quote(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell;
}
