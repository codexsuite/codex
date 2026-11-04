using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DbUp.Engine;

namespace Codex.Application.Database;

// The db/ folder, embedded in this assembly, in apply order: schema, then migrations, then seed,
// each folder in file-name order. The app applies schema + migrations; test fixtures also apply the seed.
public static class DatabaseScripts
{
    private static readonly string[] Folders = ["db/schema/", "db/migrations/", "db/seed/"];

    public static IReadOnlyList<SqlScript> Load(bool includeSeed)
    {
        var assembly = typeof(DatabaseScripts).Assembly;
        var scripts = new List<SqlScript>();
        for (var group = 0; group < Folders.Length; group++)
        {
            if (!includeSeed && group == 2)
            {
                break;
            }
            var names = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Folders[group], StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
            scripts.AddRange(names.Select(n => new SqlScript(n, ReadResource(assembly, n), new SqlScriptOptions { RunGroupOrder = group })));
        }
        return scripts;
    }

    // Identifies the exact db/ contents: names and bytes, in apply order.
    public static string Hash(bool includeSeed)
    {
        var text = new StringBuilder();
        foreach (var script in Load(includeSeed))
        {
            text.Append(script.Name).Append('\0').Append(script.Contents).Append('\0');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..12];
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
