namespace Codex.Contracts.Storage;

// A stored blob. The caller disposes the stream.
public sealed record BlobContent(Stream Content, string ContentType);
