using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// Book refs search owned and shared books together (#80): Ana owns a book titled "Product Engineering", and JD's
// Product Engineering is shared with her to view. The title names both, so a call fails ambiguous and each candidate
// names its owner; a book of JD's that is not shared with her (Product Engineering (v1)) is not among them.
[TestFixture]
[CancelAfter(60000)]
public class McpSharedTitleTests
{
    private static readonly Guid AnasBook = new("0199c0de-0000-7000-8000-0003000000a1");

    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsUserAsync(async database =>
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO book (id, owner_id, title, status) VALUES (@id, @owner, 'Product Engineering', 'draft')",
            new { id = AnasBook, owner = SeedIds.Users.Ana });
        return "ana@codex.local";
    });

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task GetOutline_WhenATitleNamesAnOwnedAndASharedBook_ShouldFailAmbiguousWithCandidatesNamingTheirOwners()
    {
        var error = await _server.Client.ErrorAsync("get_outline", new { book = "Product Engineering" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        var owners = error.GetProperty("candidates").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetGuid(), c => c.GetProperty("owner").GetProperty("display_name").GetString());
        owners.ShouldBe(new Dictionary<Guid, string?>
        {
            [SeedIds.Books.ProductEngineering] = "Joel Dickson",
            [AnasBook] = "Ana Kowalczyk",
        }, ignoreOrder: true);
    }
}
