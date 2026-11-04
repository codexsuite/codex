namespace Codex.Application.Features.Import;

// One uploaded file: its path relative to the picked folder ("manuscript/act-01-x/index.md"), already checked by
// ImportLimits, and its bytes.
public sealed record ImportFile(string Path, byte[] Content);
