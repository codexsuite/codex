namespace Codex.Contracts.Sharing;

// PUT /api/books/{id}/sharing/{userId}: adds or changes a grant. Role is "view", "comment" or "write".
public sealed record SetGrantRequest(string Role);
