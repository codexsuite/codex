using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Import;

// The Codex half of a Novelcrafter export. An entry is a Markdown file in a folder named for its kind (characters/,
// locations/, other/ …) with front matter:
//   id: 0ujsswThIGTUYm2K8FjOOfXtY1K   (the KSUID: kb_entry.external_id, and how scenes' codex[] may name the entry)
//   name: Mara Venn                   (or title:, or a leading "# Name" line, or the file name)
//   aliases: [Mara, The Courier]
//   summary: One line for the sidebar (or description:)
// and the Markdown body below. The folder name is the category.
internal static partial class NovelcrafterCodex
{
    private static readonly string[] GenericStems = ["entry", "post", "index", "readme"];

    private static readonly string[] Folders =
        ["characters", "locations", "places", "items", "lore", "organizations", "factions", "subplots", "other"];

    // A Codex file sits directly in one of the kind folders, outside the manuscript's act folders.
    public static bool IsCodexPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 2
            && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && Folders.Contains(parts[^2], StringComparer.OrdinalIgnoreCase)
            && !parts.Any(p => ActFolder().IsMatch(p));
    }

    public static bool LooksLikeKsuid(string key) => Ksuid().IsMatch(key);

    public static IReadOnlyList<NovelcrafterCodexFile> Read(IReadOnlyList<ImportFile> files, List<ImportSkippedItem> skipped)
    {
        var entries = new List<NovelcrafterCodexFile>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            if (ReadFile(file, skipped) is not { } entry)
            {
                continue;
            }
            if (entry.Id != null && !ids.Add(entry.Id))
            {
                skipped.Add(new ImportSkippedItem(file.Path, $"Codex id \"{entry.Id}\" is used twice; kept on the first entry only"));
                entry = entry with { Id = null };
            }
            entries.Add(entry);
        }
        return entries;
    }

    // One entry file, wherever it sits. Null (and listed as skipped) when it is not text or has no name. The category is
    // the kind folder; CategoryFromTags is the other way to name it.
    public static NovelcrafterCodexFile? ReadFile(ImportFile file, List<ImportSkippedItem> skipped)
    {
        if (Decode(file.Content) is not { } text)
        {
            skipped.Add(new ImportSkippedItem(file.Path, "Not UTF-8 text"));
            return null;
        }
        var (front, body) = FrontMatter.Split(text);
        var (heading, rest) = TakeHeading(body);
        var parts = file.Path.Split('/');
        var stem = parts[^1].EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? parts[^1][..^".md".Length] : parts[^1];
        // other/goodharts-law/entry.md and blog/post-x/post.md are named by their folder.
        var named = GenericStems.Contains(stem, StringComparer.OrdinalIgnoreCase) && parts.Length >= 2 ? parts[^2] : stem;
        var name = front.Scalar("name") ?? front.Scalar("title") ?? heading ?? MarkdownFolderParser.Humanize(named);
        if (string.IsNullOrWhiteSpace(name))
        {
            skipped.Add(new ImportSkippedItem(file.Path, "Codex entry without a name"));
            return null;
        }
        var tags = front.List("tags").Concat(front.Scalar("tags") is { } one ? [one] : []).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        return new NovelcrafterCodexFile(
            file.Path,
            front.Scalar("id"),
            MarkdownFolderParser.Humanize(parts.Length >= 2 ? parts[^2] : "") ?? "Other",
            MarkdownFolderParser.Clip(name),
            front.List("aliases").Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            front.Scalar("summary") ?? front.Scalar("description"),
            BodyOf(rest),
            tags,
            front.Scalar("type"),
            front.Flag("alwaysIncludeInContext") || front.Flag("always_include_in_context") || front.Flag("always_in_context"));
    }

    // The category a tagged entry belongs in: its first tag, else its type, else (a file in a kind folder) the folder.
    // Null when nothing maps. The first letter is capitalised ("story" -> "Story"; "AI" and "Laws" stay as written).
    public static string? CategoryFromTags(NovelcrafterCodexFile file)
    {
        var label = file.Tags.FirstOrDefault() ?? file.Type;
        if (label == null && IsCodexPath(file.Path))
        {
            return file.Category;
        }
        return label == null ? null : MarkdownFolderParser.Clip(char.ToUpperInvariant(label[0]) + label[1..]);
    }

    private static string? BodyOf(string markdown)
    {
        var stored = ImportProse.ToStored(markdown);
        return stored.Length == 0 ? null : stored;
    }

    private static (string? Heading, string Remainder) TakeHeading(string body)
    {
        var trimmed = body.TrimStart('\n', ' ');
        if (!trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            return (null, body.Trim('\n', ' '));
        }
        var end = trimmed.IndexOf('\n');
        return end < 0 ? (trimmed[2..].Trim(), "") : (trimmed[2..end].Trim(), trimmed[(end + 1)..].Trim('\n', ' '));
    }

    private static string? Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content).TrimStart('﻿').Replace("\0", "").Replace("\r\n", "\n").Replace('\r', '\n');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^act-\d+(?:-.*)?$")]
    private static partial Regex ActFolder();

    [GeneratedRegex(@"^[0-9A-Za-z]{27}$")]
    private static partial Regex Ksuid();
}
