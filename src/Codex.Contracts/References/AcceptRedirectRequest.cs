namespace Codex.Contracts.References;

// POST /api/references/{id}/recheck/accept: RedirectTo is the address a re-check reported.
public sealed record AcceptRedirectRequest(string RedirectTo);
