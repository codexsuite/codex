using System.Diagnostics;
using Agoda.IoC.Core;

namespace Codex.Application.Features.Print.Render;

// The pandoc and Tectonic versions this process renders with ("pandoc 3.12|Tectonic 0.15.0"), read once from the
// tools' --version (run directly: no input, trusted binaries) for the content hash. A missing tool reads "none", so a
// host without the toolchain can still assemble and hash.
[RegisterSingleton]
public sealed class ToolVersions(RenderSettings settings)
{
    private readonly Lazy<string> _value = new(() => string.Join('|', Version(settings.Pandoc, RenderSettings.PandocKey), Version(settings.Tectonic, RenderSettings.TectonicKey)));

    public string Value => _value.Value;

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
