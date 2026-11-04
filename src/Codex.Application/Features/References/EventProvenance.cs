namespace Codex.Application.Features.References;

// Who a History line (reference_event) is written as. The web endpoints pass nothing and get the author ('user'); the MCP
// tools pass Agent(clientName, …), so the line is 'agent' with the client's name, and the personal access token's id and
// name when the call came with one (#32), from the moment it is inserted.
public sealed record EventProvenance(string Source = "user", string? ClientName = null, Guid? TokenId = null, string? TokenName = null)
{
    public static EventProvenance Agent(string? clientName, Guid? tokenId = null, string? tokenName = null) =>
        new("agent", clientName, tokenId, tokenName);
}
