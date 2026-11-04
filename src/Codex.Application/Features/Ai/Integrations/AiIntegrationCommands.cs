using Agoda.IoC.Core;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Ai.Integrations;

// Connect, change, remove, default and test the signed-in user's AI integrations (#36). Every statement is scoped to
// owner_id = ICurrentUser.Id, so another user's integration is not_found. The key is encrypted before it reaches SQL,
// shown back only as key_hint, and kept out of every message and log line.
[RegisterSingleton]
public sealed class AiIntegrationCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IAiCredentialProtector protector, IAiEndpointGuard guard,
    IAiProviders providers, AiIntegrationQueries queries)
{
    public const int MaxLabel = 100;
    public const int MaxKey = 1000;
    public const int MaxModel = 200;

    // The one place the stored credential is selected: for a test, never for a DTO.
    private sealed record SavedConnection(string Kind, string? BaseUrl, string Credential);

    private const string NotFoundMessage = "Integration not found.";

    public async Task<AiIntegrationDto> CreateAsync(CreateAiIntegrationRequest request, CancellationToken cancellationToken)
    {
        var kind = RequireKind(request.Kind);
        var label = RequireLabel(request.Label);
        var key = RequireKey(request.ApiKey);
        var baseUrl = await RequireBaseUrlAsync(kind, request.BaseUrl, cancellationToken);
        var model = ModelOrNull(request.DefaultModel);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockOwnerAsync(connection, transaction, cancellationToken);
        Guid id;
        try
        {
            // "No default exists", not "no row exists": a user left without a default gets one on the next create.
            id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO ai_integration (owner_id, kind, label, base_url, credential, key_hint, default_model, is_default)
                VALUES (@owner, @kind, @label, @baseUrl, @credential, @hint, @model,
                        NOT EXISTS (SELECT 1 FROM ai_integration WHERE owner_id = @owner AND is_default AND archived_at IS NULL))
                RETURNING id
                """,
                new { owner = currentUser.Id, kind, label, baseUrl, credential = protector.Protect(key), hint = AiCredentialProtector.Hint(key), model },
                transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Conflict("Your integrations changed at the same moment. Try again.");
        }

        return await queries.GetAsync(id, cancellationToken);
    }

    // A null member is left as it is. An empty DefaultModel clears the saved model. A new key re-encrypts.
    public async Task<AiIntegrationDto> UpdateAsync(Guid id, UpdateAiIntegrationRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var kind = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT kind FROM ai_integration WHERE id = @id AND owner_id = @owner AND archived_at IS NULL",
            new { id, owner = currentUser.Id }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound(NotFoundMessage);

        var label = request.Label is null ? null : RequireLabel(request.Label);
        var key = request.ApiKey is null ? null : RequireKey(request.ApiKey);
        var baseUrl = request.BaseUrl is null ? null : await RequireBaseUrlAsync(kind, request.BaseUrl, cancellationToken);
        var clearModel = request.DefaultModel is not null && ModelOrNull(request.DefaultModel) is null;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ai_integration SET
                label = coalesce(@label, label),
                base_url = coalesce(@baseUrl, base_url),
                credential = coalesce(@credential, credential),
                key_hint = coalesce(@hint, key_hint),
                default_model = CASE WHEN @clearModel THEN NULL ELSE coalesce(@model, default_model) END
            WHERE id = @id AND owner_id = @owner AND archived_at IS NULL
            """,
            new
            {
                id, owner = currentUser.Id, label, baseUrl, clearModel, model = ModelOrNull(request.DefaultModel),
                credential = key is null ? null : protector.Protect(key), hint = key is null ? null : AiCredentialProtector.Hint(key),
            },
            cancellationToken: cancellationToken));
        return await queries.GetAsync(id, cancellationToken);
    }

    // Removing archives the row and blanks its key (#85). The default goes with it: the user's oldest remaining live
    // integration takes over, in the same transaction.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockOwnerAsync(connection, transaction, cancellationToken);
        var wasDefault = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT is_default FROM ai_integration WHERE id = @id AND owner_id = @owner AND archived_at IS NULL FOR UPDATE",
            new { id, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound(NotFoundMessage);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ai_integration SET archived_at = now(), credential = '', is_default = false WHERE id = @id",
            new { id }, transaction, cancellationToken: cancellationToken));
        if (wasDefault)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ai_integration SET is_default = true
                WHERE id = (SELECT id FROM ai_integration WHERE owner_id = @owner AND archived_at IS NULL ORDER BY created_at, id LIMIT 1)
                """,
                new { owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Two statements in one transaction: the partial unique index allows one default per user at every step.
    public async Task<AiSettingsDto> MakeDefaultAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockOwnerAsync(connection, transaction, cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM ai_integration WHERE id = @id AND owner_id = @owner AND archived_at IS NULL FOR UPDATE)",
            new { id, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound(NotFoundMessage);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ai_integration SET is_default = false WHERE owner_id = @owner AND is_default AND id <> @id",
            new { id, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ai_integration SET is_default = true WHERE id = @id AND owner_id = @owner",
            new { id, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await queries.ListAsync(cancellationToken);
    }

    // Try a connection before it is saved. A refused base URL is a 400; a provider that says no is Ok = false.
    public async Task<AiConnectionTestDto> TestAsync(TestAiConnectionRequest request, CancellationToken cancellationToken)
    {
        var kind = RequireKind(request.Kind);
        var key = RequireKey(request.ApiKey);
        var baseUrl = await RequireBaseUrlAsync(kind, request.BaseUrl, cancellationToken);
        return await ListModelsAsync(new AiConnection(kind, baseUrl is null ? null : new Uri(baseUrl), key), cancellationToken);
    }

    // Serialises Create, Delete and MakeDefault per user, so the one-default rule never sees two writers at once.
    private async Task LockOwnerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT 1 FROM app_user WHERE id = @owner FOR UPDATE", new { owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));

    // Test a saved integration with its stored key; the models are cached and last_error follows the outcome.
    public async Task<AiConnectionTestDto> TestSavedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SavedConnection>(new CommandDefinition(
            "SELECT kind AS Kind, base_url AS BaseUrl, credential AS Credential FROM ai_integration WHERE id = @id AND owner_id = @owner AND archived_at IS NULL",
            new { id, owner = currentUser.Id }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound(NotFoundMessage);

        var baseUrl = row.BaseUrl is null ? null : new Uri(row.BaseUrl);
        if (baseUrl is not null)
        {
            await guard.RequireAllowedAsync(baseUrl, cancellationToken);
        }

        var result = await ListModelsAsync(new AiConnection(row.Kind, baseUrl, protector.Unprotect(row.Credential)), cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ai_integration SET models = CASE WHEN @ok THEN @models ELSE models END, last_error = @error WHERE id = @id AND owner_id = @owner AND archived_at IS NULL",
            new { id, owner = currentUser.Id, ok = result.Ok, models = result.Models.ToArray(), error = result.Error },
            cancellationToken: cancellationToken));
        return result;
    }

    private async Task<AiConnectionTestDto> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            return new AiConnectionTestDto(true, await providers.ListModelsAsync(connection, cancellationToken), null);
        }
        catch (ApiException ex)
        {
            return new AiConnectionTestDto(false, [], ex.Message);
        }
    }

    private static string RequireKind(string? kind) =>
        kind is not null && AiIntegrationKinds.All.Contains(kind)
            ? kind
            : throw ApiException.Validation($"Choose a provider: {string.Join(", ", AiIntegrationKinds.All)}.");

    private static string RequireLabel(string? label)
    {
        var trimmed = label?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw ApiException.Validation("Give the integration a name.");
        }

        return trimmed.Length > MaxLabel ? throw ApiException.Validation($"The name can have at most {MaxLabel} characters.") : trimmed;
    }

    private static string RequireKey(string? apiKey)
    {
        var trimmed = apiKey?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw ApiException.Validation("Enter the API key.");
        }

        return trimmed.Length > MaxKey ? throw ApiException.Validation($"The API key can have at most {MaxKey} characters.") : trimmed;
    }

    private static string? ModelOrNull(string? model)
    {
        var trimmed = model?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxModel ? throw ApiException.Validation($"The model can have at most {MaxModel} characters.") : trimmed;
    }

    // Only an OpenAI-compatible gateway takes (and needs) a base URL. Returns it normalised, without a trailing slash.
    private async Task<string?> RequireBaseUrlAsync(string kind, string? baseUrl, CancellationToken cancellationToken)
    {
        var text = baseUrl?.Trim();
        if (kind != AiIntegrationKinds.OpenAiCompatible)
        {
            return string.IsNullOrEmpty(text) ? null : throw ApiException.Validation("Only an OpenAI-compatible integration takes a base URL.");
        }

        if (string.IsNullOrEmpty(text))
        {
            throw ApiException.Validation("Enter the base URL, such as https://api.openai.com/v1.");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw ApiException.Validation("The base URL isn't a web address. Use https://host/path.");
        }

        await guard.RequireAllowedAsync(uri, cancellationToken);
        return uri.AbsoluteUri.TrimEnd('/');
    }
}
