using System.Text.Json;

namespace Codex.Contracts.Print;

// POST /api/books/{id}/proofs: a proof of one chapter. Template, Trim and Options override the book's Layout settings
// for this proof (as in the preview). Rerender forces a new compile even when a proof of the same content, template
// version, trim and options is already cached.
public sealed record ProofRequest(
    Guid ChapterId,
    string? Template = null,
    string? Trim = null,
    IReadOnlyDictionary<string, JsonElement>? Options = null,
    bool Rerender = false);
