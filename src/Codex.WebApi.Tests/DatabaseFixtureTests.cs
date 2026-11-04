using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.WebApi.Tests;

[TestFixture]
public class DatabaseFixtureTests
{
    [Test]
    public async Task CloneSeed_WhenTwoClonesCreated_ShouldBeIsolated()
    {
        await using var first = await CodexDatabase.CloneSeedAsync();
        await using var second = await CodexDatabase.CloneSeedAsync();
        first.Name.ShouldNotBe(second.Name);
        var seeded = await CountUsersAsync(second);

        await using (var connection = await first.OpenAsync())
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO app_user (email, display_name) VALUES ('iso@example.com', 'Iso')", connection))
        {
            await insert.ExecuteNonQueryAsync();
        }

        (await CountUsersAsync(first)).ShouldBe(seeded + 1);
        (await CountUsersAsync(second)).ShouldBe(seeded);
    }

    [Test]
    public async Task CloneSchema_WhenCreated_ShouldUseTheProductionLocale()
    {
        await using var clone = await CodexDatabase.CloneSchemaAsync();
        await using var connection = await clone.OpenAsync();

        await using (var show = new NpgsqlCommand("SELECT datctype FROM pg_database WHERE datname = current_database()", connection))
        {
            ((string)(await show.ExecuteScalarAsync())!).ShouldBe(CodexDatabase.Locale);
        }
        // markdown_word_count uses [[:alnum:]], which only counts accented letters under a real locale.
        await using var count = new NpgsqlCommand("SELECT markdown_word_count('Café naïve façade')", connection);
        ((int)(await count.ExecuteScalarAsync())!).ShouldBe(3);
    }

    [Test]
    public async Task CloneSeed_WhenDisposed_ShouldDropTheDatabase()
    {
        var clone = await CodexDatabase.CloneSeedAsync();
        var connection = await clone.OpenAsync();
        await connection.DisposeAsync();

        await clone.DisposeAsync();

        var ex = await Should.ThrowAsync<PostgresException>(async () => await clone.OpenAsync());
        ex.SqlState.ShouldBe(PostgresErrorCodes.InvalidCatalogName);
    }

    private static async Task<long> CountUsersAsync(ClonedDatabase database)
    {
        await using var connection = await database.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM app_user", connection);
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
