namespace Codex.Application.Identity;

// A personal access token that checked out (ApiTokens.AuthenticateAsync): the user it acts as and what it may do.
public sealed record ApiTokenGrant(Guid TokenId, string Name, Guid UserId, IReadOnlyList<string> Scopes);
