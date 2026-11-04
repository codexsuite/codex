using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codex.Application.Features.Print.Render;

// The assembler's output: the pandoc JSON AST (`pandoc -f json`), the images it places, the flags, the template,
// trim and option values it was built for, and ContentHash: SHA-256 over all of that, the proof cache's content_hash.
public sealed record AssembledDocument(
    JsonObject Ast,
    IReadOnlyList<PlacedImage> Images,
    IReadOnlyList<RenderFlag> Flags,
    string TemplateSlug,
    string TemplateVersion,
    string Trim,
    IReadOnlyDictionary<string, JsonElement> Options,
    string ContentHash)
{
    // The layout_template_version it was resolved to (set by RenderService); CompileAsync opens that version's package.
    // Empty for a document assembled by hand, which compiles with the built-in package of TemplateSlug.
    public Guid TemplateVersionId { get; init; }

    public IEnumerable<RenderFlag> RawLatex => Flags.Where(f => f.Kind == RenderFlag.RawLatex);
}
