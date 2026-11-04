using Codex.Contracts.Errors;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// The shared steps of putting a row among its siblings: lock the parent, read the live siblings, pick a fractional key.
public static class SiblingKeys
{
    // Two moves into the same parent would otherwise pick the same key.
    public static Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid parentId, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@parentId::text, 0))",
            new { parentId }, transaction, cancellationToken: cancellationToken));

    public static async Task<List<Sibling>> SiblingsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid parentId, Guid movingId, CancellationToken cancellationToken)
        => (await connection.QueryAsync<Sibling>(new CommandDefinition(
            sql, new { parentId, movingId }, transaction, cancellationToken: cancellationToken))).ToList();

    public static string KeyAfter(List<Sibling> siblings, Guid? afterId)
    {
        if (afterId == null)
        {
            return FractionalKey.Between(null, siblings.Count > 0 ? siblings[0].Position : null);
        }
        var index = siblings.FindIndex(s => s.Id == afterId);
        if (index < 0)
        {
            throw ApiException.Validation("afterId must be another live item in the target.");
        }
        return FractionalKey.Between(siblings[index].Position, index + 1 < siblings.Count ? siblings[index + 1].Position : null);
    }
}
