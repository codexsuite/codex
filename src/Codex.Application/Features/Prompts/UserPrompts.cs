using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Prompts;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Prompts;

// The prompt templates the signed-in user can pick from: the built-in ones (files under prompts/) plus their own rows.
// Models are not here: they come from the user's AI integrations (GET /api/ai/models); the catalogue's models are only
// label suggestions. Generation resolves the template a request names through IPromptTemplates (both registered by
// PromptsServices).
public sealed class UserPrompts(NpgsqlDataSource dataSource, IPromptCatalogue catalogue, ICurrentUser currentUser) : IPromptTemplates
{
    private sealed record TemplateRow(Guid Id, string Name, string Description, string Body);

    // GET /api/prompts: the draft picker's lists.
    public async Task<PromptsDto> GetAsync(CancellationToken cancellationToken)
    {
        var built = catalogue.Get();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var templates = await connection.QueryAsync<(Guid Id, string Name, string Description)>(new CommandDefinition(
            "SELECT id, name, description FROM prompt_template WHERE user_id = @userId AND archived_at IS NULL ORDER BY lower(name), id",
            new { userId = currentUser.Id }, cancellationToken: cancellationToken));
        return built with
        {
            Templates = [.. built.Templates, .. templates.Select(t => new PromptTemplateDto(t.Id.ToString(), t.Name, t.Description, false))],
        };
    }

    public async Task<ResolvedTemplate> ResolveAsync(string templateId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(templateId, out var id))
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<TemplateRow>(new CommandDefinition(
                "SELECT id, name, description, body FROM prompt_template WHERE id = @id AND user_id = @userId AND archived_at IS NULL",
                new { id, userId = currentUser.Id }, cancellationToken: cancellationToken));
            return row == null
                ? throw ApiException.Validation($"Unknown template '{templateId}'.")
                : new ResolvedTemplate(row.Id.ToString(), row.Name, row.Body, BuiltIn: false);
        }

        var instruction = catalogue.FindInstruction(templateId) ?? throw ApiException.Validation($"Unknown template '{templateId}'.");
        return new ResolvedTemplate(templateId, catalogue.Get().Templates.First(t => t.Id == templateId).Name, instruction, BuiltIn: true);
    }
}
