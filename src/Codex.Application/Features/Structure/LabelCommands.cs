using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Labels belong to a book or a series (label.book_id) and are visible to every book in that scope.
// Deleting a label archives it (#85): it leaves every scene that carries it, the scene_label rows stay, and its name
// is free again. The client confirms first,
// using LabelDto.SceneCount. StructureChanged "label" goes out under the label's owning book or series id.
[RegisterSingleton]
public sealed class LabelCommands(NpgsqlDataSource dataSource, IBookAccess access, IBookEvents events)
{
    private const int MaxNameLength = 80;
    private const int MaxColorLength = 40;

    private const string LabelSelect =
        """
        SELECT l.id, l.book_id AS ScopeId, l.name, l.color,
               (SELECT count(*) FROM scene_label sl WHERE sl.label_id = l.id AND sl.archived_at IS NULL)::int AS SceneCount
        FROM label l
        """;

    public async Task<IReadOnlyList<LabelDto>> ListAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, notFound: "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var labels = await connection.QueryAsync<LabelDto>(new CommandDefinition(
            LabelSelect + " JOIN book_scope bs ON bs.scope_id = l.book_id WHERE bs.book_id = @bookId AND l.archived_at IS NULL ORDER BY l.position, l.id",
            new { bookId }, cancellationToken: cancellationToken));
        return labels.ToList();
    }

    public async Task<LabelDto> CreateAsync(Guid bookId, CreateLabelRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, notFound: "Book not found.");
        var name = CleanName(request.Name);
        var color = CleanColor(request.Color);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Guid id;
        try
        {
            // Position sorts after every existing label: the longest-so-far key plus a character.
            id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO label (book_id, name, color, position)
                VALUES (@bookId, @name, @color,
                        coalesce((SELECT max(position) FROM label WHERE book_id = @bookId AND archived_at IS NULL), '') || 'n')
                RETURNING id
                """,
                new { bookId, name, color }, cancellationToken: cancellationToken));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Conflict($"A label named \"{name}\" already exists.");
        }

        await events.PublishAsync(new StructureChanged(bookId, "label"));
        return await GetAsync(connection, id, cancellationToken);
    }

    public async Task<LabelDto> PatchAsync(Guid labelId, PatchLabelRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Label, labelId, BookRole.Write, cancellationToken, notFound: "Label not found.");
        if (request.Name == null && request.Color == null)
        {
            throw ApiException.Validation("Nothing to change: give a name or color.");
        }
        var name = request.Name == null ? null : CleanName(request.Name);
        var color = CleanColor(request.Color);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Guid? scopeId;
        try
        {
            scopeId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                """
                UPDATE label SET
                    name  = coalesce(@name::text, name),
                    color = CASE WHEN @color::text IS NULL THEN color ELSE nullif(@color::text, '') END
                WHERE id = @labelId AND archived_at IS NULL
                RETURNING book_id
                """,
                new { labelId, name, color }, cancellationToken: cancellationToken));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Conflict($"A label named \"{name}\" already exists.");
        }
        if (scopeId == null)
        {
            throw ApiException.NotFound("Label not found.");
        }

        await events.PublishAsync(new StructureChanged(scopeId.Value, "label"));
        return await GetAsync(connection, labelId, cancellationToken);
    }

    public async Task DeleteAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Label, labelId, BookRole.Write, cancellationToken, notFound: "Label not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var scopeId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "UPDATE label SET archived_at = now() WHERE id = @labelId AND archived_at IS NULL RETURNING book_id", new { labelId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Label not found.");

        await events.PublishAsync(new StructureChanged(scopeId, "label"));
    }

    private static Task<LabelDto> GetAsync(NpgsqlConnection connection, Guid labelId, CancellationToken cancellationToken)
        => connection.QuerySingleAsync<LabelDto>(new CommandDefinition(
            LabelSelect + " WHERE l.id = @labelId", new { labelId }, cancellationToken: cancellationToken));

    private static string CleanName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            throw ApiException.Validation($"A label name is 1 to {MaxNameLength} characters.");
        }
        return trimmed;
    }

    private static string? CleanColor(string? color)
    {
        var trimmed = color?.Trim();
        if (trimmed is { Length: > MaxColorLength })
        {
            throw ApiException.Validation($"A label color is at most {MaxColorLength} characters.");
        }
        return trimmed;
    }
}
