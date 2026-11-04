namespace Codex.Contracts.Manuscript;

// POST /api/books/{id}/find. `ScopeKind` is book, act, chapter or scene (`ScopeId` names it). Prose, Summaries and
// Beats say which texts are searched; Include archived adds archived scenes (and scenes of archived chapters or acts).
public sealed record FindRequest(
    string Query,
    string ScopeKind = "book",
    Guid? ScopeId = null,
    bool Prose = true,
    bool Summaries = false,
    bool Beats = false,
    bool MatchCase = false,
    bool WholeWord = false,
    bool IncludeArchived = false);
