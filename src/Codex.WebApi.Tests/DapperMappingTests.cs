using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests;

[TestFixture]
public class DapperMappingTests
{
    public sealed record StampedRow(Guid Id, DateTimeOffset At, DateTimeOffset? Missing);

    [Test]
    public async Task Query_WhenTimestamptzMapsToDateTimeOffsetRecord_ShouldReadUtc()
    {
        await using var clone = await CodexDatabase.CloneSchemaAsync();
        await using var connection = await clone.OpenAsync();
        var written = new DateTimeOffset(2026, 10, 19, 13, 30, 0, TimeSpan.Zero);

        var row = await connection.QuerySingleAsync<StampedRow>(
            "SELECT uuidv7() AS id, @written::timestamptz AS at, NULL::timestamptz AS missing", new { written });

        row.At.ShouldBe(written);
        row.Missing.ShouldBeNull();
    }
}
