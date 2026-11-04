using System.Reflection;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Pages.References;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Lanes.References;

[TestFixture]
public partial class ReferencesTestIdsTests
{
    [GeneratedRegex(@"^\s*(\w+): '([^']*)',$", RegexOptions.Multiline)]
    private static partial Regex ConstantPattern();

    [Test]
    public void ReferencesTestIds_InTsAndCs_ShouldMatchForTheConstants()
    {
        var ts = File.ReadAllText(Path.Combine(
            RepoPaths.Root, "src", "Codex.Client", "src", "features", "references", "testIds.ts"));
        var tsIds = ConstantPattern().Matches(ts).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        var csIds = typeof(ReferencesTestIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(f => char.ToLowerInvariant(f.Name[0]) + f.Name[1..], f => (string)f.GetRawConstantValue()!);

        tsIds.ShouldBe(csIds, ignoreOrder: true);
    }
}
