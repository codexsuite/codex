namespace Codex.Application.Mcp;

// The words an agent reads for a source's clearance (#20 section F): the stored reason code worded as the References
// panel words it, and the one next_action the agent can take (reduce_quoted_words, request_permission, chase_permission,
// cut_or_request, sign_off, decide_fair_use, set_licence, fix_identifier, review_new_use, plus fill_details for a source
// whose citation is incomplete and none for a source that needs nothing).
public static class McpClearance
{
    public static string Reason(string code, int quotedWords, int limit) => code switch
    {
        "no_markers" => "No passage cites it",
        "quote_over_limit" => $"{quotedWords} of {limit} words, no request",
        "lyric" => "Lyric, no request",
        "figure" => "Figure, no request",
        "permission_drafted" => "Permission drafted, not sent",
        "permission_sent" => "Permission asked, no reply",
        "permission_refused" => "Permission refused",
        "licence_unknown" => "Licence unknown",
        "fair_use_undecided" => "Fair use not decided",
        "citation_incomplete" => "Citation incomplete",
        "identifier_failing" => "An ISBN, DOI or URL does not check out",
        "ready_to_sign_off" => $"{quotedWords} of {limit} words. Ready to sign off",
        "new_uses_since_signoff" => "New uses since sign-off",
        "signed_off" => "Signed off",
        _ => code.Replace('_', ' '),
    };

    public static string? NextAction(string status, string code) => status switch
    {
        "not_cited" or "cleared" => null,
        _ => code switch
        {
            "quote_over_limit" => "reduce_quoted_words",
            "lyric" or "permission_refused" => "cut_or_request",
            "figure" or "permission_drafted" => "request_permission",
            "permission_sent" => "chase_permission",
            "licence_unknown" => "set_licence",
            "fair_use_undecided" => "decide_fair_use",
            "citation_incomplete" => "fill_details",
            "identifier_failing" => "fix_identifier",
            "new_uses_since_signoff" => "review_new_use",
            "ready_to_sign_off" => "sign_off",
            _ => null,
        },
    };
}
