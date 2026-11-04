namespace Codex.Contracts.Settings;

// One step of the copy-down and join, worded for the Review move dialog. Key is stable: copyCategories, copyEntries,
// copyLabels, repoint, keepSettings, join, renumber.
public sealed record SeriesMoveStepDto(string Key, string Text);
