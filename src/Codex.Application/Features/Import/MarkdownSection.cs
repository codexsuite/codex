namespace Codex.Application.Features.Import;

public sealed record MarkdownSection(string Heading, string Text)
{
    // The section as Markdown again ("## Heading\n\ntext").
    public string ToMarkdown() => Text.Length == 0 ? $"## {Heading}" : $"## {Heading}\n\n{Text}";
}
