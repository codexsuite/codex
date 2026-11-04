using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbQueries
{
    Task<KbDto> GetBookKbAsync(Guid bookId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class KbQueries(NpgsqlDataSource dataSource, IBookAccess access) : IKbQueries
{
    private sealed record CategoryRow(Guid Id, string Name, string? Tag, string? Colour, string Position, string FieldDefs, bool FromSeries,
        string? SeriesName);

    private sealed record EntryRow(
        Guid Id, Guid CategoryId, string Name, string Aliases, int SceneCount, int UseCount, bool FromSeries,
        string? SeriesName, bool AlwaysInContext, string? Summary);

    public async Task<KbDto> GetBookKbAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var categories = (await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT coalesce(bc.own_id, bc.series_id) AS Id, bc.name, bc.tag, bc.color AS Colour,
                   bc.position, c.field_defs::text AS FieldDefs, bc.own_id IS NULL AS FromSeries,
                   CASE WHEN bc.own_id IS NULL THEN owner.title END AS SeriesName
            FROM book_kb_category bc
            JOIN kb_category c ON c.id = coalesce(bc.own_id, bc.series_id)
            JOIN book owner ON owner.id = c.book_id
            WHERE bc.book_id = @bookId
            ORDER BY bc.position COLLATE "C", bc.name, 1
            """,
            new { bookId }, cancellationToken: cancellationToken)))
            .Select(r => new KbCategoryDto(r.Id, r.Name, r.Tag, r.Colour, r.Position, ParseFieldDefs(r.FieldDefs), r.FromSeries, r.SeriesName))
            .ToList();

        var entries = (await connection.QueryAsync<EntryRow>(new CommandDefinition(
            """
            SELECT e.id, coalesce(bc.own_id, bc.series_id) AS CategoryId, e.name, to_json(e.aliases)::text AS Aliases,
                   coalesce(u.scene_count, 0)::integer AS SceneCount, coalesce(u.use_count, 0)::integer AS UseCount,
                   e.book_id <> @bookId AS FromSeries,
                   CASE WHEN e.book_id <> @bookId THEN owner.title END AS SeriesName,
                   e.always_in_context AS AlwaysInContext, e.summary AS Summary
            FROM book_scope bs
            JOIN kb_entry e ON e.book_id = bs.scope_id AND e.archived_at IS NULL
            JOIN kb_category c ON c.id = e.category_id
            JOIN book_kb_category bc ON bc.book_id = bs.book_id AND bc.name = c.name
            JOIN book owner ON owner.id = e.book_id
            LEFT JOIN (
                SELECT ku.kb_entry_id, count(DISTINCT ku.scene_id) AS scene_count, sum(ku.occurrences) AS use_count
                FROM kb_entry_use ku WHERE ku.book_id = @bookId
                GROUP BY ku.kb_entry_id
            ) u ON u.kb_entry_id = e.id
            WHERE bs.book_id = @bookId
            ORDER BY bc.position COLLATE "C", e.position COLLATE "C", e.id
            """,
            new { bookId }, cancellationToken: cancellationToken)))
            .Select(r => new KbEntrySummaryDto(
                r.Id, r.CategoryId, r.Name, JsonSerializer.Deserialize<List<string>>(r.Aliases, JsonSerializerOptions.Web) ?? [], r.SceneCount, r.UseCount, r.FromSeries, r.SeriesName,
                r.AlwaysInContext, SummaryLine(r.Summary)))
            .ToList();

        return new KbDto(categories, entries);
    }

    private const int SummaryLineLength = 140;

    // The first non-empty line of the summary, cut to SummaryLineLength characters with an ellipsis; "" for none.
    private static string SummaryLine(string? summary)
    {
        var line = (summary ?? "")
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length <= SummaryLineLength ? line : line[..(SummaryLineLength - 1)].TrimEnd() + "…";
    }

    private static List<KbFieldDefDto> ParseFieldDefs(string json)
        => JsonSerializer.Deserialize<List<KbFieldDefDto>>(json, JsonSerializerOptions.Web) ?? [];
}
