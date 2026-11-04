using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Auth;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.TestSupport;

namespace Codex.WebApi.Tests;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/contracts.test.ts.
[TestFixture]
public class DtoFixtureTests
{
    [Test]
    public void LibraryDto_RoundTrips()
    {
        RoundTrips<LibraryDto>("library.json").ShouldBeTrue();
    }

    [Test]
    public void BookHeaderDto_RoundTrips()
    {
        RoundTrips<BookHeaderDto>("book-header.json").ShouldBeTrue();
    }

    [Test]
    public void MeDto_RoundTrips()
    {
        RoundTrips<MeDto>("current-user.json").ShouldBeTrue();
    }

    [Test]
    public void CreatedDeviceSessionDto_RoundTrips()
    {
        RoundTrips<CreatedDeviceSessionDto>("created-device-session.json").ShouldBeTrue();
    }

    [Test]
    public void ApiError_RoundTrips()
    {
        RoundTrips<List<ApiError>>("api-errors.json").ShouldBeTrue();
        Fixture("api-errors.json").AsArray().Select(n => n!["code"]!.GetValue<string>()).Order()
            .ShouldBe(ApiErrorCodes.All.Order());
    }

    [Test]
    public void BookEvents_RoundTrip()
    {
        RoundTrips<List<BookEvent>>("book-events.json").ShouldBeTrue();
    }

    private static JsonNode Fixture(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;

    private static bool RoundTrips<T>(string name)
    {
        var original = Fixture(name);
        var model = original.Deserialize<T>(JsonSerializerOptions.Web);

        var again = JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web);

        return JsonNode.DeepEquals(again, original);
    }
}
