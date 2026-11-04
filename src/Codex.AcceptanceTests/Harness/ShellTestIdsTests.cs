using System.Reflection;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Harness;

[TestFixture]
public partial class ShellTestIdsTests
{
    [GeneratedRegex(@"^\s*(\w+): '([^']*)',$", RegexOptions.Multiline)]
    private static partial Regex ConstantPattern();

    [Test]
    public void ShellTestIds_InTsAndCs_ShouldMatch()
    {
        var ts = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.Client", "src", "shell", "shellTestIds.ts"));
        var tsIds = ConstantPattern().Matches(ts).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        var csIds = typeof(ShellTestIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(f => char.ToLowerInvariant(f.Name[0]) + f.Name[1..], f => (string)f.GetRawConstantValue()!);

        tsIds.ShouldBe(csIds, ignoreOrder: true);
    }
}
