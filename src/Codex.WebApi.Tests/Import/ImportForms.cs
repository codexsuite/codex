using System.Net.Http.Headers;
using System.Text;
using Codex.TestSupport;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Import;

// Multipart bodies as the Import dialog sends them: a source field and one "files" part per file, named by its path
// inside the picked folder (the folder's own name first, as webkitRelativePath gives it).
public static class ImportForms
{
    public static string SmallFolder => Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "markdown-small");

    public static string ExpectedProse(string importRef) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "markdown-small.expected", importRef + ".md"));

    public static IReadOnlyList<(string Path, byte[] Content)> Folder(string folder)
    {
        var name = Path.GetFileName(folder);
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => (name + "/" + Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes(f)))
            .ToList();
    }

    public static (string Path, byte[] Content) Text(string path, string text) => (path, Encoding.UTF8.GetBytes(text));

    public static MultipartFormDataContent Form(
        IEnumerable<(string Path, byte[] Content)> files, string source = "markdown", string[]? notMarkdown = null, string[]? tooLarge = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(source), "source" } };
        foreach (var path in notMarkdown ?? [])
        {
            form.Add(new StringContent(path), "notMarkdown");
        }
        foreach (var path in tooLarge ?? [])
        {
            form.Add(new StringContent(path), "tooLarge");
        }
        foreach (var (path, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
            form.Add(part, "files", path);
        }
        return form;
    }

    // A pandoc book folder as the Import dialog sends it: the .md and .yaml files, the images named in send (streamed from
    // disk, never read whole), and every other path named as left out (notMarkdown), so it is listed as skipped.
    public static MultipartFormDataContent Pandoc(string? folder = null, IEnumerable<string>? send = null, Func<string, byte[]?>? replace = null)
    {
        var images = new HashSet<string>(send ?? [], StringComparer.Ordinal);
        var form = new MultipartFormDataContent { { new StringContent("pandoc"), "source" } };
        foreach (var (path, file) in PlayersGuideCampaign.Files(folder))
        {
            if (PlayersGuideCampaign.IsText(path) || images.Contains(path))
            {
                HttpContent part = replace?.Invoke(path) is { } bytes ? new ByteArrayContent(bytes) : new StreamContent(File.OpenRead(file));
                part.Headers.ContentType = new MediaTypeHeaderValue(PlayersGuideCampaign.IsImage(path) ? "image/png" : "text/markdown");
                form.Add(part, "files", path);
            }
            else
            {
                form.Add(new StringContent(path), "notMarkdown");
            }
        }
        return form;
    }

    // The #10 layout at full size: index, CLAUDE.md, MEMORY.md, 6 acts, 30 chapters, 90 scenes, 25 prose files, and
    // 76 research notes the importer skips: 230 files.
    public static IReadOnlyList<(string Path, byte[] Content)> FullSizeLayout()
    {
        var files = new List<(string, byte[])>
        {
            Text("book/CLAUDE.md", "Write plainly."),
            Text("book/MEMORY.md", "Remember the river."),
            Text("book/manuscript/index.md", "---\ntype: book\ntitle: Full Size\nacts: 6\n---\n"),
        };
        var scene = 0;
        for (var a = 1; a <= 6; a++)
        {
            var act = $"book/manuscript/act-{a:00}-part-{a}";
            files.Add(Text($"{act}/index.md", $"---\ntitle: Part {a}\nreading_order: {a}\n---\n"));
            for (var c = 1; c <= 5; c++)
            {
                var chapterNumber = ((a - 1) * 5) + c;
                var chapter = $"{act}/chapter-{chapterNumber * 2:00}-ch-{chapterNumber}";
                files.Add(Text($"{chapter}/index.md", $"---\ntitle: Chapter {chapterNumber}\n---\n\n## Shape of the chapter\n\nShape {chapterNumber}.\n"));
                for (var s = 1; s <= 3; s++)
                {
                    scene++;
                    files.Add(Text($"{chapter}/scene-{s:00}.md",
                        $"---\nid: ch{chapterNumber:00}-s{s:00}\nstatus: outline\ncodex: [Hero, \"Place {a}\"]\n---\n\n## Purpose\n\nScene {scene}.\n\n## Scene Beats\n\n1. **Start.** Begin.\n2. **End.** Finish.\n"));
                    if (scene <= 25)
                    {
                        files.Add(Text($"{chapter}/scene-{s:00}-prose.md", $"Scene {scene} has exactly six words."));
                    }
                }
            }
        }
        for (var n = 1; n <= 76; n++)
        {
            files.Add(Text($"book/research/note-{n:00}.md", $"Note {n}."));
        }
        return files;
    }
}
