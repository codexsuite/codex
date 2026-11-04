namespace Codex.Application.Features.Import;

// An image sent with an import whose source places images (pandoc): its path in the folder (checked by ImportLimits),
// its length, and a way to open it. It is never buffered whole by the endpoint; the asset intake reads it once.
public sealed record ImportImage(string Path, long Length, Func<Stream> Open);
