using Codex.AcceptanceTests.Harness;
using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.AcceptanceTests.Lanes.Write;

// Base class of the Write lane's scenarios: the seed clone, plus a way to change the cloned database from a test
// (for setup the UI cannot do yet, such as reordering chapters).
public abstract class WriteLaneTest : SeededAcceptanceTest
{
    private ClonedDatabase _clone = null!;

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        _clone = await base.CloneDatabaseAsync();
        return _clone;
    }

    // Runs one statement against this scenario's database; `parameters` are @name values.
    protected async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _clone.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    // Runs one query against this scenario's database and returns the first column of the first row (null for none).
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
