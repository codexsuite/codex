namespace Codex.Contracts.Auth;

// POST /api/auth/tokens: the new token's row and its secret (cdx_pat_…), which the server keeps only as a hash and
// never shows again.
public sealed record CreatedApiTokenDto(ApiTokenDto Token, string Secret);
