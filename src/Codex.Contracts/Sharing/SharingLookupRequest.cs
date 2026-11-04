namespace Codex.Contracts.Sharing;

// POST /api/books/{id}/sharing/lookup: one exact email (case and surrounding whitespace ignored, nothing else). A POST
// so the address never lands in a URL or an access log.
public sealed record SharingLookupRequest(string Email);
