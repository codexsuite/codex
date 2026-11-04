using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.Structure;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/structure.test.ts and manuscript.test.ts.
[TestFixture]
public class StructureDtoFixtureTests
{
    [Test]
    public void OutlineDto_RoundTrips()
    {
        RoundTrips<OutlineDto>("structure/outline.json").ShouldBeTrue();
    }

    [Test]
    public void OutlineOpenerDto_RoundTrips()
    {
        RoundTrips<OutlineOpenerDto>("structure/outline-opener.json").ShouldBeTrue();
    }

    [Test]
    public void LabelDtos_RoundTrip()
    {
        RoundTrips<List<LabelDto>>("structure/labels.json").ShouldBeTrue();
    }

    [Test]
    public void LabelRefs_RoundTrip()
    {
        RoundTrips<List<LabelRefDto>>("structure/label-refs.json").ShouldBeTrue();
    }

    [Test]
    public void PatchSceneRequest_RoundTrips()
    {
        RoundTrips<PatchSceneRequest>("structure/patch-scene-request.json").ShouldBeTrue();
    }

    [Test]
    public void SetSceneLabelsRequest_RoundTrips()
    {
        RoundTrips<SetSceneLabelsRequest>("structure/set-scene-labels-request.json").ShouldBeTrue();
    }

    [Test]
    public void CreateLabelRequest_RoundTrips()
    {
        RoundTrips<CreateLabelRequest>("structure/create-label-request.json").ShouldBeTrue();
    }

    [Test]
    public void PatchLabelRequest_RoundTrips()
    {
        RoundTrips<PatchLabelRequest>("structure/patch-label-request.json").ShouldBeTrue();
    }

    [Test]
    public void ProseDto_RoundTrips()
    {
        RoundTrips<ProseDto>("manuscript/prose.json").ShouldBeTrue();
    }

    [Test]
    public void SaveProseRequest_RoundTrips()
    {
        RoundTrips<SaveProseRequest>("manuscript/save-prose-request.json").ShouldBeTrue();
    }

    [Test]
    public void SaveProseResult_RoundTrips()
    {
        RoundTrips<SaveProseResult>("manuscript/save-prose-result.json").ShouldBeTrue();
    }

    private static bool RoundTrips<T>(string name)
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
        var model = original.Deserialize<T>(JsonSerializerOptions.Web);

        return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original);
    }
}
