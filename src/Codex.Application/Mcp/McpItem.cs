using Codex.Contracts.Books;

namespace Codex.Application.Mcp;

// The thing a result is about (#20 section 2.2): a stable id, its kind (book, series, act, chapter, scene, kb_entry,
// draft, source), the label the author knows it by ("Ch 5 · Sc 2 · Outcome metrics that don't rot") and the deep link
// the agent can hand to the author, built by CodexLinks (the same URLs as the UI's link builders, D8). Number is the
// display number for an act, chapter or scene ("act:2", "5", "5.2"), resolved when the call ran. Owner: who owns a book
// or series offered as a candidate (#80), so the agent can tell the caller's own book from one shared with them.
public sealed record McpItem(Guid Id, string Kind, string Label, string OpenUrl, string? Number = null, BookOwnerDto? Owner = null);
