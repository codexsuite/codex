using System.Diagnostics;
using Codex.Application.Telemetry;
using Codex.Contracts.Errors;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Records every MCP tool call (contract C4 of SHIP-257): one mcp.tool_call span, a codex.mcp.tool_calls increment and a
// codex.mcp.tool_duration sample, tagged with the tool and one outcome:
//   ok         the tool returned a result;
//   error      the call ended in an error result: a code the tool raised (McpToolException, ApiException), a call the
//              caller got wrong (unknown tool, a missing or mistyped argument), or a result with isError set;
//   exception  the tool failed in a way that is a bug; McpToolErrors turns it into internal_error.
// Registered after McpToolErrors and the scope check, so it sits closest to the tool and sees the exception before
// McpToolErrors turns it into a result. A call the client cancelled records the span without an outcome and no metrics.
// The tag is a registered tool's name, or "unknown" for any other name, so a client cannot make up tag values. The
// arguments, the result and the exception message are never recorded: they hold book ids and prose.
public static class McpTelemetryFilter
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Thrown = "exception";

    public const string UnknownTool = "unknown";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (context, cancellationToken) =>
    {
        var tool = ToolName(context);
        using var activity = CodexTelemetry.Source.StartActivity(CodexTelemetry.Spans.McpToolCall);
        activity?.SetTag(CodexTelemetry.SpanTags.McpTool, tool);
        var started = Stopwatch.GetTimestamp();
        string? outcome = null;
        try
        {
            var result = await next(context, cancellationToken);
            outcome = result.IsError == true ? Error : Ok;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            outcome = e is McpToolException or ApiException || McpArgumentErrors.From(e, context) != null ? Error : Thrown;
            throw;
        }
        finally
        {
            if (outcome != null)
            {
                Record(activity, tool, outcome, Stopwatch.GetElapsedTime(started));
            }
        }
    };

    private static void Record(Activity? activity, string tool, string outcome, TimeSpan elapsed)
    {
        activity?.SetTag(CodexTelemetry.SpanTags.Outcome, outcome);
        if (outcome != Ok)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        var tags = new TagList
        {
            { CodexTelemetry.Tags.Tool, tool },
            { CodexTelemetry.Tags.Outcome, outcome },
        };
        CodexTelemetry.McpToolCalls.Add(1, tags);
        CodexTelemetry.McpToolDuration.Record(elapsed.TotalSeconds, tags);
    }

    private static string ToolName(RequestContext<CallToolRequestParams> context)
    {
        var name = context.Params?.Name;
        return name != null && context.Server.ServerOptions.ToolCollection is { } tools && tools.TryGetPrimitive(name, out _) ? name : UnknownTool;
    }
}
