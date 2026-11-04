using System.Globalization;
using Codex.Application.Database;
using Npgsql;

namespace Codex.TestSupport.Database;

// Template databases and per-test-class clones.
//
//   codex_schema_<hash>   db/schema + db/migrations applied
//   codex_seed_<hash>     the schema template plus db/seed
//   codex_seed_large_<hash>  the seed template plus db/seed-large (Iron Litany at ~125,000 words; performance tests)
//   codex_t_<stamp>_<id>  one test class's private copy (CREATE DATABASE ... TEMPLATE ...)
//
// <hash> is a hash of the db/ file contents in apply order, so a changed script means a new template,
// built once. Many test classes, processes and agents share one server, so every step that touches a
// template runs under one Postgres advisory lock (a database cannot be copied while anyone is
// connected to it, and two builders must not race to build the same template).
public static class CodexDatabase
{
    private const long LockKey = 0x436F646578;
    private const string StampFormat = "yyMMddHHmm";
    public const string Locale = "en_US.UTF-8";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);
    private static readonly TimeSpan OldTemplateAfter = TimeSpan.FromHours(24);
    private static int _cleaned;

    public static Task<ClonedDatabase> CloneSchemaAsync() => CloneAsync(includeSeed: false, includeLarge: false);

    public static Task<ClonedDatabase> CloneSeedAsync() => CloneAsync(includeSeed: true, includeLarge: false);

    // The seed plus db/seed-large, which is never embedded in the app: only this template runs it.
    public static Task<ClonedDatabase> CloneSeedLargeAsync() => CloneAsync(includeSeed: true, includeLarge: true);

    private static async Task<ClonedDatabase> CloneAsync(bool includeSeed, bool includeLarge)
    {
        DapperTypeHandlers.Register();
        var admin = await PostgresServer.AdminConnectionStringAsync();
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "SELECT pg_advisory_lock(@k)", ("k", LockKey));
        try
        {
            if (Interlocked.Exchange(ref _cleaned, 1) == 0)
            {
                await CleanUpAsync(connection);
            }
            var template = await EnsureTemplateAsync(connection, admin, includeSeed, includeLarge);
            await ExecuteAsync(connection, $"COMMENT ON DATABASE \"{template}\" IS '{Stamp()}'");
            var name = $"codex_t_{Stamp()}_{Guid.NewGuid():N}"[..28];
            await ExecuteAsync(connection, $"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"");
            var cloneConnection = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = true }.ConnectionString;
            return new ClonedDatabase(name, cloneConnection, admin);
        }
        finally
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_unlock(@k)", ("k", LockKey));
        }
    }

    private static async Task<string> EnsureTemplateAsync(NpgsqlConnection connection, string admin, bool includeSeed, bool includeLarge)
    {
        var schemaTemplate = $"codex_schema_{DatabaseScripts.Hash(includeSeed: false)}";
        if (!await ExistsAsync(connection, schemaTemplate))
        {
            await BuildAsync(connection, admin, schemaTemplate, source: null, includeSeed: false);
        }
        if (!includeSeed)
        {
            return schemaTemplate;
        }
        var seedTemplate = $"codex_seed_{DatabaseScripts.Hash(includeSeed: true)}";
        if (!await ExistsAsync(connection, seedTemplate))
        {
            await BuildAsync(connection, admin, seedTemplate, source: schemaTemplate, includeSeed: true);
        }
        if (!includeLarge)
        {
            return seedTemplate;
        }
        var largeTemplate = LargeTemplateName();
        if (!await ExistsAsync(connection, largeTemplate))
        {
            await BuildLargeAsync(connection, admin, largeTemplate, seedTemplate);
        }
        return largeTemplate;
    }

    // Built under a scratch name and renamed, so a crashed build never leaves a half-made template behind.
    private static async Task BuildAsync(NpgsqlConnection connection, string admin, string final, string? source, bool includeSeed)
    {
        var scratch = $"codex_b_{Stamp()}_{Guid.NewGuid():N}"[..28];
        // The schema template pins the locale of the postgres:18 image the app runs on in production and CI
        // (text comparison, ORDER BY title and [[:alnum:]] in markdown_word_count all depend on it), so a
        // server with another default still tests what ships. Clones and the seed template inherit it.
        var from = source == null
            ? $" TEMPLATE template0 ENCODING 'UTF8' LOCALE_PROVIDER libc LOCALE '{Locale}'"
            : $" TEMPLATE \"{source}\"";
        await ExecuteAsync(connection, $"CREATE DATABASE \"{scratch}\"{from}");
        var scratchConnection = new NpgsqlConnectionStringBuilder(admin) { Database = scratch, Pooling = false }.ConnectionString;
        DatabaseMigrator.Apply(scratchConnection, includeSeed);
        if (includeSeed)
        {
            // A real database has autovacuum statistics; a freshly loaded one has none and the planner guesses
            // (re-running the kb_entry_use view per row). Clones copy the statistics with the template.
            await using var scratchDb = new NpgsqlConnection(scratchConnection);
            await scratchDb.OpenAsync();
            await ExecuteAsync(scratchDb, "ANALYZE");
        }
        await ExecuteAsync(connection, $"ALTER DATABASE \"{scratch}\" RENAME TO \"{final}\"");
    }

    // The seed template plus db/seed-large/*.sql in file-name order, built under a scratch name like the others.
    private static async Task BuildLargeAsync(NpgsqlConnection connection, string admin, string final, string source)
    {
        var scratch = $"codex_b_{Stamp()}_{Guid.NewGuid():N}"[..28];
        await ExecuteAsync(connection, $"CREATE DATABASE \"{scratch}\" TEMPLATE \"{source}\"");
        var scratchConnection = new NpgsqlConnectionStringBuilder(admin) { Database = scratch, Pooling = false }.ConnectionString;
        await using (var scratchDb = new NpgsqlConnection(scratchConnection))
        {
            await scratchDb.OpenAsync();
            foreach (var script in LargeScripts())
            {
                await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(script), scratchDb) { CommandTimeout = 300 };
                await command.ExecuteNonQueryAsync();
            }
            await ExecuteAsync(scratchDb, "ANALYZE");
        }
        await ExecuteAsync(connection, $"ALTER DATABASE \"{scratch}\" RENAME TO \"{final}\"");
    }

    private static string[] LargeScripts()
        => Directory.GetFiles(Path.Combine(RepoPaths.Root, "db", "seed-large"), "*.sql").Order(StringComparer.Ordinal).ToArray();

    // Hash of the seed template's scripts plus the large scripts' names and bytes.
    private static string LargeTemplateName()
    {
        var text = new System.Text.StringBuilder(DatabaseScripts.Hash(includeSeed: true)).Append('\0');
        foreach (var script in LargeScripts())
        {
            text.Append(Path.GetFileName(script)).Append('\0').Append(File.ReadAllText(script)).Append('\0');
        }
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())))[..12];
        return $"codex_seed_large_{hash}";
    }

    // Stale per-class databases and abandoned builds, and templates for db/ contents nobody has used for a day.
    private static async Task CleanUpAsync(NpgsqlConnection connection)
    {
        var current = new[]
        {
            $"codex_schema_{DatabaseScripts.Hash(includeSeed: false)}",
            $"codex_seed_{DatabaseScripts.Hash(includeSeed: true)}",
            LargeTemplateName(),
        };
        var now = TimeProvider.System.GetUtcNow();
        var rows = new List<(string Name, string? Comment)>();
        await using (var list = new NpgsqlCommand(
            @"SELECT datname, shobj_description(oid, 'pg_database') FROM pg_database
              WHERE datname ~ '^codex_(t|b|schema|seed)_'", connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }
        foreach (var (name, comment) in rows)
        {
            var isScratch = name.StartsWith("codex_t_", StringComparison.Ordinal) || name.StartsWith("codex_b_", StringComparison.Ordinal);
            var stamp = isScratch ? name.Split('_')[2] : comment;
            var age = TryParseStamp(stamp, out var when) ? now - when : TimeSpan.MaxValue;
            var drop = isScratch ? age > StaleAfter : !current.Contains(name) && age > OldTemplateAfter;
            if (drop)
            {
                await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            }
        }
    }

    private static string Stamp() => TimeProvider.System.GetUtcNow().ToString(StampFormat, CultureInfo.InvariantCulture);

    private static bool TryParseStamp(string? stamp, out DateTimeOffset when)
        => DateTimeOffset.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out when);

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string name)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @n)", connection);
        command.Parameters.AddWithValue("n", name);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }
}
