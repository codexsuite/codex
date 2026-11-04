using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Prompts;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

// A scene's version history (J15): the list of revisions the prose store cut, and Restore, which is an ordinary prose
// write (origin "restore") so it cuts its own revision and history only grows.
[RegisterSingleton]
public sealed class ProseHistory(
    NpgsqlDataSource dataSource, IBookAccess access, IProseStore prose, IPromptCatalogue prompts, TimeProvider clock)
{
    private const int MaxRows = 50;
    private const int RestoreAttempts = 3;

    private sealed record RevisionRow(
        Guid Id, string Source, string Body, int Words, DateTimeOffset CreatedAt, string? ClientName, string? Model, int? Attempt);

    public async Task<ProseHistoryDto> ListAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.View, cancellationToken, "Scene not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var archived = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT archived_at IS NOT NULL FROM scene WHERE id = @sceneId", new { sceneId }, cancellationToken: cancellationToken));
        var current = await prose.GetAsync(sceneId, cancellationToken);
        var rows = (await connection.QueryAsync<RevisionRow>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.source AS Source, r.body AS Body, r.word_count AS Words, r.created_at AS CreatedAt,
                   r.client_name AS ClientName, g.model AS Model, (g.request->>'attempt')::int AS Attempt
            FROM prose_revision r
            LEFT JOIN ai_generation g ON g.id = r.generation_id
            WHERE r.scene_id = @sceneId
            ORDER BY r.created_at DESC, r.id DESC
            LIMIT @limit
            """,
            new { sceneId, limit = MaxRows }, cancellationToken: cancellationToken))).ToList();

        // The row whose text is what the scene holds now: the newest with that exact body.
        var currentId = rows.FirstOrDefault(r => r.Body == current.Body)?.Id;
        var revisions = rows.Select(r => new ProseRevisionDto(
            r.Id, r.Source, LabelFor(r.Source, r.ClientName), DetailFor(r),
            r.CreatedAt, DisplayDate.Relative(r.CreatedAt, clock), DisplayDate.Clock(r.CreatedAt, clock),
            r.Words, r.Body, r.Id == currentId)).ToList();
        var currentWords = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT markdown_word_count(@body)", new { body = current.Body }, cancellationToken: cancellationToken));
        return new ProseHistoryDto(sceneId, archived, current.Version, currentWords, current.Body, revisions);
    }

    // Puts the revision's text in the scene. The text it replaces is kept first (as the revision it already is, or as a
    // new one), and its id comes back so Undo is a restore of it.
    public async Task<RestoreRevisionResult> RestoreAsync(Guid sceneId, Guid revisionId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, "Scene not found.");
        string body;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT archived_at IS NOT NULL FROM scene WHERE id = @sceneId", new { sceneId }, cancellationToken: cancellationToken)))
            {
                throw ApiException.Blocked("An archived scene's history is read-only. Restore the scene first.");
            }

            body = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT body FROM prose_revision WHERE id = @revisionId AND scene_id = @sceneId",
                new { revisionId, sceneId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Revision not found.");
        }

        for (var attempt = 1; attempt <= RestoreAttempts; attempt++)
        {
            var current = await prose.GetAsync(sceneId, cancellationToken);
            if (current.Body == body)
            {
                throw ApiException.Validation("This is already the current version.");
            }

            var previousId = Guid.Empty;
            try
            {
                var version = await prose.UpdateAsync(
                    sceneId, _ => body, current.Version, "restore",
                    new ProseWriteOptions
                    {
                        InTransaction = async (connection, transaction, ct) =>
                            previousId = await KeepPreviousAsync(connection, transaction, sceneId, current.Body, ct),
                    },
                    cancellationToken);
                return new RestoreRevisionResult(version, body, previousId);
            }
            catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
            {
                // Saved to meanwhile: read it again, so the text this restore replaces is the newest.
            }
        }

        throw ApiException.Conflict("The scene keeps changing. Try again.");
    }

    public static async Task<Guid> KeepPreviousAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, Guid sceneId, string body, CancellationToken cancellationToken)
    {
        // The restore revision was just written, so it is skipped: it holds the new text, which differs from `body`.
        var existing = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM prose_revision WHERE scene_id = @sceneId AND body = @body ORDER BY created_at DESC, id DESC LIMIT 1",
            new { sceneId, body }, transaction, cancellationToken: cancellationToken));
        if (existing is { } id)
        {
            return id;
        }

        // Backdated by a millisecond so the list keeps it just below the restore revision cut by this transaction.
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO prose_revision (scene_id, body, source, created_at)
            VALUES (@sceneId, @body, 'manual', now() - interval '1 millisecond') RETURNING id
            """,
            new { sceneId, body }, transaction, cancellationToken: cancellationToken));
    }

    private static string LabelFor(string source, string? clientName) => source switch
    {
        "import" => "Imported",
        "ai_apply" => "AI draft applied",
        "restore" => "Restored",
        "agent" => string.IsNullOrWhiteSpace(clientName) ? "Edited by an agent" : $"Edited by {clientName}",
        "replaced" => "Replaced by another session",
        _ => "Edited",
    };

    private string? DetailFor(RevisionRow row)
    {
        if (row.Source != "ai_apply" || row.Model is null)
        {
            return null;
        }

        var label = prompts.Get().Models.FirstOrDefault(m => m.Id == row.Model)?.Label ?? row.Model;
        return $"{label} · attempt {row.Attempt ?? 1}";
    }
}
