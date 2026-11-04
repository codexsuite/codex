namespace Codex.Application.Mcp;

// What a step's target must look like right now for undo to be safe: the state the write left. McpUndoChecks compares
// it with the database before any step runs, so an author's edit made since (in Plan or Write) makes undo a conflict
// instead of overwriting it. A null member is not checked. Title, Summary and Status are a scene's or an entry's (Title is
// the entry's name); Labels is a scene's labels or a beat's context; Place compares Parent and After (the sibling it
// follows, null = first); Body, Flag and Texts (aliases) are a beat's or an entry's. A source's details and Codex link are
// Source (SourceTools.State); a use's type is Kind; a permission draft is Status, Body (the letter), To, with Parent = its source. A figure is Parent (its image), Status
// (placement), Summary (caption, "" = none) and Body (alt text, "" = none); an opener is Body (its image id, "" = none) and Summary.
public sealed record McpUndoState(
    string? Title = null,
    string? Summary = null,
    string? Status = null,
    IReadOnlyList<Guid>? Labels = null,
    bool Place = false,
    Guid? Parent = null,
    Guid? After = null,
    string? Body = null,
    bool? Flag = null,
    IReadOnlyList<string>? Texts = null,
    string? Kind = null,
    string? Source = null,
    string? To = null,
    IReadOnlyList<string>? Tags = null,
    bool? Exclude = null);
