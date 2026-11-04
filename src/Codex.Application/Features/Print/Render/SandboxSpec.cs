namespace Codex.Application.Features.Print.Render;

// One sandboxed run: Program (a full path) with Arguments, in WorkDirectory (read-write). ReadWrite adds writable
// folders, ReadOnly readable ones (Tectonic's warm bundle cache); NetPorts are the only TCP ports it may connect to (none: no network). MaxFileBytes caps
// every file it writes (RLIMIT_FSIZE); MaxMemoryBytes its address space (null for pandoc, whose runtime reserves a
// large range up front); Timeout is both the wall clock and the CPU limit.
public sealed record SandboxSpec(
    string Program,
    IReadOnlyList<string> Arguments,
    string WorkDirectory,
    TimeSpan Timeout,
    long MaxFileBytes,
    long? MaxMemoryBytes = null,
    IReadOnlyList<string>? ReadWriteDirectories = null,
    IReadOnlyList<int>? Ports = null,
    IReadOnlyDictionary<string, string>? Variables = null,
    IReadOnlyList<string>? ReadOnlyDirectories = null)
{
    public IReadOnlyList<string> ReadOnly => ReadOnlyDirectories ?? [];

    public IReadOnlyList<string> ReadWrite => ReadWriteDirectories ?? [];

    public IReadOnlyList<int> NetPorts => Ports ?? [];

    public IReadOnlyDictionary<string, string> Environment => Variables ?? new Dictionary<string, string>();
}
