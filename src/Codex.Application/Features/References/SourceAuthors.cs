using System.Text.Json;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.References;

// Authors as one editable line: "Beck, Kent; Poppendieck, Mary; DORA" (CSL names; a name without a comma is an organisation).
public static class SourceAuthors
{
    public static string ToText(string authorsJson)
    {
        using var document = JsonDocument.Parse(authorsJson);
        return string.Join("; ", CslNames.Names(document.RootElement).Select(n =>
            n.Literal ?? (string.IsNullOrEmpty(n.Given) ? n.Family ?? string.Empty : $"{n.Family}, {n.Given}")));
    }

    public static string ToJson(string text)
    {
        var names = text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var comma = part.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                return new Dictionary<string, string> { ["literal"] = part };
            }

            var family = part[..comma].Trim();
            var given = part[(comma + 1)..].Trim();
            if (family.Length == 0)
            {
                throw ApiException.Validation($"The author '{part}' has no family name.");
            }

            return given.Length == 0
                ? new Dictionary<string, string> { ["family"] = family }
                : new Dictionary<string, string> { ["family"] = family, ["given"] = given };
        }).ToList();
        return JsonSerializer.Serialize(names);
    }
}
