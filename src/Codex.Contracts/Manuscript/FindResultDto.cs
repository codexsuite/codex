namespace Codex.Contracts.Manuscript;

// The answer to a find: counts over everything searched, and the first matches (at most 200) in reading order.
// InDrafts counts matches in the text of pending AI drafts, which are never replaced.
public sealed record FindResultDto(
    int Matches, int Replaceable, int Cited, int SpansLink, int InDrafts, int Scenes, bool Truncated, IReadOnlyList<FindItemDto> Items);
