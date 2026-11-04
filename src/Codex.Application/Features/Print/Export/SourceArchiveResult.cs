namespace Codex.Application.Features.Print.Export;

// The source export's .zip and what it left out ("1 pending AI draft is not included"), for the job's notes.
public sealed record SourceArchiveResult(byte[] Bytes, IReadOnlyList<string> Notes);
