using System.Reflection;
using System.Text.RegularExpressions;
using Codex.TestSupport;

namespace Codex.WebApi.Tests;

[TestFixture]
public partial class TestIdRegistryTests
{
    [GeneratedRegex(@"^\s*(\w+): '([^']*)',$", RegexOptions.Multiline)]
    private static partial Regex ConstantPattern();

    [GeneratedRegex(@"^\s*(\w+): \((\w+): string\) => `([^`]*)`,$", RegexOptions.Multiline)]
    private static partial Regex BuilderPattern();

    [Test]
    public void ShellIds_InTsAndCs_ShouldMatch()
    {
        var ts = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.Client", "src", "testIds.ts"));
        var tsConstants = ConstantPattern().Matches(ts).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var tsBuilders = BuilderPattern().Matches(ts).ToDictionary(m => m.Groups[1].Value, m => m.Groups[3].Value);

        var cs = typeof(TestIds.Shell);
        var csConstants = cs.GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(f => Camel(f.Name), f => (string)f.GetRawConstantValue()!);
        // A builder is compared by calling it with a marker and turning the marker back into ${arg}.
        var csBuilders = cs.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.DeclaringType == cs)
            .ToDictionary(m => Camel(m.Name), m => ((string)m.Invoke(null, ["@@"])!).Replace("@@", "${ARG}"));

        tsConstants.ShouldBe(csConstants, ignoreOrder: true);
        tsBuilders.Select(b => (b.Key, Template: Regex.Replace(b.Value, @"\$\{\w+\}", "${ARG}")))
            .ShouldBe(csBuilders.Select(b => (b.Key, Template: b.Value)), ignoreOrder: true);
        tsConstants.Count.ShouldBeGreaterThan(20);
        tsBuilders.Count.ShouldBe(5);
    }

    [Test]
    public void ShellIds_WhenListed_ShouldBeUniqueAndPrefixed()
    {
        var values = typeof(TestIds.Shell).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!).ToList();

        values.Distinct().Count().ShouldBe(values.Count);
        values.ShouldAllBe(v => v == "app-shell" || v.StartsWith("shell-", StringComparison.Ordinal));
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
