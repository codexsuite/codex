namespace Codex.Contracts.Structure;

// PATCH /api/chapters/{id}: a field left out (null) is unchanged; an empty Summary or Notes clears it. The title
// cannot be empty, and the chapter's slug never changes. IsNumbered false takes the chapter out of the numbering
// (Preface, front matter), so the chapters after it renumber. An archived chapter is not_found.
public sealed record PatchChapterRequest(string? Title, string? Summary = null, string? Notes = null, bool? IsNumbered = null);
