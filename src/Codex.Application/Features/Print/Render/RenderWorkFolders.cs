using Agoda.IoC.Core;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Print.Render;

// The per-job folders under CODEX_RENDER_WORK: created owner-only (0700), deleted without letting a cleanup failure
// hide the error that caused it, and swept of folders left by a crashed process (older than StaleAfter) on startup.
[RegisterSingleton]
public sealed class RenderWorkFolders(RenderSettings settings, TimeProvider clock, ILogger<RenderWorkFolders> logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public string Create()
    {
        var job = Path.Combine(settings.WorkRoot, Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            return Directory.CreateDirectory(job).FullName;
        }

        Directory.CreateDirectory(settings.WorkRoot, OwnerOnly);
        return Directory.CreateDirectory(job, OwnerOnly).FullName;
    }

    public void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete the render job folder {Folder}", folder);
        }
    }

    public int SweepStale()
    {
        if (!Directory.Exists(settings.WorkRoot))
        {
            return 0;
        }

        var cutoff = clock.GetUtcNow() - StaleAfter;
        var stale = Directory.EnumerateDirectories(settings.WorkRoot).Where(d => Directory.GetLastWriteTimeUtc(d) < cutoff.UtcDateTime).ToList();
        stale.ForEach(TryDelete);
        return stale.Count;
    }
}
