using System.Reflection;
using Codex.Application.Identity;
using Codex.Application.Mcp;
using ModelContextProtocol.Server;

namespace Codex.Mcp.Tests;

// Every tool needs a scope (#32): a new tool without a row in McpScopes.ForTool would run for any token, so this fails
// until it has one. Rows for tools that no longer exist fail too.
[TestFixture]
public class McpScopesTests
{
    private static readonly IReadOnlyList<string> Tools = typeof(McpServerSetup).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
        .OfType<string>()
        .ToList();

    [Test]
    public void ForTool_ShouldNameAScopeForEveryTool()
    {
        Tools.ShouldNotBeEmpty();
        Tools.Where(t => !McpScopes.ForTool.ContainsKey(t)).ShouldBeEmpty();
    }

    [Test]
    public void ForTool_ShouldOnlyNameToolsThatExist()
    {
        McpScopes.ForTool.Keys.Except(Tools).ShouldBeEmpty();
    }

    [Test]
    public void ForTool_ShouldOnlyUseKnownScopes()
    {
        McpScopes.ForTool.Values.Except(ApiTokenScopes.All).ShouldBeEmpty();
    }

    [Test]
    public void ForTool_WhenAToolIsReadOnly_ShouldNeedOnlyRead()
    {
        var readOnly = typeof(McpServerSetup).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .OfType<McpServerToolAttribute>()
            .Where(a => a.ReadOnly)
            .Select(a => a.Name!);

        readOnly.Where(t => McpScopes.ForTool[t] != ApiTokenScopes.Read).ShouldBeEmpty();
    }
}
