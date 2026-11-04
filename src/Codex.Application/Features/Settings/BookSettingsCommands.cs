using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Settings;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// Book settings (#80): anyone who can open the book reads them; editing needs write. The series list for "Move to
// another series" is the caller's own series, and only for the book's owner (moving is the owner's); anyone else gets
// an empty list.
[RegisterSingleton]
public sealed class BookSettingsCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events)
{
    private const int MaxTitleLength = 300;
    private const int MaxAuthorLength = 200;
    private static readonly string[] Statuses = ["planning", "draft", "writing", "revising", "published"];

    private sealed record BookRow(
        Guid Id, string Title, string? AuthorName, string Status, int? TargetWords, int WordCount, Guid? SeriesId,
        string? SeriesName, int? SeriesNumber, string? StyleGuide, string? SeriesGuide, bool Archived, string? Subtitle, string? Description);

    private sealed record OrderRow(Guid Id, string Title, int Number, string Status);

    public async Task<BookSettingsDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var role = await access.RequireAsync(bookId, BookRole.View, cancellationToken, "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<BookRow>(new CommandDefinition(
            """
            SELECT b.id, b.title, b.author_name AS AuthorName, b.status, b.target_word_count AS TargetWords,
                   coalesce((SELECT sum(ss.word_count) FROM scene_stats ss WHERE ss.book_id = b.id), 0)::int AS WordCount,
                   b.series_id AS SeriesId, s.title AS SeriesName, b.series_number AS SeriesNumber,
                   b.style_guide AS StyleGuide, s.style_guide AS SeriesGuide,
                   (b.archived_at IS NOT NULL) AS Archived, b.subtitle AS Subtitle, b.description AS Description
            FROM book b LEFT JOIN book s ON s.id = b.series_id
            WHERE b.id = @bookId AND b.kind = 'book'
            """,
            new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("That book doesn't exist.");

        var options = role != BookRole.Owner ? [] : (await connection.QueryAsync<SeriesOptionDto>(new CommandDefinition(
            """
            SELECT s.id, s.title AS Name,
                   (SELECT count(*) FROM book x WHERE x.series_id = s.id AND x.archived_at IS NULL)::int AS BookCount
            FROM book s
            WHERE s.kind = 'series' AND s.owner_id = @owner AND s.archived_at IS NULL
            ORDER BY s.title COLLATE "C", s.id
            """,
            new { owner = currentUser.Id }, cancellationToken: cancellationToken))).ToList();

        var order = row.SeriesId is null
            ? []
            : (await connection.QueryAsync<OrderRow>(new CommandDefinition(
                """
                SELECT id, title, series_number AS Number, status
                FROM book WHERE series_id = @seriesId AND archived_at IS NULL
                ORDER BY series_number, id
                """,
                new { seriesId = row.SeriesId }, cancellationToken: cancellationToken)))
                .Select(o => new SeriesOrderEntryDto(o.Id, o.Title, o.Number, o.Status)).ToList();

        int? percent = row.TargetWords is > 0 ? (int)Math.Round(100.0 * row.WordCount / row.TargetWords.Value) : null;
        return new BookSettingsDto(
            row.Id, row.Title, row.AuthorName, row.Status, row.TargetWords, row.WordCount, percent, row.SeriesId,
            row.SeriesName, row.SeriesNumber, options, order, row.StyleGuide, row.SeriesGuide, row.Archived, row.Subtitle, row.Description);
    }

    public async Task<BookSettingsDto> PatchAsync(Guid bookId, PatchBookSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, "That book doesn't exist.");
        var fields = new List<string>();
        if (request.Title != null) fields.Add("title");
        if (request.AuthorName != null) fields.Add("authorName");
        if (request.Status != null) fields.Add("status");
        if (request.TargetWords != null) fields.Add("targetWords");
        if (request.StyleGuide != null) fields.Add("styleGuide");
        if (request.Subtitle != null) fields.Add("subtitle");
        if (request.Description != null) fields.Add("description");
        if (fields.Count == 0)
        {
            throw ApiException.Validation("Nothing to change.");
        }

        var title = request.Title?.Trim();
        if (title is { Length: 0 })
        {
            throw ApiException.Validation("A book needs a title.");
        }
        if (title is { Length: > MaxTitleLength })
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }
        if (request.Subtitle is { Length: > MaxTitleLength })
        {
            throw ApiException.Validation($"Subtitle can be at most {MaxTitleLength} characters.");
        }
        if (request.AuthorName is { Length: > MaxAuthorLength })
        {
            throw ApiException.Validation($"Author can be at most {MaxAuthorLength} characters.");
        }
        if (request.Status != null && !Statuses.Contains(request.Status))
        {
            throw ApiException.Validation($"Status must be one of {string.Join(", ", Statuses)}.");
        }
        if (request.TargetWords < 0)
        {
            throw ApiException.Validation("Target words can't be negative.");
        }

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE book SET
                    title = coalesce(@title::text, title),
                    author_name = CASE WHEN @author::text IS NULL THEN author_name ELSE nullif(btrim(@author::text), '') END,
                    status = coalesce(@status::text, status),
                    target_word_count = CASE WHEN @target::int IS NULL THEN target_word_count ELSE nullif(@target::int, 0) END,
                    style_guide = CASE WHEN @style::text IS NULL THEN style_guide
                                       WHEN btrim(@style::text) = '' THEN NULL ELSE @style::text END,
                    subtitle = CASE WHEN @subtitle::text IS NULL THEN subtitle ELSE nullif(btrim(@subtitle::text), '') END,
                    description = CASE WHEN @description::text IS NULL THEN description ELSE nullif(btrim(@description::text), '') END
                WHERE id = @bookId AND kind = 'book'
                """,
                new
                {
                    bookId, title, author = request.AuthorName, status = request.Status,
                    target = request.TargetWords, style = request.StyleGuide, subtitle = request.Subtitle, description = request.Description,
                },
                cancellationToken: cancellationToken));
            if (updated == 0)
            {
                throw ApiException.NotFound("That book doesn't exist.");
            }
        }

        await events.PublishAsync(new BookMetaChanged(bookId, fields));
        return await GetAsync(bookId, cancellationToken);
    }
}
