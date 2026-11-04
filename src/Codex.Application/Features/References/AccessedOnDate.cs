using System.Globalization;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.References;

// The inbound edge for a source's accessed_on day (HTTP and MCP): one exact format, no later than the viewer's today, no earlier than 1900.
internal static class AccessedOnDate
{
    private const int EarliestYear = 1900;

    public static DateOnly? Parse(string? text, DateOnly today)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw ApiException.Validation($"'{text}' is not a date: use yyyy-MM-dd.");
        }

        if (day > today)
        {
            throw ApiException.Validation("The accessed date cannot be in the future.");
        }

        return day.Year < EarliestYear
            ? throw ApiException.Validation($"The accessed date cannot be before {EarliestYear}.")
            : day;
    }
}
