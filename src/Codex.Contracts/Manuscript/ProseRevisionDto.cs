namespace Codex.Contracts.Manuscript;

// One row of a scene's version history. `Label` names how the text came to be ("Edited", "AI draft applied",
// "Imported", "Restored", "Edited by <client>", "Replaced by another session"); `Detail` is the model and attempt of an
// applied draft. `TimeLabel` is "12m ago"-style and `ClockLabel` the time of day ("14:02") for the confirm line.
// `Current` marks the one row whose text is what the scene holds now.
public sealed record ProseRevisionDto(
    Guid Id,
    string Source,
    string Label,
    string? Detail,
    DateTimeOffset CreatedAt,
    string TimeLabel,
    string ClockLabel,
    int Words,
    string Body,
    bool Current);
