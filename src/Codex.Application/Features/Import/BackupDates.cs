using System.Globalization;
using System.Text.Json.Nodes;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Import;

// The inbound edge for a backup's dates (utc-everywhere): a backup row is written with jsonb_populate_recordset, which
// reads a timestamptz string without an offset in the session zone and a malformed one as a database error. So the
// parser checks every date field first. The columns follow one naming rule (schema: `*_at` is a timestamptz, `*_on` is a
// date), so a new column is covered without a list to keep. A `date` is exactly yyyy-MM-dd; an instant is ISO-8601 with
// an explicit `Z` or +hh:mm offset (what to_jsonb writes), with up to seven fraction digits. Null is a blank column.
internal static class BackupDates
{
    private static readonly string[] InstantFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    // Checks the date fields of one row of `table` (a top-level property is a column; nested JSON is not).
    public static void Check(JsonObject row, string table)
    {
        foreach (var (field, value) in row)
        {
            var isInstant = field.EndsWith("_at", StringComparison.Ordinal);
            if ((!isInstant && !field.EndsWith("_on", StringComparison.Ordinal)) || value is null)
            {
                continue;
            }

            var text = value is JsonValue json && json.TryGetValue<string>(out var s) ? s : null;
            var valid = text is not null && (isInstant
                ? HasOffset(text) && DateTimeOffset.TryParseExact(text, InstantFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            if (!valid)
            {
                var expected = isInstant ? "an ISO-8601 date and time with an offset, like 2026-10-19T08:12:33Z" : "a date as yyyy-MM-dd";
                throw ApiException.Validation($"{table}.{field} in this backup is not {expected}.");
            }
        }
    }

    // zzz also reads +07 and +0700; a backup writes +hh:mm or Z, so anything else is refused.
    private static bool HasOffset(string text) =>
        text.EndsWith('Z') || (text.Length > 6 && text[^3] == ':' && text[^6] is '+' or '-');

    // Checks the book's own fields and every row of every table-shaped array in a backup file.
    public static void CheckFile(JsonObject file)
    {
        foreach (var (property, node) in file)
        {
            if (node is JsonArray rows)
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    Check(row, property);
                }
            }
            else if (node is JsonObject nested)
            {
                Check(nested, property);
            }
        }
    }
}
