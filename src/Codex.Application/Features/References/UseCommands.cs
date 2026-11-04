using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

// Reading a use, archived or not, and setting where in the source it points. A use of another user's book is not
// found (404). `provenance` says who the History line is written as (null: the author).
public interface IUseCommands
{
    Task<UseInfoDto> GetAsync(Guid useId, CancellationToken cancellationToken);

    // Validation (400) for a locator over 200 characters; not found for an archived use. Writes a History line when the locator changes.
    Task<UseInfoDto> SetLocatorAsync(Guid useId, SetUseLocatorRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);
}

[RegisterSingleton]
public sealed class UseCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access) : IUseCommands
{
    private const int MaxLocator = 200;

    public async Task<UseInfoDto> GetAsync(Guid useId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.ReferenceUse, useId, BookRole.View, cancellationToken, $"Use {useId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, null, useId, false, cancellationToken);
    }

    public async Task<UseInfoDto> SetLocatorAsync(Guid useId, SetUseLocatorRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.ReferenceUse, useId, BookRole.Write, cancellationToken, $"Use {useId} was not found.");
        var locator = string.IsNullOrWhiteSpace(request.Locator) ? null : request.Locator.Trim();
        if (locator is { Length: > MaxLocator })
        {
            throw ApiException.Validation($"The locator is longer than {MaxLocator} characters.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await ReadAsync(connection, transaction, useId, true, cancellationToken);
        if (current.Archived)
        {
            throw ApiException.NotFound($"Use {useId} was not found.");
        }

        if (!string.Equals(current.Locator, locator, StringComparison.Ordinal))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE reference_use SET locator = @locator, updated_at = now() WHERE id = @useId", new { useId, locator }, transaction, cancellationToken: cancellationToken));
            await SourceEvents.RecordAsync(connection, transaction, current.SourceId, currentUser.Id, SourceEvents.UseLocatorChanged,
                new { from = current.Locator ?? "none", to = locator ?? "none" }, cancellationToken, provenance);
        }

        await transaction.CommitAsync(cancellationToken);
        return current with { Locator = locator };
    }

    private const string Select =
        """
        SELECT id AS Id, book_id AS BookId, reference_id AS SourceId, scene_id AS SceneId, kind AS Kind, locator AS Locator,
               archived_at IS NOT NULL AS Archived, generation_id IS NOT NULL AS Pending
        FROM reference_use WHERE id = @useId
        """;

    private static async Task<UseInfoDto> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid useId, bool forUpdate, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<UseInfoDto>(new CommandDefinition(
            forUpdate ? Select + " FOR UPDATE" : Select, new { useId }, transaction, cancellationToken: ct))
        ?? throw ApiException.NotFound($"Use {useId} was not found.");
}
