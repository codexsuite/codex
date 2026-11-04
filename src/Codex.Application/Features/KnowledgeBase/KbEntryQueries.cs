using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbEntryQueries
{
    Task<KbEntryDto> GetAsync(Guid entryId, CancellationToken cancellationToken);

    // The sidebar form of one category of the book (own row or the series').
    Task<KbCategoryDto> GetCategoryAsync(Guid bookId, Guid categoryId, CancellationToken cancellationToken);
}

// One query per part of the window (entry, source, related, uses, timeline) on one connection.
[RegisterSingleton]
public sealed class KbEntryQueries(NpgsqlDataSource dataSource, TimeProvider timeProvider, IBookAccess access) : IKbEntryQueries
{
    private static readonly string[] KindOrder = ["mention", "summary", "linked", "beat", "beat_context", "timeline", "printed", "text"];

    private sealed record EntryRow(
        Guid Id, Guid BookId, Guid CategoryId, string CategoryName, string? CategoryColour, string FieldDefs, string Name,
        string Slug, string? Summary, string Body, string Fields, string CustomFields, string Aliases, bool AlwaysInContext, string Tags, bool ExcludeFromAutoContext,
        int PositionInCategory, int CategoryEntryCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record UseRow(
        Guid SceneId, Guid BookId, int? ChapterNumber, string ChapterTitle, int InChapter, string Kind, int Occurrences,
        string? Body, string? SceneSummary);

    private sealed record NoteRow(Guid Id, Guid SceneId, Guid BookId, int? ChapterNumber, int InChapter, string Body);

    public async Task<KbEntryDto> GetAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<EntryRow>(new CommandDefinition(
            """
            SELECT e.id, e.book_id AS BookId, e.category_id AS CategoryId, c.name AS CategoryName, c.color AS CategoryColour,
                   c.field_defs::text AS FieldDefs, e.name, e.slug, e.summary, e.body, e.fields::text AS Fields,
                   e.custom_fields::text AS CustomFields, to_json(e.aliases)::text AS Aliases,
                   e.always_in_context AS AlwaysInContext, to_json(e.tags)::text AS Tags, e.exclude_from_auto_context AS ExcludeFromAutoContext,
                   (SELECT count(*) FROM kb_entry s WHERE s.category_id = e.category_id AND s.archived_at IS NULL
                       AND (s.position COLLATE "C", s.id) <= (e.position COLLATE "C", e.id))::integer AS PositionInCategory,
                   (SELECT count(*) FROM kb_entry s WHERE s.category_id = e.category_id AND s.archived_at IS NULL)::integer AS CategoryEntryCount,
                   e.created_at AS CreatedAt, e.updated_at AS UpdatedAt
            FROM kb_entry e JOIN kb_category c ON c.id = e.category_id
            WHERE e.id = @entryId AND e.archived_at IS NULL
            """,
            new { entryId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");

        var source = await connection.QueryFirstOrDefaultAsync<KbSourceDto>(new CommandDefinition(
            """
            SELECT r.id, r.title, rc.status, rc.reason
            FROM reference r JOIN reference_clearance rc ON rc.reference_id = r.id
            WHERE r.kb_entry_id = @entryId AND r.archived_at IS NULL
            ORDER BY r.created_at, r.id LIMIT 1
            """,
            new { entryId }, cancellationToken: cancellationToken));

        var related = (await connection.QueryAsync<KbRelatedDto>(new CommandDefinition(
            """
            SELECT o.id, o.name, c.name AS CategoryName
            FROM kb_entry_related rel
            JOIN kb_entry o ON o.id = rel.related_id AND o.archived_at IS NULL
            JOIN kb_category c ON c.id = o.category_id
            WHERE rel.kb_entry_id = @entryId
            ORDER BY o.name, o.id
            """,
            new { entryId }, cancellationToken: cancellationToken))).ToList();

        var aliases = Deserialize<List<string>>(row.Aliases) ?? [];
        var uses = await UsesAsync(connection, row, aliases, cancellationToken);

        var notes = (await connection.QueryAsync<NoteRow>(new CommandDefinition(
            """
            SELECT p.progression_id AS Id, p.scene_id AS SceneId, ss.book_id AS BookId, p.chapter_number::integer AS ChapterNumber,
                   p.scene_number::integer AS InChapter, p.body
            FROM kb_progression_outline p JOIN scene_stats ss ON ss.scene_id = p.scene_id
            JOIN book b ON b.id = ss.book_id AND b.deleted_at IS NULL
            WHERE p.kb_entry_id = @entryId
            ORDER BY p.timeline_order
            """,
            new { entryId }, cancellationToken: cancellationToken)))
            .Select(n => new KbTimelineNoteDto(n.Id, n.SceneId, n.BookId, SceneNumber(n.ChapterNumber, n.InChapter), n.Body))
            .ToList();

        return new KbEntryDto(
            row.Id, row.BookId, row.CategoryId, row.CategoryName, row.CategoryColour,
            Deserialize<List<KbFieldDefDto>>(row.FieldDefs) ?? [], row.Name, row.Slug, row.Summary ?? "", row.Body,
            Deserialize<Dictionary<string, string>>(row.Fields) ?? [], Deserialize<List<KbCustomFieldDto>>(row.CustomFields) ?? [],
            aliases, row.AlwaysInContext, Deserialize<List<string>>(row.Tags) ?? [], row.ExcludeFromAutoContext, row.PositionInCategory, row.CategoryEntryCount, source, related, uses, notes,
            DisplayDate.Relative(row.CreatedAt, timeProvider), DisplayDate.Relative(row.UpdatedAt, timeProvider));
    }

    private sealed record CategoryRow(
        Guid Id, string Name, string? Tag, string? Colour, string Position, string FieldDefs, bool FromSeries, string? SeriesName);

    public async Task<KbCategoryDto> GetCategoryAsync(Guid bookId, Guid categoryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var r = await connection.QuerySingleAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT coalesce(bc.own_id, bc.series_id) AS Id, bc.name, bc.tag, bc.color AS Colour, bc.position,
                   c.field_defs::text AS FieldDefs, bc.own_id IS NULL AS FromSeries,
                   CASE WHEN bc.own_id IS NULL THEN owner.title END AS SeriesName
            FROM book_kb_category bc
            JOIN kb_category c ON c.id = coalesce(bc.own_id, bc.series_id)
            JOIN book owner ON owner.id = c.book_id
            WHERE bc.book_id = @bookId AND @categoryId = ANY (bc.category_ids)
            """,
            new { bookId, categoryId }, cancellationToken: cancellationToken));
        return new KbCategoryDto(
            r.Id, r.Name, r.Tag, r.Colour, r.Position, Deserialize<List<KbFieldDefDto>>(r.FieldDefs) ?? [], r.FromSeries, r.SeriesName);
    }

    private static async Task<List<KbUseDto>> UsesAsync(
        NpgsqlConnection connection, EntryRow entry, List<string> aliases, CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<UseRow>(new CommandDefinition(
            KbEntryUseSql.With + """

            SELECT u.scene_id AS SceneId, u.book_id AS BookId, u.chapter_number::integer AS ChapterNumber, c.title AS ChapterTitle,
                   u.scene_number::integer AS InChapter, u.kind, u.occurrences, p.body, s.summary AS SceneSummary
            FROM entry_use u
            JOIN chapter c ON c.id = u.chapter_id
            JOIN scene s ON s.id = u.scene_id
            LEFT JOIN prose p ON p.scene_id = u.scene_id
            WHERE u.kb_entry_id = @entryId
            ORDER BY u.book_id, u.reading_order, u.scene_number
            """,
            new { entryId = entry.Id, idText = entry.Id.ToString() }, cancellationToken: cancellationToken))).ToList();

        var names = new List<string> { entry.Name };
        names.AddRange(aliases);
        return rows.GroupBy(r => r.SceneId).Select(g =>
        {
            var first = g.First();
            var body = first.Body ?? "";
            var summary = first.SceneSummary ?? "";
            var kinds = g.Select(r => new KbUseKindDto(r.Kind, r.Occurrences)).ToList();
            if (KbSnippet.HasPlainName(names, body, summary))
            {
                kinds.Add(new KbUseKindDto("text", 1));
            }
            return new KbUseDto(
                first.SceneId, first.BookId, first.ChapterNumber, first.ChapterTitle, SceneNumber(first.ChapterNumber, first.InChapter),
                kinds.OrderBy(k => Array.IndexOf(KindOrder, k.Kind)).ToList(), KbSnippet.Find(entry.Id, names, body, summary));
        }).ToList();
    }

    private static string SceneNumber(int? chapterNumber, int sceneNumber) =>
        chapterNumber is null ? sceneNumber.ToString() : $"{chapterNumber}.{sceneNumber}";

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
}
