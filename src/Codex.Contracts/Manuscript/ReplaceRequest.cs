namespace Codex.Contracts.Manuscript;

// POST /api/books/{id}/replace: the find's options plus the replacement. With `Only` just that match is replaced.
public sealed record ReplaceRequest(
    string Query,
    string Replacement,
    string ScopeKind = "book",
    Guid? ScopeId = null,
    bool Prose = true,
    bool Summaries = false,
    bool Beats = false,
    bool MatchCase = false,
    bool WholeWord = false,
    bool IncludeArchived = false,
    ReplaceTarget? Only = null);
