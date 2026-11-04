namespace Codex.Application.Prose;

// One match of the search text. `Ordinal` counts the text's matches from 0 in reading order (all statuses), so a
// single replacement can name "the third match in this scene". `Before` and `After` are visible text around it.
public sealed record TextMatch(int Ordinal, MatchStatus Status, string Before, string Text, string After);
