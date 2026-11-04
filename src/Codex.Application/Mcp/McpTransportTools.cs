using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Tools that only make sense on one transport (#65): upload_image reads a file on the server's machine, so it is
// stdio's alone; create_image_upload hands out a URL on the web host, so it is HTTP's alone. The list filter leaves
// each out of the other transport's tools/list, and Require refuses a call made to it anyway.
public static class McpTransportTools
{
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Filter(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) => async (context, cancellationToken) =>
    {
        var result = await next(context, cancellationToken);
        var hidden = IsStdio(context.Services) ? ImageTools.CreateImageUpload : ImageTools.UploadImage;
        result.Tools = result.Tools.Where(t => t.Name != hidden).ToList();
        return result;
    };

    public static void Require(McpServer server, string tool)
    {
        var stdio = IsStdio(server.Services);
        if (tool == ImageTools.UploadImage && !stdio)
        {
            throw McpToolException.Invalid("name", "upload_image reads a file on the server's machine, so it is served over stdio only. Use create_image_upload.");
        }
        if (tool == ImageTools.CreateImageUpload && stdio)
        {
            throw McpToolException.Invalid("name", "create_image_upload is served over HTTP only. Over stdio, use upload_image with the file's path.");
        }
    }

    private static bool IsStdio(IServiceProvider? services) => services?.GetService<McpStdioTransport>() != null;
}
