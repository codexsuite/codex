namespace Codex.Contracts.Manuscript;

// PUT /api/scenes/{id}/prose: Version is the version the editor loaded. A stale one still saves (last write wins,
// R6): the text it replaces is kept as a revision, and the new version is then more than Version + 1. Every body is
// checked, whichever view typed it: an ::beat, ::kb-addition, ::figure or ref: id that is not already in the stored body
// and not a row of this scene is a 400.
public sealed record SaveProseRequest(string Body, int Version);
