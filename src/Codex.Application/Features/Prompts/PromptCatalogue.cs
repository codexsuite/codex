using System.Reflection;
using System.Text.Json;
using Codex.Contracts.Prompts;

namespace Codex.Application.Features.Prompts;

// Reads prompts/models.json and prompts/templates/*.md from the manifest resources embedded in this assembly.
// A template file is "name: …", "description: …", a line holding only "---", then the instruction text.
public sealed class PromptCatalogue : IPromptCatalogue
{
    private const string ModelsResource = "prompts/models.json";
    private const string TemplatePrefix = "prompts/templates/";
    private const string TemplateSuffix = ".md";

    private readonly PromptsDto _prompts;
    private readonly Dictionary<string, string> _instructions = new(StringComparer.Ordinal);

    public PromptCatalogue()
    {
        var assembly = ApplicationMarker.Assembly;
        var templates = new List<PromptTemplateDto>();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(TemplatePrefix, StringComparison.Ordinal) && n.EndsWith(TemplateSuffix, StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            var id = resource[TemplatePrefix.Length..^TemplateSuffix.Length];
            var (name, description, instruction) = ParseTemplate(id, ReadText(assembly, resource));
            templates.Add(new PromptTemplateDto(id, name, description, BuiltIn: true));
            _instructions[id] = instruction;
        }
        var models = JsonSerializer.Deserialize<List<PromptModelDto>>(ReadText(assembly, ModelsResource), JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException($"{ModelsResource} is empty.");
        if (models.Count(m => m.Default) != 1)
        {
            throw new InvalidOperationException($"{ModelsResource} must mark exactly one default model.");
        }
        _prompts = new PromptsDto(templates, models);
    }

    public PromptsDto Get() => _prompts;

    public string? FindInstruction(string templateId) => _instructions.GetValueOrDefault(templateId);

    private static string ReadText(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"Embedded resource {resource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (string Name, string Description, string Instruction) ParseTemplate(string id, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var separator = Array.IndexOf(lines, "---");
        if (separator < 0)
        {
            throw new InvalidOperationException($"Prompt template {id} has no '---' line after its header.");
        }
        string Header(string key) => lines.Take(separator)
            .Where(l => l.StartsWith(key + ":", StringComparison.Ordinal))
            .Select(l => l[(key.Length + 1)..].Trim())
            .FirstOrDefault(v => v.Length > 0)
            ?? throw new InvalidOperationException($"Prompt template {id} has no '{key}:' header.");
        return (Header("name"), Header("description"), string.Join('\n', lines.Skip(separator + 1)).Trim());
    }
}
