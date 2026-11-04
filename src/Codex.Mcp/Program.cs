using Agoda.IoC.NetCore;
using Codex.Application;
using Codex.Application.Mcp;
using Codex.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The Codex MCP server over stdio (#20 Part 7), for Claude Desktop / Claude Code on this machine:
//   CODEX_DB_CONNECTION=... dotnet run --project src/Codex.Mcp
// It wires Codex.Application as the web host does (Agoda.IoC scan, every IServiceModule except the HTTP transport's)
// and acts as the one app_user (ICurrentUser, D15), but serves tools only: the modules' hosted services (migrations,
// the rendition worker, the quote-flag scanner) stay with the web host, so this process needs only
// CODEX_DB_CONNECTION and never competes for the web host's work. stdout carries the protocol, so logs go to stderr.
// draft_prose's background drafts are not a hosted service for that reason: McpDraftRunner is a plain task owned by a singleton.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AutoWireAssembly([ApplicationMarker.Assembly], isMockMode: false);
foreach (var module in StdioModules.Find())
{
    module.AddServices(builder.Services, builder.Configuration);
}
builder.Services.RemoveAll<IHostedService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCodexMcpServer().WithStdioServerTransport();

await builder.Build().RunAsync();
