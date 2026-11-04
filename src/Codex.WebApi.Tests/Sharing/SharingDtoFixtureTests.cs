using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Sharing;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.Sharing;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/sharingContract.test.ts.
[TestFixture]
public class SharingDtoFixtureTests
{
    [TestCase("sharing/sharing.json")]
    [TestCase("sharing/sharing-read-only.json")]
    public void SharingDto_RoundTrips(string name)
    {
        RoundTrips<SharingDto>(name).ShouldBeTrue();
    }

    [Test]
    public void SharingLookupRequest_RoundTrips()
    {
        RoundTrips<SharingLookupRequest>("sharing/sharing-lookup-request.json").ShouldBeTrue();
    }

    [Test]
    public void SharingUserDto_RoundTrips()
    {
        RoundTrips<SharingUserDto>("sharing/sharing-user.json").ShouldBeTrue();
    }

    [Test]
    public void SetGrantRequest_RoundTrips()
    {
        RoundTrips<SetGrantRequest>("sharing/set-grant-request.json").ShouldBeTrue();
    }

    [Test]
    public void SharingGrantDto_RoundTrips()
    {
        RoundTrips<SharingGrantDto>("sharing/sharing-grant.json").ShouldBeTrue();
    }

    private static bool RoundTrips<T>(string name)
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
        var again = JsonSerializer.SerializeToNode(original.Deserialize<T>(JsonSerializerOptions.Web), JsonSerializerOptions.Web);
        return JsonNode.DeepEquals(again, original);
    }
}
