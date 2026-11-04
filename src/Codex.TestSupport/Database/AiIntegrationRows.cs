using Codex.Application.Features.Ai.Integrations;
using Dapper;
using Npgsql;

namespace Codex.TestSupport.Database;

// Inserts an ai_integration row straight into a test database, with the key encrypted by the host's own protector
// (what the store lane's endpoints would have written).
public static class AiIntegrationRows
{
    public static Task<Guid> InsertAsync(
        NpgsqlConnection db, IAiCredentialProtector protector, Guid ownerId, string label, string apiKey, string kind = "anthropic",
        string? baseUrl = null, string? defaultModel = null, bool isDefault = false)
        => db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ai_integration (owner_id, kind, label, base_url, credential, key_hint, default_model, is_default)
            VALUES (@ownerId, @kind, @label, @baseUrl, @credential, @hint, @defaultModel, @isDefault)
            RETURNING id
            """,
            new { ownerId, kind, label, baseUrl, credential = protector.Protect(apiKey), hint = AiCredentialProtector.Hint(apiKey), defaultModel, isDefault });
}
