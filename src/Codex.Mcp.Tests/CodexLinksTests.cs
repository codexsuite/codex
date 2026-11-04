using System.Text.Json;
using Codex.Application.Mcp;
using Codex.TestSupport;

namespace Codex.Mcp.Tests;

// open_url must be the URL the UI's own link builders make (D8). Both sides read the same cases:
// Fixtures/links/links.json here and in src/Codex.Client/src/shell/links.test.ts (over links.ts).
[TestFixture]
public class CodexLinksTests
{
    public sealed record LinkCase(string Builder, Guid Id, Dictionary<string, string> Args, string Url)
    {
        public override string ToString() => $"{Builder} {string.Join(',', Args.Keys)}";
    }

    private sealed record LinkCases(IReadOnlyList<LinkCase> Cases);

    public static IEnumerable<LinkCase> AllCases() =>
        JsonSerializer.Deserialize<LinkCases>(
            File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "links", "links.json")),
            JsonSerializerOptions.Web)!.Cases;

    [TestCaseSource(nameof(AllCases))]
    public void Links_WhenBuilt_ShouldMatchTheSharedFixture(LinkCase link)
    {
        Build(link).ShouldBe(link.Url);
    }

    [Test]
    public void Cases_WhenLoaded_ShouldCoverEveryBuilder()
    {
        AllCases().Select(c => c.Builder).Distinct().ShouldBe(["plan", "write", "references", "settings", "series", "layout"], ignoreOrder: true);
    }

    private static string Build(LinkCase link)
    {
        string? Arg(string key) => link.Args.GetValueOrDefault(key);
        Guid? Id(string key) => Arg(key) is { } value ? Guid.Parse(value) : null;
        return link.Builder switch
        {
            "plan" => CodexLinks.Plan(link.Id, Arg("scene"), Arg("chapter"), Id("kb"), Id("entry")),
            "write" => CodexLinks.Write(link.Id, Arg("scene"), Arg("chapter"), Id("kb"), Id("entry"), Id("ref")),
            "references" => CodexLinks.References(link.Id, Id("source")),
            "settings" => CodexLinks.Settings(link.Id, Arg("section")),
            "series" => CodexLinks.Series(link.Id),
            "layout" => CodexLinks.Layout(link.Id, Arg("scene"), Arg("chapter")),
            _ => throw new ArgumentOutOfRangeException(nameof(link), link.Builder, "Unknown builder in links.json."),
        };
    }
}
