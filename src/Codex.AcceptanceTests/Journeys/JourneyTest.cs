using Codex.AcceptanceTests.Harness;
using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.AcceptanceTests.Journeys;

// Base class of every journey: a fresh clone of the seed (the #16 review's "reset to seed before each journey"),
// plus a way to check rows the UI does not show.
public abstract class JourneyTest : SeededAcceptanceTest
{
    private ClonedDatabase _clone = null!;

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        _clone = await base.CloneDatabaseAsync();
        return _clone;
    }

    // Runs one query against this journey's database and returns the first column of the first row (null for none).
    protected async Task<T?> ScalarSqlAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _clone.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }
}
