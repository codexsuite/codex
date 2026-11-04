using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Prompts;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Prompts;

// Settings -> AI, Prompt templates (J22): the signed-in user's own templates; Delete archives (#85), and an archived one is not_found. Every row is the user's own (`user_id`);
// another user's id is not_found. Built-in templates cannot be changed, only duplicated.
[RegisterSingleton]
public sealed class PromptTemplateCommands(NpgsqlDataSource dataSource, IPromptCatalogue catalogue, ICurrentUser currentUser)
{
    public const int MaxName = 100;
    public const int MaxDescription = 300;
    public const int MaxBody = 20000;

    public async Task<AiTemplatesDto> GetAsync(CancellationToken cancellationToken)
    {
        var built = catalogue.Get();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var templates = await connection.QueryAsync<(Guid Id, string Name, string Description, string Body)>(new CommandDefinition(
            "SELECT id, name, description, body FROM prompt_template WHERE user_id = @userId AND archived_at IS NULL ORDER BY lower(name), id",
            new { userId = currentUser.Id }, cancellationToken: cancellationToken));
        return new AiTemplatesDto(
            [.. built.Templates.Select(t => new AiTemplateDto(t.Id, t.Name, t.Description, catalogue.FindInstruction(t.Id) ?? "", true)),
             .. templates.Select(t => new AiTemplateDto(t.Id.ToString(), t.Name, t.Description, t.Body, false))],
            PromptVariables.All);
    }

    public async Task<AiTemplateDto> CreateTemplateAsync(SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        var (name, description, body) = CleanTemplate(request);
        return await InsertTemplateAsync(name, description, body, cancellationToken);
    }

    public async Task<AiTemplateDto> UpdateTemplateAsync(string templateId, SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        var id = UserTemplateId(templateId);
        var (name, description, body) = CleanTemplate(request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Name, string Description, string Body)?>(new CommandDefinition(
                """
                UPDATE prompt_template SET name = @name, description = @description, body = @body, updated_at = now()
                WHERE id = @id AND user_id = @userId AND archived_at IS NULL
                RETURNING id, name, description, body
                """,
                new { id, userId = currentUser.Id, name, description, body }, cancellationToken: cancellationToken));
            return row is { } r ? new AiTemplateDto(r.Id.ToString(), r.Name, r.Description, r.Body, false) : throw ApiException.NotFound("Template not found.");
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Conflict("You already have a template with that name.");
        }
    }

    public async Task DeleteTemplateAsync(string templateId, CancellationToken cancellationToken)
    {
        var id = UserTemplateId(templateId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE prompt_template SET archived_at = now() WHERE id = @id AND user_id = @userId AND archived_at IS NULL", new { id, userId = currentUser.Id }, cancellationToken: cancellationToken));
        if (deleted == 0)
        {
            throw ApiException.NotFound("Template not found.");
        }
    }

    // Copies a built-in template (its instruction, then the variables a draft needs) or one of the user's own.
    public async Task<AiTemplateDto> DuplicateTemplateAsync(string templateId, DuplicateTemplateRequest request, CancellationToken cancellationToken)
    {
        string description;
        string body;
        if (Guid.TryParse(templateId, out var id))
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<(string Description, string Body)?>(new CommandDefinition(
                "SELECT description, body FROM prompt_template WHERE id = @id AND user_id = @userId AND archived_at IS NULL",
                new { id, userId = currentUser.Id }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound("Template not found.");
            (description, body) = row;
        }
        else
        {
            var template = catalogue.Get().Templates.FirstOrDefault(t => t.Id == templateId) ?? throw ApiException.NotFound("Template not found.");
            description = template.Description;
            body = PromptVariables.Skeleton(catalogue.FindInstruction(templateId) ?? "");
        }

        var (name, _, _) = CleanTemplate(new SaveTemplateRequest(request.Name, description, body));
        return await InsertTemplateAsync(name, description, body, cancellationToken);
    }

    private async Task<AiTemplateDto> InsertTemplateAsync(string name, string description, string body, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO prompt_template (user_id, name, description, body) VALUES (@userId, @name, @description, @body) RETURNING id",
                new { userId = currentUser.Id, name, description, body }, cancellationToken: cancellationToken));
            return new AiTemplateDto(id.ToString(), name, description, body, false);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Conflict("You already have a template with that name.");
        }
    }

    private (string Name, string Description, string Body) CleanTemplate(SaveTemplateRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxName)
        {
            throw ApiException.Validation($"Give the template a name of up to {MaxName} characters.");
        }

        if (catalogue.Get().Templates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw ApiException.Conflict("A built-in template already has that name.");
        }

        var description = request.Description?.Trim() ?? "";
        if (description.Length > MaxDescription)
        {
            throw ApiException.Validation($"The description can be up to {MaxDescription} characters.");
        }

        var body = request.Body?.Trim() ?? "";
        if (body.Length == 0)
        {
            throw ApiException.Validation("The prompt cannot be empty.");
        }

        if (body.Length > MaxBody)
        {
            throw ApiException.Validation($"The prompt can be up to {MaxBody} characters.");
        }

        return (name, description, body);
    }

    // The id of one of the user's templates; a built-in one is refused with the way out.
    private Guid UserTemplateId(string templateId)
    {
        if (Guid.TryParse(templateId, out var id))
        {
            return id;
        }

        throw catalogue.Get().Templates.Any(t => t.Id == templateId)
            ? ApiException.Validation("Built-in templates cannot be changed. Duplicate it, then edit the copy.")
            : ApiException.NotFound("Template not found.");
    }
}
