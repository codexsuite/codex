using System.Data.Common;
using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Ai;

// Resolving a draft. Apply writes the output into the scene (a beat draft replaces the beat's prose) through IProseStore (origin `ai_apply`, which cuts the
// revision) and keeps the body it replaced on the generation, so Undo can put it back (origin `restore`).
[RegisterSingleton]
public sealed class DraftCommands(NpgsqlDataSource dataSource, IProseStore prose, DraftQueries queries, ICurrentUser currentUser, IBookAccess access)
{
    private const string PreApplyBody = "preApplyBody";

    // The uses the draft carries, as ids. ai_generation_resolve_uses archives or unlinks them when the draft is resolved,
    // so the ids are kept on the generation for Undo to give them back.
    private const string LiveSourceUses =
        "(SELECT coalesce(jsonb_agg(id), '[]'::jsonb) FROM reference_use WHERE generation_id = @generationId AND archived_at IS NULL)";

    private sealed record Row(Guid SceneId, Guid? BeatId, string Status, string? Output, string? PreApply);

    public async Task<DraftApplyResult> ApplyAsync(Guid generationId, DraftVersionRequest request, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(generationId, cancellationToken);
        if (row.Status != "pending")
        {
            throw ApiException.Conflict("This draft is no longer waiting to be applied.");
        }

        var text = (row.Output ?? "").Trim();
        if (text.Length == 0)
        {
            throw ApiException.Validation("The draft is empty.");
        }

        string? before = null;
        string? after = null;
        // The revision carries the generation, and the generation turns applied in the same transaction as the prose,
        // so a draft resolved meanwhile (another tab) leaves the scene untouched.
        var version = await prose.UpdateAsync(row.SceneId, current =>
        {
            before = current;
            after = Compose(current, row.BeatId, text);
            return after;
        }, request.Version, "ai_apply", new ProseWriteOptions
        {
            GenerationId = generationId,
            InTransaction = async (connection, transaction, ct) =>
            {
                // Apply replaces text, so the text it replaces must be in history: cut a revision of it, stamped just
                // before the ai_apply one, unless one already holds exactly that body.
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO prose_revision (scene_id, body, source, created_by, created_at)
                    SELECT @sceneId, @before, 'autosave', @userId, now() - interval '1 millisecond'
                    WHERE @before <> '' AND NOT EXISTS (SELECT 1 FROM prose_revision WHERE scene_id = @sceneId AND body = @before)
                    """,
                    new { sceneId = row.SceneId, before, userId = currentUser.Id }, transaction, cancellationToken: ct));
                var changed = await connection.ExecuteAsync(new CommandDefinition(
                    $"""
                    UPDATE ai_generation
                    SET status = 'applied', resolved_at = now(),
                        request = request || jsonb_build_object('preApplyBody', @before::text, 'sourceUses', {LiveSourceUses})
                    WHERE id = @generationId AND status = 'pending'
                    """,
                    new { generationId, before }, transaction, cancellationToken: ct));
                if (changed == 0)
                {
                    throw ApiException.Conflict("This draft is no longer waiting to be applied.");
                }
            },
        }, cancellationToken);
        return new DraftApplyResult(version, after!);
    }

    public async Task<DraftApplyResult> UndoApplyAsync(Guid generationId, DraftVersionRequest request, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(generationId, cancellationToken);
        if (row.Status != "applied" || row.PreApply == null)
        {
            throw ApiException.Conflict("This draft has not been applied.");
        }

        var restored = row.PreApply;
        var version = await prose.UpdateAsync(row.SceneId, _ => restored, request.Version, "restore", new ProseWriteOptions
        {
            InTransaction = (connection, transaction, ct) => SetStatusAsync(connection, transaction, generationId, "applied", "pending", ct),
        }, cancellationToken);
        return new DraftApplyResult(version, restored);
    }

    public async Task<DraftDto> DiscardAsync(Guid generationId, CancellationToken cancellationToken)
    {
        await LoadAsync(generationId, cancellationToken);
        await SetStatusAsync(generationId, "pending", "discarded", cancellationToken);
        return await queries.GetAsync(generationId, cancellationToken);
    }

    public async Task<DraftDto> UndoDiscardAsync(Guid generationId, CancellationToken cancellationToken)
    {
        await LoadAsync(generationId, cancellationToken);
        await SetStatusAsync(generationId, "discarded", "pending", cancellationToken);
        return await queries.GetAsync(generationId, cancellationToken);
    }

    // A beat draft replaces the prose under its ::beat line (up to the next beat); a continuation goes at the end of the scene.
    private static string Compose(string body, Guid? beatId, string text)
    {
        if (beatId is { } id)
        {
            try
            {
                return ProseDocument.Parse(body).ReplaceBeatProse(id, text).Text;
            }
            catch (ProseEditException e)
            {
                throw ApiException.Conflict(e.Message);
            }
        }

        var trimmed = body.TrimEnd('\n', ' ');
        return trimmed.Length == 0 ? text + "\n" : trimmed + "\n\n" + text + "\n";
    }

    private async Task<Row> LoadAsync(Guid generationId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Generation, generationId, BookRole.Write, cancellationToken, "Generation not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT scene_id AS SceneId, beat_id AS BeatId, status AS Status, output AS Output, request->>'{PreApplyBody}' AS PreApply FROM ai_generation WHERE id = @generationId",
            new { generationId }, cancellationToken: cancellationToken));
        return row ?? throw ApiException.NotFound("Generation not found.");
    }

    // Moves the row from one status to another; a row that is not in `from` (resolved twice, or by another tab) is a 409.
    // Discard keeps the ids of the draft's source uses on the row; going back to pending links them to the draft again.
    private async Task SetStatusAsync(Guid generationId, string from, string to, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetStatusAsync(connection, transaction, generationId, from, to, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SetStatusAsync(
        DbConnection connection, DbTransaction transaction, Guid generationId, string from, string to, CancellationToken cancellationToken)
    {
        if (to == "pending")
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE reference_use SET generation_id = @generationId, archived_at = NULL
                WHERE id IN (SELECT (jsonb_array_elements_text(request->'sourceUses'))::uuid
                             FROM ai_generation WHERE id = @generationId AND status = @from)
                """,
                new { generationId, from }, transaction, cancellationToken: cancellationToken));
        }

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE ai_generation
            SET status = @to, resolved_at = CASE WHEN @to = 'pending' THEN NULL ELSE now() END,
                request = CASE @to
                    WHEN 'pending' THEN request - 'preApplyBody' - 'sourceUses'
                    WHEN 'discarded' THEN request || jsonb_build_object('sourceUses', {LiveSourceUses})
                    ELSE request END
            WHERE id = @generationId AND status = @from
            """,
            new { generationId, from, to }, transaction, cancellationToken: cancellationToken));
        if (changed == 0)
        {
            throw ApiException.Conflict($"This draft is not {from}.");
        }
    }
}
