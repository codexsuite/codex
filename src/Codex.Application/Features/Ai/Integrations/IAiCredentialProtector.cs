namespace Codex.Application.Features.Ai.Integrations;

// Encrypts an API key for ai_integration.credential and decrypts it again, with the Data Protection key ring in
// CODEX_DATA_PROTECTION_DIR. AiCredentialProtector.Hint gives the "sk-…abcd" the browser is shown instead.
public interface IAiCredentialProtector
{
    string Protect(string apiKey);

    string Unprotect(string credential);
}
