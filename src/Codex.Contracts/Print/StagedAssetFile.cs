namespace Codex.Contracts.Print;

// A file offered to IAssetIntake.StageAsync: its name, declared type and length, and its bytes. Source, Prompt and
// Licence are optional notes kept in asset.meta.
public sealed record StagedAssetFile(
    string FileName, string ContentType, long Length, Stream Content, string? Source = null, string? Prompt = null, string? Licence = null);
