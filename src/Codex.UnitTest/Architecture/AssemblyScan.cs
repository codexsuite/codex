using System.Reflection;

namespace Codex.UnitTest.Architecture;

public static class AssemblyScan
{
    // Names of referenced assemblies starting with any of the prefixes.
    public static IReadOnlyList<string> ReferencesTo(Assembly assembly, params string[] prefixes)
        => WithPrefix(assembly.GetReferencedAssemblies().Select(a => a.Name!), prefixes);

    public static IReadOnlyList<string> WithPrefix(IEnumerable<string> names, params string[] prefixes)
        => names.Where(name => prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))).ToList();

    // The compiler drops a reference nothing uses, so the project files are read as well.
    public static IReadOnlyList<string> ProjectFileReferences(string projectFile, params string[] markers)
    {
        return Mentioning(File.ReadAllText(projectFile), markers);
    }

    public static IReadOnlyList<string> Mentioning(string text, params string[] markers)
    {
        return markers.Where(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
