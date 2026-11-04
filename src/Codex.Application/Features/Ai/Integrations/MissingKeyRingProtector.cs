using Codex.Contracts.Errors;

namespace Codex.Application.Features.Ai.Integrations;

// IAiCredentialProtector when CODEX_DATA_PROTECTION_DIR is unset: keys can be neither stored nor read.
public sealed class MissingKeyRingProtector : IAiCredentialProtector
{
    public string Protect(string apiKey) => throw Missing();

    public string Unprotect(string credential) => throw Missing();

    private static ApiException Missing() => ApiException.NotConfigured(
        $"AI integrations need a key folder. Set {AiSettings.DataProtectionDirKey} in the server settings.");
}
