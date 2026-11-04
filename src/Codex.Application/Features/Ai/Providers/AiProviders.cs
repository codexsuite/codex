using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Ai;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Ai.Providers;

// Picks the provider implementation for a connection's kind. Anthropic keeps its SDK-based generator (constructed as
// before); the two Chat Completions kinds share ChatCompletionsProseGenerator. Stateless, so one instance serves all users.
public sealed class AiProviders : IAiProviders
{
    private readonly IHttpClientFactory _clients;
    private readonly IAiEndpointGuard _guard;
    private readonly ILoggerFactory _loggers;
    private readonly ChatCompletionsLimits _limits;
    private readonly AiModelLister _models;

    public AiProviders(IHttpClientFactory clients, IAiEndpointGuard guard, ILoggerFactory loggers, ChatCompletionsLimits? limits = null)
    {
        _clients = clients;
        _guard = guard;
        _loggers = loggers;
        _limits = limits ?? ChatCompletionsLimits.Default;
        _models = new AiModelLister(clients, guard, loggers.CreateLogger<AiModelLister>(), _limits);
    }

    public IProseGenerator Generator(AiConnection connection) => connection.Kind switch
    {
        AiIntegrationKinds.OpenAiCompatible or AiIntegrationKinds.OpenRouter => new ChatCompletionsProseGenerator(
            _clients, _guard, connection, _loggers.CreateLogger<ChatCompletionsProseGenerator>(), _limits),
        AiIntegrationKinds.Anthropic => new AnthropicProseGenerator(_clients, connection.ApiKey, _loggers.CreateLogger<AnthropicProseGenerator>()),
        _ => throw ApiException.Validation($"'{connection.Kind}' is not a known AI integration kind."),
    };

    public Task<IReadOnlyList<string>> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken) =>
        connection.Kind is AiIntegrationKinds.OpenAiCompatible or AiIntegrationKinds.OpenRouter or AiIntegrationKinds.Anthropic
            ? _models.ListAsync(connection, cancellationToken)
            : throw ApiException.Validation($"'{connection.Kind}' is not a known AI integration kind.");
}
