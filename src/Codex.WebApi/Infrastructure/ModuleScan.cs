using System.Reflection;

namespace Codex.WebApi.Infrastructure;

// Finds every concrete implementation of a module interface (IEndpointModule, IServiceModule)
// in the given assemblies and creates it. Program.cs runs this over Codex.WebApi and Codex.Application.
public static class ModuleScan
{
    public static IReadOnlyList<T> Find<T>(IEnumerable<Assembly> assemblies)
        => assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (T)Activator.CreateInstance(t)!)
            .ToList();
}
