using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace Codex.AcceptanceTests.Harness;

// Pins the harness contract on an empty (schema-only) clone.
[TestFixture]
public class HarnessAppTests : EmptyAcceptanceTest
{
    [Test]
    public async Task Harness_WhenAppStarts_ShouldAnswerHealth()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        var body = await client.GetFromJsonAsync<JsonElement>("/api/health");

        body.GetProperty("status").GetString().ShouldBe("ok");
    }
}

// Two seeded classes in one run must each get a private database. Each records its database name when it
// starts; each test checks that no other class has the same one.
internal static class SeededDatabaseNames
{
    private static readonly ConcurrentBag<string> Started = [];

    public static void Register(string name) => Started.Add(name);

    public static int CountOf(string name) => Started.Count(n => n == name);
}

[TestFixture]
public class HarnessSeededClassOneTests : SeededAcceptanceTest
{
    [OneTimeSetUp]
    public void Register() => SeededDatabaseNames.Register(DatabaseName);

    [Test]
    public void Harness_WhenSeededClassStarts_ShouldGetOwnDatabase()
    {
        SeededDatabaseNames.CountOf(DatabaseName).ShouldBe(1);
        DatabaseName.ShouldStartWith("codex_t_");
    }
}

[TestFixture]
public class HarnessSeededClassTwoTests : SeededAcceptanceTest
{
    [OneTimeSetUp]
    public void Register() => SeededDatabaseNames.Register(DatabaseName);

    [Test]
    public void Harness_WhenSeededClassStarts_ShouldGetOwnDatabase()
    {
        SeededDatabaseNames.CountOf(DatabaseName).ShouldBe(1);
        DatabaseName.ShouldStartWith("codex_t_");
    }
}
