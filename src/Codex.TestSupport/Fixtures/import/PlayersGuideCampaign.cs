namespace Codex.TestSupport.Fixtures;

// players-guide-campaign/: a made-up campaign folder shaped like a real one (its README says how), for the pandoc
// import source. Paths are as the Import dialog sends them: the folder's own name first.
public static class PlayersGuideCampaign
{
    public const string Name = "players-guide-campaign";

    public static string Folder => Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", Name);

    public static string Expected => Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", Name + ".expected");

    // The images the three chapters place (part openers first, then figures), as upload paths.
    public static readonly IReadOnlyList<string> PlacedImages =
    [
        Name + "/images/Scenes/Harrowmere/TOC.png",
        Name + "/images/Scenes/OldVelmara/Background_The Age_of_Harvest.png",
        Name + "/images/Cities/Harbourfall/BackGround The Age of the Guild.png",
        Name + "/images/Scenes/Thornwick/Background The Age of Elemental Renewal 1.png",
        Name + "/images/Scenes/Ashgrove/Chapter 01 - end.png",
        Name + "/images/People/Fenhold/Chapter 03 Ranger 2.png",
        Name + "/images/People/Brightmoor/Chapter 03 FIghter 3.png",
        Name + "/images/Scenes/Harrowmere/Chapter 04 Elemental Monks and Their Customs 3.png",
    ];

    public const string UnusedImage = Name + "/images/Scenes/Harbourfall/Chapter 02 Humans.png";

    // Every file of a folder as (upload path, absolute path), the folder's name first. Git's own files and names a
    // browser never lists (Dropbox's "file:com.dropbox.attrs" streams on a Windows drive) are left out.
    public static IReadOnlyList<(string Path, string File)> Files(string? folder = null)
    {
        var root = folder ?? Folder;
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !Path.GetRelativePath(root, f).Contains(':'))
            .Order(StringComparer.Ordinal)
            .Select(f => (name + "/" + Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'), f))
            .ToList();
    }

    public static bool IsImage(string path) =>
        new[] { ".png", ".jpg", ".jpeg", ".webp" }.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsText(string path) =>
        new[] { ".md", ".yaml", ".yml" }.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
