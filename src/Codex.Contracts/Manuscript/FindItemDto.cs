namespace Codex.Contracts.Manuscript;

// One match. `Source` is prose, summary or beat; `Ordinal` counts the scene's matches in that source from 0 (a beat
// source counts across the scene's beats). `Status` is replaceable, cited (inside a `ref:` passage or a chapter link,
// never changed) or spans_link (runs across a link, skipped). `Before` / `After` are visible text around the match.
public sealed record FindItemDto(Guid SceneId, string Source, int Ordinal, string Status, string Before, string Text, string After);
