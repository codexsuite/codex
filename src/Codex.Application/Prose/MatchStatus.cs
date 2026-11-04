namespace Codex.Application.Prose;

public enum MatchStatus
{
    // Plain text, or the label of a mention: Replace all changes it.
    Replaceable,

    // Inside a `ref:` passage or a `chapter:` link: the marker's words are the quoted passage, never changed.
    Cited,

    // Starts in one run of text and ends in another, with a link between: replacing would break the link.
    SpansLink,
}
