using System.Text.RegularExpressions;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

// A source's cite key and permissions contact, which the details form does not carry. Unknown or archived source:
// ApiException.NotFound. `provenance` says who the History line is written as (null: the author).
public interface ISourceExtrasCommands
{
    Task<SourceExtrasDto> GetAsync(Guid sourceId, CancellationToken cancellationToken);

    // Validation (400) for a cite key with spaces or BibTeX punctuation, one already used in the book, or text over the limits.
    Task<SourceExtrasDto> SetAsync(Guid sourceId, SourceExtrasRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);
}

[RegisterSingleton]
public sealed partial class SourceExtrasCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access) : ISourceExtrasCommands
{
    private const int MaxCiteKey = 100;
    private const int MaxContact = 500;

    [GeneratedRegex("""^[^\s,{}"#%=~\\]+$""")]
    private static partial Regex CiteKeyPattern();

    public async Task<SourceExtrasDto> GetAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Source, sourceId, BookRole.View, cancellationToken, $"Source {sourceId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, null, sourceId, false, cancellationToken);
    }

    public async Task<SourceExtrasDto> SetAsync(Guid sourceId, SourceExtrasRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.Source, sourceId, BookRole.Write, cancellationToken, $"Source {sourceId} was not found.");
        var key = request.CiteKey is null ? null : Clean(request.CiteKey);
        var contact = request.PermissionsContact is null ? null : Clean(request.PermissionsContact);
        if (key is { Length: > MaxCiteKey } || (key != null && !CiteKeyPattern().IsMatch(key)))
        {
            throw ApiException.Validation($"A cite key is one word of up to {MaxCiteKey} characters, with no spaces, commas, braces or quotes.");
        }

        if (contact is { Length: > MaxContact })
        {
            throw ApiException.Validation($"The permissions contact is longer than {MaxContact} characters.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadAsync(connection, transaction, sourceId, true, cancellationToken);
        var after = new SourceExtrasDto(request.CiteKey is null ? before.CiteKey : key, request.PermissionsContact is null ? before.PermissionsContact : contact);
        var changed = new List<string>();
        if (!string.Equals(before.CiteKey, after.CiteKey, StringComparison.Ordinal))
        {
            changed.Add("cite key");
        }

        if (!string.Equals(before.PermissionsContact, after.PermissionsContact, StringComparison.Ordinal))
        {
            changed.Add("permissions contact");
        }

        if (changed.Count == 0)
        {
            return before;
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE reference SET cite_key = @CiteKey, permissions_contact = @PermissionsContact WHERE id = @sourceId",
                new { sourceId, after.CiteKey, after.PermissionsContact }, transaction, cancellationToken: cancellationToken));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Validation($"Another source in this book already has the cite key '{after.CiteKey}'.");
        }

        await SourceEvents.RecordAsync(connection, transaction, sourceId, currentUser.Id, SourceEvents.DetailsEdited,
            new { fields = string.Join(", ", changed) }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string Select = "SELECT cite_key AS CiteKey, permissions_contact AS PermissionsContact FROM reference WHERE id = @sourceId AND archived_at IS NULL";

    private static async Task<SourceExtrasDto> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid sourceId, bool forUpdate, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<SourceExtrasDto>(new CommandDefinition(
            forUpdate ? Select + " FOR UPDATE" : Select, new { sourceId }, transaction, cancellationToken: ct))
        ?? throw ApiException.NotFound($"Source {sourceId} was not found.");
}
