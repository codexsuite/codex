using System.Reflection;

namespace Codex.UnitTest.Architecture;

// The MCP server (Codex.Application.Mcp) is a leaf: it adapts the features' commands and queries, and no feature
// depends on it, so MCP can change or go without touching a feature.
public static class McpScan
{
    public const string McpNamespace = "Codex.Application.Mcp";

    // "Codex.Application.Features.A.Thing -> Codex.Application.Mcp.Other" for every reference from a feature type
    // to a type in the MCP namespace.
    public static IReadOnlyList<string> FeatureReferencesToMcp(IEnumerable<Assembly> assemblies)
        => assemblies
            .SelectMany(a => a.GetTypes())
            .Where(type => FeatureScan.FeatureOf(type) != null)
            .SelectMany(type => TypeReferences.Of(type)
                .Where(IsMcp)
                .Select(referenced => $"{type.FullName} -> {referenced.FullName}"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static bool IsMcp(Type type) =>
        type.Namespace is { } ns && (ns == McpNamespace || ns.StartsWith(McpNamespace + ".", StringComparison.Ordinal));
}
