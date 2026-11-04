using System.Globalization;
using System.Text;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Export;

// The manuscript as one Markdown document (J19): a YAML title block, an "# Act" heading per live act with chapters
// (book scope only), "## Chapter N: Title" per chapter, scenes joined by "* * *", then the bibliography and the footnotes.
// Live rows only. Scene text goes through ManuscriptText. Read-only: prose is never changed.
public interface IManuscriptMarkdown
{
    // 422 blocked when the scope holds no chapters; 404 a chapter that is not a live one of the book.
    Task<ManuscriptMarkdownResult> BuildAsync(Guid bookId, ManuscriptOptions options, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class ManuscriptMarkdown(NpgsqlDataSource dataSource) : IManuscriptMarkdown
{
    public const string NothingToExport = "Nothing to export. This book has no chapters.";

    public async Task<ManuscriptMarkdownResult> BuildAsync(Guid bookId, ManuscriptOptions options, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var book = await connection.QuerySingleOrDefaultAsync<(string Title, string? Author, string Style)?>(new CommandDefinition(
            """
            SELECT b.title AS Title, b.author_name AS Author, coalesce(ps.citation_style, 'chicago') AS Style
            FROM book b LEFT JOIN book_publication_settings ps ON ps.book_id = b.id
            WHERE b.id = @bookId AND b.kind = 'book'
            """,
            new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Book {bookId} was not found.");

        var acts = (await connection.QueryAsync<ActRow>(new CommandDefinition(
            "SELECT id, title FROM act WHERE book_id = @bookId AND archived_at IS NULL ORDER BY position, id",
            new { bookId }, cancellationToken: cancellationToken))).ToList();
        var chapters = (await connection.QueryAsync<ChapterRow>(new CommandDefinition(
            """
            SELECT c.id, c.act_id AS ActId, c.title, c.is_numbered AS IsNumbered, c.slug AS ChapterSlug
            FROM chapter c JOIN act a ON a.id = c.act_id
            WHERE c.book_id = @bookId AND c.archived_at IS NULL AND a.archived_at IS NULL
            ORDER BY a.position, a.id, c.position, c.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();
        if (chapters.Count == 0)
        {
            throw ApiException.Blocked(NothingToExport);
        }

        var inScope = chapters.Where(c => options.Scope == "book" || c.Id == options.ChapterId).ToList();
        if (inScope.Count == 0)
        {
            throw ApiException.NotFound($"Chapter {options.ChapterId} is not a live chapter of book {bookId}.");
        }

        var scenes = (await connection.QueryAsync<SceneRow>(new CommandDefinition(
            """
            SELECT s.chapter_id AS ChapterId, s.summary, coalesce(p.body, '') AS Body
            FROM scene s LEFT JOIN prose p ON p.scene_id = s.id
            WHERE s.book_id = @bookId AND s.archived_at IS NULL
            ORDER BY s.position, s.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToLookup(s => s.ChapterId);
        var beats = (await connection.QueryAsync<(Guid Id, string Body)>(new CommandDefinition(
            """
            SELECT b.id, b.body FROM beat b JOIN scene s ON s.id = b.scene_id
            WHERE s.book_id = @bookId AND b.archived_at IS NULL
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToDictionary(b => b.Id, b => b.Body);
        var citations = (await connection.QueryAsync<ManuscriptCitation>(new CommandDefinition(
            """
            SELECT u.id AS UseId, u.reference_id AS ReferenceId, r.authors::text AS Authors, r.title, r.year, r.publisher, u.locator
            FROM reference_use u JOIN reference r ON r.id = u.reference_id
            WHERE u.book_id = @bookId AND u.archived_at IS NULL AND u.generation_id IS NULL
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToDictionary(c => c.UseId);

        var text = new ManuscriptText(book.Style, citations, beats, options.IncludeBeats);
        var blocks = new List<string> { Front(book.Title, book.Author) };
        var number = 0;
        var numbers = chapters.ToDictionary(c => c.Id, c => c.IsNumbered ? ++number : (int?)null);
        foreach (var act in acts.Where(a => inScope.Any(c => c.ActId == a.Id)))
        {
            if (options.Scope == "book" && act.Title.Trim().Length > 0)
            {
                blocks.Add($"# {act.Title.Trim()}");
            }

            foreach (var chapter in inScope.Where(c => c.ActId == act.Id))
            {
                var heading = numbers[chapter.Id] is { } n ? string.Create(CultureInfo.InvariantCulture, $"Chapter {n}: {chapter.Title}") : chapter.Title;
                blocks.Add($"## {heading}");
                var chapterScenes = new List<string>();
                foreach (var scene in scenes[chapter.Id])
                {
                    var parts = new List<string>();
                    if (options.IncludeSummaries && !string.IsNullOrWhiteSpace(scene.Summary) && ManuscriptText.Summary(scene.Summary).Length > 0)
                    {
                        parts.Add(ManuscriptText.Summary(scene.Summary));
                    }

                    var prose = text.Scene(scene.Body);
                    if (prose.Length > 0)
                    {
                        parts.Add(prose);
                    }

                    if (parts.Count > 0)
                    {
                        chapterScenes.Add(string.Join("\n\n", parts));
                    }
                }

                blocks.Add(string.Join("\n\n* * *\n\n", chapterScenes));
            }
        }

        blocks.Add(text.Bibliography());
        blocks.Add(text.FootnoteDefinitions());
        var markdown = string.Join("\n\n", blocks.Where(b => b.Length > 0)) + "\n";

        var notes = new List<string>();
        if (text.RawLatexSkipped > 0)
        {
            notes.Add($"{text.RawLatexSkipped} raw-LaTeX {(text.RawLatexSkipped == 1 ? "block" : "blocks")} skipped");
        }

        if (text.FiguresSkipped > 0)
        {
            notes.Add($"{text.FiguresSkipped} {(text.FiguresSkipped == 1 ? "figure" : "figures")} not included (images are not part of this export)");
        }

        var slug = Slug(book.Title);
        if (options.Scope == "chapter")
        {
            slug += "-" + Slug(inScope[0].ChapterSlug);
        }

        return new ManuscriptMarkdownResult(book.Title, book.Author, slug, markdown, notes);
    }

    private static string Front(string title, string? author)
    {
        var front = new StringBuilder("---\ntitle: ").Append(JsonSerializer.Serialize(title));
        if (!string.IsNullOrWhiteSpace(author))
        {
            front.Append("\nauthor: ").Append(JsonSerializer.Serialize(author));
        }

        return front.Append("\n---").ToString();
    }

    private static string Slug(string title)
    {
        var slug = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        if (text.Length == 0)
        {
            return "manuscript";
        }

        return text.Length > 60 ? text[..60].TrimEnd('-') : text;
    }

    private sealed record ActRow(Guid Id, string Title);

    private sealed record ChapterRow(Guid Id, Guid ActId, string Title, bool IsNumbered, string ChapterSlug);

    private sealed record SceneRow(Guid ChapterId, string? Summary, string Body);
}
