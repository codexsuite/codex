namespace Codex.Application.Features.References;

public sealed record SourceExportFile(string FileName, string ContentType, byte[] Content);
