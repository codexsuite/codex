namespace Codex.Application.Features.Import;

// A Markdown body cut at its "## " headings. Lead is the text before the first one.
public sealed record MarkdownSections(string Lead, IReadOnlyList<MarkdownSection> Sections)
{
    public static MarkdownSections Split(string body)
    {
        var lead = new List<string>();
        var sections = new List<MarkdownSection>();
        string? heading = null;
        var lines = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading != null)
                {
                    sections.Add(new MarkdownSection(heading, Join(lines)));
                }
                heading = line[3..].Trim();
                lines = [];
            }
            else if (heading == null)
            {
                lead.Add(line);
            }
            else
            {
                lines.Add(line);
            }
        }
        if (heading != null)
        {
            sections.Add(new MarkdownSection(heading, Join(lines)));
        }
        return new MarkdownSections(Join(lead), sections);
    }

    private static string Join(List<string> lines) => string.Join('\n', lines).Trim('\n', ' ');
}
