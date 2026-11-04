using System.Text.Json;

namespace Codex.Contracts.Print;

// One template option for the Layout panel: Type is "boolean" or "choice" (one of Choices); Value is what the book
// has now (its stored value, or Default).
public sealed record LayoutOptionDto(string Key, string Label, string Type, IReadOnlyList<string> Choices, JsonElement Default, JsonElement Value);
