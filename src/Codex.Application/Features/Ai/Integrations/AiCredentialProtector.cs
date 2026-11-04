using System.Security.Cryptography;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.DataProtection;

namespace Codex.Application.Features.Ai.Integrations;

// IAiCredentialProtector over one Data Protection purpose. The key ring is the provider's (AiIntegrationServices
// builds it from CODEX_DATA_PROTECTION_DIR); a rotated ring still decrypts what older keys protected. A credential the
// ring can't read (the key folder was lost or replaced) is not_configured, so drafting and Test ask for the key again.
public sealed class AiCredentialProtector(IDataProtectionProvider provider) : IAiCredentialProtector
{
    private const string Purpose = "Codex.AiIntegration.Credential.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string apiKey) => _protector.Protect(apiKey);

    public string Unprotect(string credential)
    {
        try
        {
            return _protector.Unprotect(credential);
        }
        catch (CryptographicException)
        {
            throw ApiException.NotConfigured("The saved key can't be read with this server's key folder. Enter the key again in Settings → AI.");
        }
    }

    // "sk-…abcd": the key's prefix up to its first '-' (when that is short) and its last 4 characters. A key under 20
    // characters shows no prefix, and one under 8 shows no characters at all, so a short key is never mostly revealed.
    public static string Hint(string apiKey)
    {
        var key = apiKey.Trim();
        if (key.Length < 8)
        {
            return "…";
        }

        if (key.Length < 20)
        {
            return $"…{key[^4..]}";
        }

        var dash = key.IndexOf('-', StringComparison.Ordinal);
        var prefix = dash is > 0 and <= 4 ? key[..(dash + 1)] : string.Empty;
        return $"{prefix}…{key[^4..]}";
    }
}
