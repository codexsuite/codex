namespace Codex.Contracts.References;

// Status is cleared, needs_check, risk or not_cited; Reason the clearance code. Action is the one next step the panel
// offers: sign_off, reopen, accept_fair_use, set_licence, fill_details, recheck, remove, permission (the permission
// tracker's step) or none. SignedOffBy and SignedOffLabel ("2h ago") are set while the source is signed off.
public sealed record SourceVerdictDto(
    string Status,
    string Reason,
    string Action,
    string? SignedOffBy,
    string? SignedOffLabel);
