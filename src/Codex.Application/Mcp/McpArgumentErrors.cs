using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// The SDK binds tool arguments before a tool runs and throws when it can't (a JsonException for a wrong type, an
// ArgumentException for a missing required argument, an McpProtocolException for an unknown tool). Those are the
// caller's mistakes, so they become `invalid` with the argument at fault, found by checking the call against the
// tool's own input schema. Only a failure the schema check can name is mapped; anything else stays internal_error,
// so a bug in a tool is never reported as the caller's fault.
public static class McpArgumentErrors
{
    public static McpError? From(Exception exception, RequestContext<CallToolRequestParams> context)
    {
        if (exception is not (JsonException or ArgumentException or ModelContextProtocol.McpProtocolException))
        {
            return null;
        }
        var name = context.Params?.Name ?? "";
        var tool = context.MatchedPrimitive as McpServerTool
                   ?? (context.Server.ServerOptions.ToolCollection is { } tools && tools.TryGetPrimitive(name, out var found) ? found : null);
        if (tool == null)
        {
            return Invalid("name", $"There is no tool \"{name}\"; list_tools shows what this server offers.");
        }
        var schema = tool.ProtocolTool.InputSchema;
        var arguments = context.Params?.Arguments ?? new Dictionary<string, JsonElement>();
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
            && required.EnumerateArray().Select(r => r.GetString()!).FirstOrDefault(f => !arguments.ContainsKey(f)) is { } missing)
        {
            return Invalid(missing, $"{name} needs {missing}.");
        }
        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var (field, value) in arguments)
            {
                if (properties.TryGetProperty(field, out var property) && !Fits(property, value))
                {
                    return Invalid(field, $"{field} should be {Expected(property)}, not {Describe(value)}.");
                }
            }
        }
        return null;
    }

    private static McpError Invalid(string field, string message) => new(McpErrorCodes.Invalid, message, null, field, null);

    private static bool Fits(JsonElement schema, JsonElement value)
    {
        var types = TypesOf(schema);
        return types.Count == 0 || types.Any(type => type switch
        {
            "string" => value.ValueKind == JsonValueKind.String
                        && (!schema.TryGetProperty("format", out var format) || format.GetString() != "uuid" || Guid.TryParse(value.GetString(), out _)),
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array
                       && (!schema.TryGetProperty("items", out var items) || value.EnumerateArray().All(item => Fits(items, item))),
            _ => true,
        });
    }

    private static List<string> TypesOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return [];
        }
        return type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()!).ToList() : [type.GetString()!];
    }

    private static string Expected(JsonElement schema)
    {
        var types = TypesOf(schema).Where(t => t != "null").ToList();
        var uuid = schema.TryGetProperty("format", out var format) && format.GetString() == "uuid";
        return uuid ? "an id (uuid)" : string.Join(" or ", types.Select(t => t == "array" ? "a list" : $"a {t}"));
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"\"{value.GetString()}\"",
        JsonValueKind.Array => "a list",
        JsonValueKind.Object => "an object",
        _ => value.GetRawText(),
    };
}
