using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

[RegisterSingleton]
public sealed class SceneCommands(NpgsqlDataSource dataSource, IBookAccess access, OutlineQuery outline, IBookEvents events)
{
    private const int MaxTitleLength = 200;

    private static readonly HashSet<string> Statuses = ["empty", "outline", "beats", "draft", "revised", "final"];

    // An archived scene, or one in an archived act, is not on the board: 404 before anything is written.
    public async Task<OutlineSceneDto> PatchAsync(Guid sceneId, PatchSceneRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, notFound: "Scene not found.");
        if (request.Summary == null && request.Title == null && request.Status == null && request.PlanNotes == null)
        {
            throw ApiException.Validation("Nothing to change: give a summary, title, status or plan notes.");
        }
        if (request.Status != null && !Statuses.Contains(request.Status))
        {
            throw ApiException.Validation($"Status must be one of {string.Join(", ", Statuses.Order())}.");
        }
        var title = request.Title?.Trim();
        if (title is { Length: > MaxTitleLength })
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            UPDATE scene SET
                summary = CASE WHEN @summary::text IS NULL THEN summary ELSE nullif(@summary::text, '') END,
                title   = CASE WHEN @title::text   IS NULL THEN title   ELSE nullif(@title::text, '')   END,
                status  = coalesce(@status::text, status),
                plan_notes = CASE WHEN @planNotes::text IS NULL THEN plan_notes ELSE nullif(@planNotes::text, '') END
            WHERE id = @sceneId AND archived_at IS NULL AND NOT EXISTS (
                SELECT 1 FROM chapter c JOIN act a ON a.id = c.act_id
                WHERE c.id = scene.chapter_id AND a.archived_at IS NOT NULL)
            RETURNING book_id
            """,
            new { sceneId, summary = request.Summary, title, status = request.Status, planNotes = request.PlanNotes }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Scene not found.");

        await events.PublishAsync(new StructureChanged(bookId, "scene"));
        return await outline.GetSceneAsync(sceneId, cancellationToken);
    }

    // Replaces the scene's label set. Returns the labels now on the scene.
    public async Task<IReadOnlyList<LabelRefDto>> SetLabelsAsync(Guid sceneId, SetSceneLabelsRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, notFound: "Scene not found.");
        var labelIds = request.LabelIds?.Distinct().ToArray() ?? throw ApiException.Validation("labelIds is required.");

        Guid bookId;
        List<LabelRefDto> labels;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM scene WHERE id = @sceneId AND archived_at IS NULL FOR UPDATE",
                new { sceneId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Scene not found.");
            try
            {
                // A label that was deleted (archived) can't be added; one the scene already has is archived, not removed.
                if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                        "SELECT count(*)::int FROM label WHERE id = ANY(@labelIds) AND archived_at IS NULL",
                        new { labelIds }, transaction, cancellationToken: cancellationToken)) != labelIds.Length)
                {
                    throw ApiException.Validation("Every label must exist and belong to this book or its series.");
                }
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE scene_label SET archived_at = now() WHERE scene_id = @sceneId AND label_id <> ALL (@labelIds) AND archived_at IS NULL",
                    new { sceneId, labelIds }, transaction, cancellationToken: cancellationToken));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO scene_label (scene_id, label_id) SELECT @sceneId, unnest(@labelIds) ON CONFLICT (scene_id, label_id) DO UPDATE SET archived_at = NULL",
                    new { sceneId, labelIds }, transaction, cancellationToken: cancellationToken));
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                // A label that does not exist, or one outside the book's scope (the schema's scope trigger).
                throw ApiException.Validation("Every label must exist and belong to this book or its series.");
            }
            labels = (await connection.QueryAsync<LabelRefDto>(new CommandDefinition(
                """
                SELECT l.id, l.name, l.color FROM scene_label sl JOIN label l ON l.id = sl.label_id AND l.archived_at IS NULL
                WHERE sl.scene_id = @sceneId AND sl.archived_at IS NULL ORDER BY l.position, l.id
                """,
                new { sceneId }, transaction, cancellationToken: cancellationToken))).ToList();
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "label"));
        return labels;
    }
}
