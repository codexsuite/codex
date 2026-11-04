namespace Codex.Contracts.Print;

// An image IAssetIntake.StageAsync checked and stored, waiting for its asset row. Meta is the asset.meta JSON.
public sealed record StagedAsset(
    Guid Id, Guid BookId, string FileName, string Mime, long Bytes, int WidthPx, int HeightPx, string StorageKey, string Meta);
