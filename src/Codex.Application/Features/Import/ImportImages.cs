using Codex.Application.Prose;

namespace Codex.Application.Features.Import;

// After the images are stored: a plan without the figures and openers whose image could not be (not uploaded, or
// refused by the asset intake), their ::figure lines taken out of the prose, and each such image listed as skipped
// with why. Pure.
public static class ImportImages
{
    public static ImportPlan Without(ImportPlan plan, IReadOnlyList<ImportSkippedItem> failed)
    {
        if (failed.Count == 0)
        {
            return plan;
        }
        var paths = failed.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var acts = plan.Acts.Select(act => act with
        {
            Opener = act.Opener != null && paths.Contains(act.Opener.ImagePath) ? null : act.Opener,
            Chapters = act.Chapters.Select(chapter => chapter with { Scenes = chapter.Scenes.Select(scene => Scene(scene, paths)).ToList() }).ToList(),
        }).ToList();
        var skipped = plan.Skipped.Concat(failed.Select(f => f with { Reason = f.Reason + " The image was left out." })).ToList();
        return plan with { Acts = acts, Skipped = skipped };
    }

    private static ImportScenePlan Scene(ImportScenePlan scene, HashSet<string> paths)
    {
        var dropped = (scene.Figures ?? []).Where(f => paths.Contains(f.ImagePath)).ToList();
        if (dropped.Count == 0)
        {
            return scene;
        }
        var document = ProseDocument.Parse(scene.Prose ?? "");
        foreach (var figure in dropped)
        {
            document = document.StripFigure(figure.Id);
        }
        var prose = document.Text.Length == 0 ? null : document.Text;
        return scene with { Prose = prose, Figures = scene.Figures!.Except(dropped).ToList(), Status = prose == null ? "outline" : scene.Status };
    }
}
