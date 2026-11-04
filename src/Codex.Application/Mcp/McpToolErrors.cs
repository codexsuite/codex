using Codex.Contracts.Errors;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// The one place a failed tool call becomes a result: an McpToolException keeps its code and candidates, an
// ApiException from a command or query keeps its code (validation → invalid), the SDK's own argument binding failures
// are invalid with the field (McpArgumentErrors), and anything else is internal_error
// with a fixed message, as on the HTTP API. The result is an MCP tool error (isError) whose text is the JSON body, so
// the agent reads the code instead of the call failing at the protocol level.
public static class McpToolErrors
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (context, cancellationToken) =>
    {
        try
        {
            return await next(context, cancellationToken);
        }
        catch (McpToolException e)
        {
            return Result(new McpError(e.Code, e.Message, e.Candidates, e.Field, e.Rule, e.Scope, e.OpenUrl));
        }
        catch (ApiException e)
        {
            return Result(new McpError(McpErrorCodes.FromApiCode(e.Code), e.Message, null, null, null));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (McpArgumentErrors.From(e, context) is { } invalid)
            {
                return Result(invalid);
            }
            if (context.Services?.GetService(typeof(ILoggerFactory)) is ILoggerFactory loggers)
            {
                loggers.CreateLogger(typeof(McpToolErrors)).LogError(e, "MCP tool {Tool} failed", context.Params?.Name);
            }
            return Result(new McpError(McpErrorCodes.Internal, ApiException.Internal().Message, null, null, null));
        }
    };

    public static CallToolResult Result(McpError error) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = McpJson.Serialize(new { ok = false, error }) }],
    };
}
