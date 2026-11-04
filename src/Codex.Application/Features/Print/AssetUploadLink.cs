namespace Codex.Application.Features.Print;

// A minted upload link: the token goes in the URL (PUT /api/asset-uploads/{token}); it is shown once and stored only as a hash.
public sealed record AssetUploadLink(string Token, DateTimeOffset ExpiresAt);
