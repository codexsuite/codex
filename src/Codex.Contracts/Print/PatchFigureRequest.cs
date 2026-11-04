namespace Codex.Contracts.Print;

// PATCH /api/figures/{id}: a null member is left as it is; an empty Caption or AltText clears it.
public sealed record PatchFigureRequest(Guid? AssetId = null, string? Placement = null, string? Caption = null, string? AltText = null);
