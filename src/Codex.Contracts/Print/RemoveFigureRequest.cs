namespace Codex.Contracts.Print;

// POST /api/figures/{id}/remove: takes the `::figure` line out of the scene saved at Version; the line going archives the figure.
public sealed record RemoveFigureRequest(int Version);
