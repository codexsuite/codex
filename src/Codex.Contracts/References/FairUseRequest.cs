namespace Codex.Contracts.References;

// POST /api/references/{id}/fair-use. Reason is required.
public sealed record FairUseRequest(string Reason);
