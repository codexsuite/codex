namespace Codex.Contracts.References;

// One row of the References table. Number is the marker number (order of first use in the book, the one Write
// shows) and is null for a source nothing cites. Authors is the display form ("Cagan and Jones", "Forsgren et al.").
// Kind and Licence are the stored codes' labels; Status is cleared, needs_check, risk or not_cited and Reason the
// clearance code the UI words. Uses counts live markers and UsedIn names their chapters ("Ch 4, 8", empty when
// there are none). QuotedWords is the source's quoted total across the book and Limit the book's quote limit.
// ClearedLabel says who signed the source off and when ("Cleared by JD · 9 Oct"), null when it never was.
// ReminderLabel is set while a sent permission request waits on a reply ("Reminder due", "Reminder due in 14 days");
// ReminderDue is true once the reminder date has come.
public sealed record SourceRowDto(
    Guid Id,
    int? Number,
    string Title,
    string Authors,
    int? Year,
    string? Publisher,
    string Kind,
    string KindLabel,
    string LicenceLabel,
    string Status,
    string Reason,
    int Uses,
    string UsedIn,
    int QuotedWords,
    int Limit,
    bool Cited,
    string? ClearedLabel,
    string? ReminderLabel,
    bool ReminderDue);
