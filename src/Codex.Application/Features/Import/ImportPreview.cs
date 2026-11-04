using System.Text.RegularExpressions;
using Codex.Contracts.Import;

namespace Codex.Application.Features.Import;

// Step two of the pipeline: what a plan will create, count -> destination, for the dialog and book_import.mapping.
// Images are the paths of the images the plan places: the dialog sends exactly those with the import.
public static partial class ImportPreview
{
    public static ImportPreviewDto From(string source, ImportPlan plan)
    {
        var chapters = plan.Acts.SelectMany(a => a.Chapters).ToList();
        var scenes = chapters.SelectMany(c => c.Scenes).ToList();
        var creates = new List<ImportCountDto>
        {
            new("Acts", plan.Acts.Count, "Plan · acts"),
            new("Chapters", chapters.Count, "Plan · chapters"),
            new("Scenes", scenes.Count(s => !s.Archived), "Plan · scene cards"),
        };
        Add(creates, "Archived scenes", scenes.Count(s => s.Archived), "Plan · Archived");
        Add(creates, "Scene beats", scenes.Sum(s => s.Beats.Count), "Write · Scene Beats");
        Add(creates, "Scenes with prose", scenes.Count(s => s.Prose != null), "Write · manuscript");
        Add(creates, "Knowledge base entries", plan.Entries.Count, "Knowledge base");
        Add(creates, "Style guide", plan.Book.StyleGuide == null ? 0 : 1, "Book settings · AI rules");
        Add(creates, "Figures", scenes.Sum(s => s.Figures?.Count ?? 0), "Write · figures");
        Add(creates, "Act openers", plan.Acts.Count(a => a.Opener != null), "Layout · part opener art");
        var images = plan.ImagePaths();
        Add(creates, "Images", images.Count, "Assets");
        if (plan.Layout != null)
        {
            var dropCaps = plan.Layout.Options?.ContainsKey(PandocFolderParser.DropCapsOption) == true ? ", drop caps on" : "";
            creates.Add(new ImportCountDto("Layout template", 1, $"Layout · {plan.Layout.Template}{dropCaps}"));
        }
        Add(creates, "Sources", plan.Backup?.Sources ?? 0, "References");
        Add(creates, "Cited sources", plan.Backup?.CitedSources ?? 0, "References · cited");
        Add(creates, "Labels", plan.Backup?.LabelCount ?? 0, "Plan · labels");
        // A backup carries the database's own word counts; the other sources are counted here.
        var words = plan.Backup?.Words ?? scenes.Where(s => s.Prose != null).Sum(s => Word().Count(s.Prose!));
        return new ImportPreviewDto(
            source, plan.Book.Title, plan.FileCount, words, creates,
            plan.Skipped.Select(s => new ImportSkippedDto(s.Path, s.Reason)).ToList(),
            images);
    }

    private static void Add(List<ImportCountDto> creates, string item, int count, string destination)
    {
        if (count > 0)
        {
            creates.Add(new ImportCountDto(item, count, destination));
        }
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’-]*")]
    private static partial Regex Word();
}
