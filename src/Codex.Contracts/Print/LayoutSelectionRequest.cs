using System.Text.Json;

namespace Codex.Contracts.Print;

// PUT /api/books/{id}/layout: the book's template, with its trim and the options to change (the others keep their
// stored value or default).
public sealed record LayoutSelectionRequest(string Template, string? Trim = null, IReadOnlyDictionary<string, JsonElement>? Options = null);
