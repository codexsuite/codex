namespace Codex.Contracts.References;

// Sources: Total is every live source; Badge is the References tab count: the copyright risks when there are any (shown in danger), otherwise the
// sources to check (shown in warn). RemindersDue counts sources whose sent permission request is past its reminder date.
public sealed record ClearanceSummaryDto(int Cleared, int Total, int Risks, int NeedsCheck, int NotCited, int Badge, int RemindersDue);
