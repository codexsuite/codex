using Codex.Contracts.References;

namespace Codex.Application.Features.References;

// What the clearance view knows about a source, in the shape the checklist and the next action are worked out from.
public sealed record SourceFacts(
    string Status,
    string Reason,
    string Licence,
    string LicenceLabel,
    bool AllowsReuse,
    bool FairUseOpen,
    string? FairUseNote,
    int QuotedWords,
    int LyricWords,
    int FigureCount,
    int DataCount,
    int Limit,
    IReadOnlyList<string> Missing,
    IReadOnlyList<IdentifierCheckDto> Identifiers,
    bool SignedOff);

// The panel's checklist and the one next step, from the same facts the view decides the status on.
public static class SourceChecklist
{
    public static IReadOnlyList<ClearanceCheckDto> Checks(SourceFacts f)
    {
        var publicDomain = f.Licence == "public_domain";
        return [Words(f, publicDomain), Lyrics(f, publicDomain), Figures(f, publicDomain), Data(f), Licence(f), Citation(f), Identifiers(f)];
    }

    private static ClearanceCheckDto Words(SourceFacts f, bool publicDomain)
    {
        const string title = "Prose quoted against the limit";
        if (f.QuotedWords > f.Limit && !publicDomain)
        {
            return new("words", "danger", title, $"{f.QuotedWords} of {f.Limit} words quoted. Permission is needed above the limit.");
        }

        return new("words", "ok", title, f.QuotedWords == 0 ? "No prose quoted." : $"{f.QuotedWords} of {f.Limit} words quoted.");
    }

    private static ClearanceCheckDto Lyrics(SourceFacts f, bool publicDomain) =>
        f.LyricWords > 0 && !publicDomain
            ? new("lyrics", "danger", "Lyrics and poetry", $"{f.LyricWords} words of lyrics or poetry always need permission.")
            : new("lyrics", "ok", "Lyrics and poetry", "None quoted.");

    private static ClearanceCheckDto Figures(SourceFacts f, bool publicDomain)
    {
        if (f.FigureCount > 0 && !f.AllowsReuse && !publicDomain)
        {
            return new("figures", "danger", "Figures", "A reproduced figure needs permission under this licence.");
        }

        return new("figures", "ok", "Figures", f.FigureCount == 0 ? "None reproduced." : "Reuse is allowed by the licence.");
    }

    private static ClearanceCheckDto Data(SourceFacts f)
    {
        var noun = f.DataCount == 1 ? "use" : "uses";
        var detail = f.DataCount == 0 ? "No data reproduced." : $"{f.DataCount} data {noun}.";
        return new("data", "ok", "Data", detail);
    }

    private static ClearanceCheckDto Licence(SourceFacts f)
    {
        if (f.Licence == "unknown")
        {
            return new("licence", "warn", "Licence", "The licence is not recorded.");
        }

        if (f.FairUseOpen)
        {
            return new("licence", "warn", "Licence", $"{f.LicenceLabel}: a partial quote needs a fair-use decision.");
        }

        var detail = f.FairUseNote is { Length: > 0 } ? $"{f.LicenceLabel}. Fair use: {f.FairUseNote}" : f.LicenceLabel;
        return new("licence", "ok", "Licence", detail);
    }

    private static ClearanceCheckDto Citation(SourceFacts f) =>
        f.Missing.Count > 0
            ? new("citation", "warn", "Citation complete", $"Missing: {string.Join(", ", f.Missing.Select(m => m.Replace('_', ' ')))}.")
            : new("citation", "ok", "Citation complete", "Every required field is filled in.");

    private static ClearanceCheckDto Identifiers(SourceFacts f)
    {
        var failing = f.Identifiers.Where(i => i.Status is "redirect" or "not_found" or "error").ToList();
        return failing.Count > 0
            ? new("identifiers", "warn", "Identifiers verified", string.Join(" ", failing.Select(IdentifierText)))
            : new("identifiers", "ok", "Identifiers verified", "No identifier is failing.");
    }

    public static string NextAction(SourceFacts f) => f.Status switch
    {
        "not_cited" => "remove",
        "risk" => "permission",
        "cleared" => "reopen",
        _ => f.Reason switch
        {
            "licence_unknown" => "set_licence",
            "fair_use_undecided" => "accept_fair_use",
            "citation_incomplete" => "fill_details",
            "identifier_failing" => "recheck",
            _ => "sign_off",
        },
    };

    private static string IdentifierText(IdentifierCheckDto i)
    {
        var outcome = i.Status switch
        {
            "redirect" => "redirects",
            "not_found" => "was not found",
            _ => "could not be checked",
        };
        return $"The {i.Field.ToUpperInvariant()} {outcome}.";
    }
}
