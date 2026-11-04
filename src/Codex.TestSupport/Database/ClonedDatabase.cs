using Npgsql;

namespace Codex.TestSupport.Database;

// A database created for one test class. Disposing drops it, closing any connection still open.
public sealed class ClonedDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;

    internal ClonedDatabase(string name, string connectionString, string adminConnectionString)
    {
        Name = name;
        ConnectionString = connectionString;
        _adminConnectionString = adminConnectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        // Only this clone's pool: other test classes in the process keep theirs.
        await using (var own = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(own);
        }
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
