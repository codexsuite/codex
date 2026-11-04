namespace Codex.Contracts.References;

// The permission tracker of one source: its live request, or the latest one that was answered (a withdrawn request
// counts as none). Status is draft, sent, granted or refused. RemindOn is a yyyy-MM-dd date; the labels are worded by
// the server ("Asked 3 Oct", "Reminder 17 Oct", "Reminder due"). Note is the reason a refusal gave.
// FollowUp is the short chaser for a sent request (null otherwise); FollowedUpLabel says when it was last marked as
// followed up ("Followed up 19 Oct"); FeeAmount is the fee a grant named.
public sealed record PermissionDto(
    Guid Id,
    string Status,
    string To,
    string Letter,
    string? SentLabel,
    string? RemindOn,
    string? RemindLabel,
    bool RemindDue,
    string? RespondedLabel,
    string? CreditLine,
    string? Terms,
    string? Note,
    PermissionLetterDto? Attachment,
    string? FollowUp,
    string? FollowedUpLabel,
    decimal? FeeAmount);
