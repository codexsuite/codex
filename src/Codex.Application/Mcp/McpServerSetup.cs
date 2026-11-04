using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// The Codex MCP server (#20) without a transport: its tools, resources and prompts (every [McpServerToolType],
// [McpServerResourceType] and [McpServerPromptType] in this assembly), the error filter and the scope checks
// (McpScopes) and the telemetry for each tool call (McpTelemetryFilter). The web host adds the streamable HTTP transport
// (McpHttpServices), the Codex.Mcp process adds stdio.
public static class McpServerSetup
{
    public const string ServerName = "codex";

    private const string Instructions =
        """
        Codex is a novel-writing app; you act as its one author. Refer to things as the author does: a book by title or
        "<series>/<number>", a scene as "5.2" (Ch 5 · Sc 2), a chapter as "5" or "ch:<slug>", an act as "act:2", a
        Knowledge base entry by name or alias. Display numbers are resolved when a call runs, so prefer the ids results
        return for follow-up calls. Every result carries an open_url to hand to the author. read_manuscript pages with
        max_words and cursor. Errors come back as { ok: false, error: { code, message, candidates } }.
        """;

    public static IMcpServerBuilder AddCodexMcpServer(this IServiceCollection services)
    {
        // Options of this host's own: see McpJson.Create.
        var json = McpJson.Create();
        return services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = ServerName, Version = "1.0.0" };
                options.ServerInstructions = Instructions;
            })
            .WithToolsFromAssembly(typeof(McpServerSetup).Assembly, json)
            .WithResources(typeof(McpServerSetup).Assembly, json)
            .WithPromptsFromAssembly(typeof(McpServerSetup).Assembly, json)
            .WithSubscriptionsListenHandler(McpSubscriptions.ListenAsync)
            .WithRequestFilters(filters => filters
                .AddCallToolFilter(McpToolErrors.Filter)
                .AddCallToolFilter(McpScopes.ToolFilter)
                .AddCallToolFilter(McpTelemetryFilter.Filter)
                .AddListToolsFilter(McpTransportTools.Filter)
                .AddReadResourceFilter(McpScopes.ResourceFilter)
                .AddGetPromptFilter(McpScopes.PromptFilter));
    }

    // WithResourcesFromAssembly, but with the host's own serializer options (the SDK's has no parameter for them).
    private static IMcpServerBuilder WithResources(this IMcpServerBuilder builder, Assembly assembly, JsonSerializerOptions json)
    {
        var methods = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerResourceTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.GetCustomAttribute<McpServerResourceAttribute>() != null));
        foreach (var method in methods)
        {
            var type = method.DeclaringType!;
            builder.Services.AddSingleton(services => McpServerResource.Create(
                method,
                context => method.IsStatic ? null! : ActivatorUtilities.CreateInstance(context.Services!, type),
                new McpServerResourceCreateOptions { Services = services, SerializerOptions = json }));
        }

        return builder;
    }
}
