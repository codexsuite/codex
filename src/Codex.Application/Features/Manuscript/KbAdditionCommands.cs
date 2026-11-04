using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

// Codex additions (kb_progression rows). The row starts archived and the prose write brings it back, so the
// ::kb-addition line in the saved body is the only thing that places it; the scope trigger rejects an entry from
// outside the scene's book (a 400 from ProseStore), and a stale version leaves no row behind.
[RegisterSingleton]
public sealed class KbAdditionCommands(NpgsqlDataSource dataSource, IProseStore prose, IBookAccess access)
{
    private const string EditorOrigin = "editor";

    private sealed record AdditionRow(Guid Id, Guid SceneId, Guid EntryId, string Body);

    public async Task<BookKbAdditionsDto> ListAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AdditionRow>(new CommandDefinition(
            """
            SELECT k.id AS Id, k.scene_id AS SceneId, k.kb_entry_id AS EntryId, k.body AS Body
            FROM kb_progression k JOIN scene s ON s.id = k.scene_id
            WHERE s.book_id = @bookId AND k.archived_at IS NULL AND s.archived_at IS NULL
            ORDER BY k.created_at, k.id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return new BookKbAdditionsDto(bookId, rows.Select(r => new KbAdditionDto(r.Id, r.SceneId, r.EntryId, r.Body)).ToList());
    }

    public async Task<InsertKbAdditionResult> InsertAsync(Guid sceneId, InsertKbAdditionRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, "Scene not found.");
        var sceneBody = request.SceneBody ?? throw ApiException.Validation("Prose body is required.");
        if (!AdditionIds(sceneBody).Contains(request.AdditionId))
        {
            throw ApiException.Validation("The body has no ::kb-addition line for the new addition.");
        }

        var versions = await prose.UpdateManyAsync([(sceneId, request.Version, current => Conserved(current, sceneBody))], EditorOrigin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "SELECT EXISTS (SELECT 1 FROM kb_progression WHERE id = @id)", new { id = request.AdditionId }, transaction, cancellationToken: ct)))
                    {
                        throw ApiException.Conflict("An addition with that id already exists.");
                    }

                    await connection.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO kb_progression (id, kb_entry_id, scene_id, body, archived_at) SELECT @id, @entryId, id, @body, now() FROM scene WHERE id = @sceneId",
                        new { id = request.AdditionId, entryId = request.EntryId, sceneId, body = request.Body ?? "" }, transaction, cancellationToken: ct));
                },
            },
            cancellationToken);
        return new InsertKbAdditionResult(request.AdditionId, versions[0]);
    }

    public async Task<KbAdditionDto> PatchAsync(Guid additionId, PatchKbAdditionRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbAddition, additionId, BookRole.Write, cancellationToken, "Addition not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<AdditionRow>(new CommandDefinition(
                """
                UPDATE kb_progression SET body = coalesce(@body, body), kb_entry_id = coalesce(@entryId, kb_entry_id), updated_at = now()
                WHERE id = @additionId AND archived_at IS NULL
                RETURNING id AS Id, scene_id AS SceneId, kb_entry_id AS EntryId, body AS Body
                """,
                new { additionId, body = request.Body, entryId = request.EntryId }, cancellationToken: cancellationToken));
            return row is null ? throw ApiException.NotFound("Addition not found.") : new KbAdditionDto(row.Id, row.SceneId, row.EntryId, row.Body);
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.ForeignKeyViolation)
        {
            throw ApiException.Validation(e.MessageText);
        }
    }

    // The client sends the whole body, so the server checks that no embed present before has silently vanished.
    private static string Conserved(string current, string next)
    {
        var kept = Embeds(next).ToHashSet();
        return Embeds(current).All(kept.Contains) ? next : throw ApiException.Validation("The change would drop an embedded block (a beat, figure or addition) from the scene.");
    }

    private static IEnumerable<(ProseEmbedKind, Guid)> Embeds(string body) =>
        ProseDocument.Parse(body).ListEmbeds().Select(e => (e.Kind, e.Id));

    private static List<Guid> AdditionIds(string body) =>
        ProseDocument.Parse(body).ListEmbeds().Where(e => e.Kind == ProseEmbedKind.KbAddition).Select(e => e.Id).ToList();
}
