namespace Codex.Contracts.Print;

// POST /api/scenes/{id}/figures, the way a beat is inserted: the client makes the figure's id and sends the whole new
// scene body holding its `::figure{id}` line, with the version it edited, so the server never reads an editor position.
// AssetId is an image of the book or its series; Placement is column (the default), page, spread or float-top.
public sealed record CreateFigureRequest(
    Guid FigureId, Guid AssetId, string? Body, int Version, string? Placement = null, string? Caption = null, string? AltText = null);
