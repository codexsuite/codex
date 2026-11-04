using System.Text.Json;

namespace Codex.Application.Features.Print.Templates;

// One entry of the manifest's options schema: Type "boolean" or "choice" (one of Choices), its Default, and the
// Label the options UI shows. Values reach the pandoc template and filters as metadata under the option's name.
public sealed class TemplateOption
{
    public string Type { get; set; } = "boolean";

    public string? Label { get; set; }

    public IReadOnlyList<string>? Choices { get; init; }

    public JsonElement Default { get; set; }
}
