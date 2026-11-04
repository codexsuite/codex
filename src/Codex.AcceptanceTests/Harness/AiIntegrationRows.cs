using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.AcceptanceTests.Harness;

// JD's default AI integration for scenarios that draft through a fake IProseGenerator: the Write draft panel offers
// Generate only when the user has one connected (#36). The fake ignores the key, so its value does not matter.
public static class AiIntegrationRows
{
    public static readonly Guid IntegrationId = new("0199c0de-0000-7000-8000-000d00000001");

    public const string DefaultModel = "claude-sonnet-5-5";

    public static async Task InsertAsync(string connectionString, IServiceProvider services)
    {
        var credential = services.GetRequiredService<IAiCredentialProtector>().Protect("sk-test-0000");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ai_integration (id, owner_id, kind, label, credential, key_hint, default_model, models, is_default)
            VALUES (@id, @owner, 'anthropic', 'Anthropic', @credential, 'sk-…0000', @model,
                    ARRAY['claude-sonnet-5-5', 'claude-opus-5-5', 'claude-haiku-5-5'], true)
            """,
            connection);
        command.Parameters.AddWithValue("id", IntegrationId);
        command.Parameters.AddWithValue("owner", SeedIds.Users.Jd);
        command.Parameters.AddWithValue("credential", credential);
        command.Parameters.AddWithValue("model", DefaultModel);
        await command.ExecuteNonQueryAsync();
    }
}
