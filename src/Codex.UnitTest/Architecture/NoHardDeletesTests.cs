using System.Text.RegularExpressions;
using Codex.TestSupport;

namespace Codex.UnitTest.Architecture;

// #85: app code never hard-deletes. "Delete" and "remove" set archived_at (or deleted_at) and keep the row; link rows
// are archived too and a re-add revives them. Only rows that are rebuilt from other rows may be deleted.
[TestFixture]
public class NoHardDeletesTests
{
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["quote_flag"] = "derived: QuoteFlagScanner rebuilds the flags by rescanning the manuscript",
        ["asset_rendition"] = "derived: AssetRenditionWorker re-renders it from the original asset",
    };

    private static readonly string[] TestProjectMarkers = [".Tests", "Codex.UnitTest", "Codex.AcceptanceTests", "Codex.TestSupport"];

    private static readonly Regex Delete = new(@"\bDELETE\s+FROM\s+(?:ONLY\s+)?([A-Za-z_][A-Za-z0-9_.""]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Truncate = new(@"\bTRUNCATE\s+(?:TABLE\s+)?(?:ONLY\s+)?([A-Za-z_][A-Za-z0-9_.""]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Test]
    public void ProductionSql_WhenScanned_ShouldNotHardDeleteAnyEntityOrLinkTable()
    {
        var violations = Scan(ProductionFiles());

        violations.ShouldBeEmpty("Archive instead (archived_at / deleted_at); only the derived tables in Exempt may be deleted from.");
    }

    [Test]
    public void Scan_WhenSqlDeletesOrTruncates_ShouldReportEachTableExceptTheExemptOnes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"no-hard-deletes-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, "a(\"delete  from book\");\nb(\"\"\"\n  DELETE FROM\n  ONLY scene_kb\"\"\");\nc(\"DELETE FROM quote_flag\");\nd(\"TRUNCATE label\");\n");
        try
        {
            Scan([path]).Select(v => v[(v.IndexOf(':') + 1)..]).ShouldBe(["1:book", "3:scene_kb", "6:label"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // "path:line:table", the line being where DELETE (or TRUNCATE) starts.
    private static List<string> Scan(IEnumerable<string> files)
    {
        var found = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var match in Delete.Matches(text).Concat(Truncate.Matches(text)))
            {
                var table = match.Groups[1].Value.Trim('"').ToLowerInvariant();
                if (table.StartsWith("public.", StringComparison.Ordinal))
                {
                    table = table["public.".Length..];
                }

                if (!Exempt.ContainsKey(table))
                {
                    var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                    found.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{line}:{table}");
                }
            }
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> ProductionFiles()
    {
        var src = Path.Combine(RepoPaths.Root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var parts = Path.GetRelativePath(src, f).Split(Path.DirectorySeparatorChar);
                return !parts.Skip(1).Any(p => p is "bin" or "obj" or "node_modules")
                       && !TestProjectMarkers.Any(m => parts[0].EndsWith(m, StringComparison.Ordinal) || parts[0] == m);
            });
    }
}
