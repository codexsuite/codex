using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Edits a chapter's title, summary, notes and numbering. The slug is set once from the first title and never changes
// (the schema enforces it), so ch:<slug> keeps resolving after a rename. Each command checks that the current user
// owns the book first (#29): another user's chapter is not_found.
[RegisterSingleton]
public sealed class ChapterCommands(NpgsqlDataSource dataSource, IBookAccess access, OutlineQuery outline, IBookEvents events)
{
    private const int MaxTitleLength = 200;

    public async Task<ChapterFieldsDto> GetFieldsAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.View, cancellationToken, notFound: "Chapter not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ChapterFieldsDto>(new CommandDefinition(
            """
            SELECT title, coalesce(summary, '') AS Summary, coalesce(notes, '') AS Notes, is_numbered AS IsNumbered
            FROM chapter WHERE id = @chapterId AND archived_at IS NULL
            """,
            new { chapterId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Chapter not found.");
    }

    public async Task<OutlineChapterDto> PatchAsync(Guid chapterId, PatchChapterRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.Write, cancellationToken, notFound: "Chapter not found.");
        var title = request.Title?.Trim();
        if (title is { Length: 0 })
        {
            throw ApiException.Validation("A chapter needs a title.");
        }
        if (title is { Length: > MaxTitleLength })
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            UPDATE chapter SET
                title = coalesce(@title::text, title),
                summary = CASE WHEN @summary::text IS NULL THEN summary ELSE nullif(btrim(@summary::text), '') END,
                notes = CASE WHEN @notes::text IS NULL THEN notes ELSE nullif(btrim(@notes::text), '') END,
                is_numbered = coalesce(@isNumbered::boolean, is_numbered),
                updated_at = now()
            WHERE id = @chapterId AND archived_at IS NULL
            RETURNING book_id
            """,
            new { chapterId, title, summary = request.Summary, notes = request.Notes, isNumbered = request.IsNumbered },
            cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Chapter not found.");

        await events.PublishAsync(new StructureChanged(bookId, "chapter"));
        return (await outline.GetAsync(bookId, cancellationToken)).Acts
            .SelectMany(a => a.Chapters).Single(c => c.Id == chapterId);
    }
}
