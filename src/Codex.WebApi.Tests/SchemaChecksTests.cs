using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.WebApi.Tests;

[TestFixture]
public class SchemaChecksTests
{
    [Test]
    public async Task Rev6CheckScript_OnSchemaClone_ShouldPassAll60Checks()
    {
        await using var database = await CodexDatabase.CloneSchemaAsync();
        await using var connection = await database.OpenAsync();
        await using var script = new NpgsqlCommand(ReadScript(), connection);

        // The script ends with two queries: one row per check (n, PASS/FAIL, name, detail), then "<passed>/<total>".
        // Every other statement also yields a result set, so keep the last two.
        List<string> checks = [];
        var total = string.Empty;
        await using (var reader = await script.ExecuteReaderAsync())
        {
            do
            {
                if (reader.FieldCount == 4 && reader.GetName(1) == "r")
                {
                    checks = await ReadRowsAsync(reader);
                }
                else if (reader.FieldCount == 1 && reader.GetName(0) == "total")
                {
                    total = (await ReadRowsAsync(reader)).Single();
                }
            }
            while (await reader.NextResultAsync());
        }

        checks.Count.ShouldBe(60);
        checks.Where(c => !c.Contains(" PASS ")).ShouldBeEmpty();
        total.ShouldBe("60/60");
    }

    private static async Task<List<string>> ReadRowsAsync(NpgsqlDataReader reader)
    {
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            if (reader.FieldCount == 1)
            {
                rows.Add(reader.GetString(0));
                continue;
            }
            var detail = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            rows.Add($"{reader.GetInt32(0)} {reader.GetString(1)} {reader.GetString(2)} {detail}");
        }
        return rows;
    }

    private static string ReadScript()
    {
        var assembly = typeof(SchemaChecksTests).Assembly;
        using var stream = assembly.GetManifestResourceStream("Codex.WebApi.Tests.Resources.checks_rev6.sql")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
