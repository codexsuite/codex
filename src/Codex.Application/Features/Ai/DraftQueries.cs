using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Ai;

[RegisterSingleton]
public sealed class DraftQueries(NpgsqlDataSource dataSource, IBookAccess access)
{
    internal const string Columns =
        """
        g.id AS Id, g.scene_id AS SceneId, g.beat_id AS BeatId, g.kind AS Kind, g.model AS Model,
        g.request->>'templateId' AS TemplateId, g.request->>'template' AS Template,
        (g.request->>'targetWords')::int AS TargetWords, (g.request->>'attempt')::int AS Attempt,
        g.status AS Status, g.output AS Text, markdown_word_count(coalesce(g.output, ''))::int AS Words,
        g.retry_of_id AS RetryOfId, g.input_tokens AS InputTokens, g.output_tokens AS OutputTokens,
        g.integration_id AS IntegrationId
        """;

    // The latest generation of each live beat (and of each scene's continuation), when it is waiting for Apply or Discard.
    public async Task<BookDraftsDto> ListAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DraftRow>(new CommandDefinition(
            $"""
            SELECT * FROM (
                SELECT DISTINCT ON (coalesce(g.beat_id, g.scene_id)) {Columns}
                FROM ai_generation g
                JOIN scene s ON s.id = g.scene_id AND s.archived_at IS NULL
                LEFT JOIN beat b ON b.id = g.beat_id
                WHERE g.book_id = @bookId AND (g.beat_id IS NULL OR b.archived_at IS NULL)
                ORDER BY coalesce(g.beat_id, g.scene_id), g.created_at DESC, g.id DESC
            ) latest
            WHERE latest.Status = 'pending'
            ORDER BY latest.Id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return new BookDraftsDto(bookId, rows.Select(r => r.ToDto()).ToList());
    }

    public async Task<DraftDto> GetAsync(Guid generationId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Generation, generationId, BookRole.View, cancellationToken, "Generation not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition(
            $"SELECT {Columns} FROM ai_generation g WHERE g.id = @generationId", new { generationId }, cancellationToken: cancellationToken));
        return (row ?? throw ApiException.NotFound("Generation not found.")).ToDto();
    }

    // What a generation sent, from its stored request snapshot (so a later template edit never changes it).
    public async Task<DraftSentDto> SentAsync(Guid generationId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Generation, generationId, BookRole.View, cancellationToken, "Generation not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SentRow>(new CommandDefinition(
            """
            SELECT g.model AS Model, g.request::text AS Request, g.input_tokens AS InputTokens, g.output_tokens AS OutputTokens
            FROM ai_generation g WHERE g.id = @generationId
            """,
            new { generationId }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound("Generation not found.");
        using var json = System.Text.Json.JsonDocument.Parse(row.Request);
        var root = json.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        var context = root.TryGetProperty("context", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Array
            ? c.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
            : [];
        return new DraftSentDto(
            Text("template"), row.Model, Text("integration"), context, row.InputTokens, row.OutputTokens, Text("instruction") ?? "", Text("prompt") ?? "");
    }

    private sealed record SentRow(string Model, string Request, int? InputTokens, int? OutputTokens);
}
