using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Books;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.Shell;

internal static class ShellJson
{
    public static async Task<LibraryDto> GetLibraryAsync(this HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/library" + query);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LibraryDto>())!;
    }

    public static JsonNode Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;

    public static IEnumerable<string> PropertyNames(JsonNode node) =>
        node.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal);
}
