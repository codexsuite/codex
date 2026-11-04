using System.Diagnostics;
using System.Text;
using Agoda.IoC.Core;

namespace Codex.Application.Features.Print.Render;

// Runs pandoc or Tectonic through codex-sandbox (src/sandbox/codex-sandbox.c): Landlock confines the program to its
// job folder (read-write), the paths in Spec (read-only) and its own binary, TCP only to Spec.NetPorts, plus file-size,
// address-space and CPU limits. The environment is replaced (nothing of the app's, such as CODEX_DB_CONNECTION,
// reaches the program), arguments go as an argument list (never through a shell), the wall clock kills the whole
// process tree, and stdout/stderr are each kept to their last MaxLogChars characters.
[RegisterSingleton]
public sealed class SandboxedProcess(RenderSettings settings)
{
    public const int MaxLogChars = 200_000;

    // Read-only system paths a program may need: TLS roots and DNS for Tectonic's bundle fetch, fontconfig's setup.
    private static readonly string[] SystemReadOnly =
    [
        "/etc/ssl", "/etc/pki", "/usr/share/ca-certificates", "/etc/ca-certificates", "/etc/resolv.conf", "/etc/hosts",
        "/etc/nsswitch.conf", "/etc/fonts", "/usr/share/fonts", "/dev/null", "/dev/urandom",
    ];

    public async Task<ProcessOutcome> RunAsync(SandboxSpec spec, CancellationToken cancellationToken)
    {
        var sandbox = RenderSettings.Resolve(settings.Sandbox, RenderSettings.SandboxKey);
        var start = new ProcessStartInfo(sandbox)
        {
            WorkingDirectory = spec.WorkDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Clear();
        start.Environment["HOME"] = spec.WorkDirectory;
        start.Environment["TMPDIR"] = spec.WorkDirectory;
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.Environment["LANG"] = "C.UTF-8";
        foreach (var (key, value) in spec.Environment)
        {
            start.Environment[key] = value;
        }

        var args = start.ArgumentList;
        foreach (var dir in spec.ReadWrite.Prepend(spec.WorkDirectory))
        {
            args.Add("--rw");
            args.Add(dir);
        }
        foreach (var path in spec.ReadOnly.Concat(SystemReadOnly))
        {
            args.Add("--ro");
            args.Add(path);
        }
        args.Add("--exec");
        args.Add(spec.Program);
        foreach (var port in spec.NetPorts)
        {
            args.Add("--net-port");
            args.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        args.Add("--fsize");
        args.Add(spec.MaxFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (spec.MaxMemoryBytes is { } memory)
        {
            args.Add("--as");
            args.Add(memory.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        // CPU time above the wall clock, so a run that hits the wall clock is reported as a timeout, not a kill.
        args.Add("--cpu");
        args.Add(((long)Math.Ceiling(spec.Timeout.TotalSeconds * 2) + 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
        args.Add("--nproc");
        args.Add(settings.MaxProcesses.ToString(System.Globalization.CultureInfo.InvariantCulture));
        args.Add("--");
        args.Add(spec.Program);
        foreach (var arg in spec.Arguments)
        {
            args.Add(arg);
        }

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The sandbox did not start.");
        process.StandardInput.Close();
        var stdout = ReadCappedAsync(process.StandardOutput);
        var stderr = ReadCappedAsync(process.StandardError);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(spec.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (!timedOut)
            {
                throw;
            }
        }

        return new ProcessOutcome(process.ExitCode, await stdout, await stderr, timedOut, clock.Elapsed);
    }

    // Keeps the tail: the error that stopped a compile is at the end of its output.
    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > 2 * MaxLogChars)
            {
                text.Remove(0, text.Length - MaxLogChars);
            }
        }

        return text.Length > MaxLogChars ? text.ToString(text.Length - MaxLogChars, MaxLogChars) : text.ToString();
    }
}
