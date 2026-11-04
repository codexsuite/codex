using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.WebApi.Tests;

[TestFixture]
public class SeedIdsTests
{
    private sealed record IdRow(string Kind, string Name, Guid Uuid);

    [Test]
    public void GeneratedCs_ShouldMatchCsv()
    {
        var fromCsv = ReadCsv().ToDictionary(r => $"{r.Kind}.{r.Name}", r => r.Uuid);
        var fromCode = ReadGenerated();

        fromCode.Keys.Order().ShouldBe(fromCsv.Keys.Order());
        foreach (var (key, uuid) in fromCsv)
        {
            fromCode[key].ShouldBe(uuid, key);
        }
    }

    [Test]
    public void Csv_ShouldHaveUniqueUuidsAndNames()
    {
        var rows = ReadCsv();

        rows.GroupBy(r => r.Uuid).Where(g => g.Count() > 1).ShouldBeEmpty();
        rows.GroupBy(r => (r.Kind, r.Name)).Where(g => g.Count() > 1).ShouldBeEmpty();
        rows.ShouldAllBe(r => r.Uuid.Version == 7);
    }

    [Test]
    public void CurrentUserId_ShouldBeTheSeededJd()
    {
        SeedIds.Users.Jd.ShouldBe(ReadCsv().Single(r => r.Kind == "users" && r.Name == "Jd").Uuid);
    }

    private static List<IdRow> ReadCsv()
    {
        var path = Path.Combine(RepoPaths.Root, "db", "seed", "ids.csv");
        return File.ReadAllLines(path)
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(',', 4))
            .Select(f => new IdRow(f[0], f[1], Guid.Parse(f[2])))
            .ToList();
    }

    // Keyed "<kind>.<name>" with the kind in lowerCamel, as in the CSV.
    private static Dictionary<string, Guid> ReadGenerated()
    {
        var result = new Dictionary<string, Guid>();
        foreach (var group in typeof(SeedIds).GetNestedTypes())
        {
            var kind = char.ToLowerInvariant(group.Name[0]) + group.Name[1..];
            foreach (var field in group.GetFields())
            {
                result[$"{kind}.{field.Name}"] = (Guid)field.GetValue(null)!;
            }
        }
        return result;
    }
}
