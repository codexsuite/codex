using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Codex.Application.Mcp;

// MCP results speak #20's wire format: snake_case names (`open_url`, `side_effects`), nulls left out. This differs
// from the HTTP API's camelCase on purpose: #20 is the contract agents read.
public static class McpJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    // A fresh set of options with the same settings. The MCP SDK caches the AIFunction behind each tool, resource and
    // prompt in a table keyed by the options it was given, and a cached function holds the host's service provider. Under
    // one static instance that table lives for the whole process and every host started in it stays reachable; under an
    // instance a host owns, the table goes with the host (McpServerSetup passes each host its own).
    public static JsonSerializerOptions Create() => new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
