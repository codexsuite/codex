namespace Codex.Contracts.Citations;

// POST /api/citations/{useId}/remove: Version is the prose version of the use's scene.
public sealed record RemoveCitationRequest(int Version);
