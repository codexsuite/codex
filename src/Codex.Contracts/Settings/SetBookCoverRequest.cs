namespace Codex.Contracts.Settings;

// PUT /api/books/{id}/cover: an uploaded image (POST /api/books/{id}/assets) of the book or its series becomes the
// front cover that Library and the book bar show. A missing AssetId is a 400.
public sealed record SetBookCoverRequest(Guid? AssetId);
