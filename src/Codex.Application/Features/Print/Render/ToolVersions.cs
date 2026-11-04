using System.Diagnostics;
using Agoda.IoC.Core;

namespace Codex.Application.Features.Print.Render;

// The pandoc and Tectonic versions the content hash covers ("pandoc 3.12|Tectonic 0.15.0"). The web and worker images
// differ in what they install (the web image has no Tectonic), so each sets CODEX_TOOLCHAIN at build time from the same
// pins and both hash the same string; a tool bump changes it, and cached proofs stop reading as current. An operator
// never sets it. Without it (a dev host, the tests) the versions are read once from the tools' --version (run directly:
// no input, trusted binaries). A missing tool reads "none", so a host without the toolchain can still assemble and hash.
[RegisterSingleton]
public class ToolVersions(RenderSettings settings)
{
    public const string ToolchainKey = "CODEX_TOOLCHAIN";

    private readonly Lazy<string> _value = new(() =>
        Environment.GetEnvironmentVariable(ToolchainKey) is { Length: > 0 } built
            ? built
            : string.Join('|', Version(settings.Pandoc, RenderSettings.PandocKey), Version(settings.Tectonic, RenderSettings.TectonicKey)));

    public virtual string Value => _value.Value;

    private static string Version(string tool, string key)
    {
        string path;
        try
        {
            path = RenderSettings.Resolve(tool, key);
        }
        catch (Codex.Contracts.Errors.ApiException)
        {
            return Path.GetFileName(tool) + " none";
        }

        var start = new ProcessStartInfo(path, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(start);
        if (process is null)
        {
            return Path.GetFileName(tool) + " none";
        }

        var first = process.StandardOutput.ReadLine() ?? string.Empty;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return first.Trim();
    }
}
