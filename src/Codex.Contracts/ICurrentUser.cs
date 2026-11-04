namespace Codex.Contracts;

// The user the request acts as: the signed-in user on an HTTP request (the cookie's NameIdentifier claim), and the
// configured MCP user (CODEX_MCP_USER_EMAIL, JD by default) on stdio and the localhost /mcp binding. It is the single
// source of the user id for the API and MCP alike: nothing takes a user id as a parameter.
public interface ICurrentUser
{
    Guid Id { get; }
}
