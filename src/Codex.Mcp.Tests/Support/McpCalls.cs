using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Codex.Mcp.Tests.Support;

// Calls a tool and reads its JSON result (#20 section 2.2): OkAsync expects `ok: true`, ErrorAsync expects an MCP tool
// error whose body is { ok: false, error: { code, message, candidates } }.
public static class McpCalls
{
    public static async Task<JsonElement> OkAsync(this McpClient client, string tool, object? arguments = null)
    {
        var result = await client.CallToolAsync(tool, Arguments(arguments), cancellationToken: Token);
        var body = Body(result);
        result.IsError.ShouldNotBe(true, body.ToString());
        body.GetProperty("ok").GetBoolean().ShouldBeTrue();
        return body;
    }

    public static async Task<JsonElement> ErrorAsync(this McpClient client, string tool, object? arguments = null)
    {
        var result = await client.CallToolAsync(tool, Arguments(arguments), cancellationToken: Token);
        var body = Body(result);
        result.IsError.ShouldBe(true, body.ToString());
        body.GetProperty("ok").GetBoolean().ShouldBeFalse();
        return body.GetProperty("error");
    }

    // The running test's token: cancelled by the class's [CancelAfter], so a call to a hung server fails the test.
    public static CancellationToken Token => TestContext.CurrentContext.CancellationToken;

    private static Dictionary<string, object?> Arguments(object? arguments) =>
        arguments == null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments))!;

    private static JsonElement Body(CallToolResult result) =>
        JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();
}
