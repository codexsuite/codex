namespace Codex.Application.Features.Ai.Integrations;

// Server-owner settings for AI integrations (#36). The users' own keys live in ai_integration, never here.
public static class AiSettings
{
    // Folder holding the Data Protection key ring that encrypts ai_integration.credential. Kept off the database, so a
    // database dump alone cannot decrypt a key; the web app, worker and stdio MCP host must share it. A relative path
    // resolves against the content root.
    public const string DataProtectionDirKey = "CODEX_DATA_PROTECTION_DIR";

    // "true" lets users point an OpenAI-compatible integration at http:// and private-network addresses (Ollama, LM
    // Studio on the LAN). Off by default: a user-supplied base URL is otherwise https:// to a public address only.
    public const string AllowLocalEndpointsKey = "CODEX_AI_ALLOW_LOCAL_ENDPOINTS";
}
