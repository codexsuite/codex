using System.ComponentModel;
using Codex.Application.Features.Settings;
using Codex.Contracts.Settings;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// get_book_settings (M3): the Book settings screen's reads, BookSettingsCommands.GetAsync (book, series, writing) and
// PublicationSettingsCommands.GetAsync (citations and permissions). The merged style guide is what every AI draft is
// sent: the series' guide, then the book's (as DraftGenerator joins them).
[McpServerToolType]
public sealed class SettingsTools(McpRefs refs, BookSettingsCommands settings, PublicationSettingsCommands publication)
{
    public sealed record StyleGuide(string? Book, string? Series, string Merged);

    public sealed record Setting<T>(T Value, bool Inherited);

    public sealed record Publication(
        Setting<string> CitationStyle, string RulesName, Setting<int> QuoteWordLimit, Setting<string?> Publisher,
        Setting<int?> PlannedYear, Setting<IReadOnlyList<string>> Formats, Setting<int?> PrintRun, Setting<string?> Territory);

    public sealed record GetBookSettingsResult(
        string Title, string? AuthorName, string Status, int? TargetWords, int Words, int? ProgressPercent, McpItem? Series,
        int? SeriesNumber, StyleGuide StyleGuide, Publication Publication, bool Archived) : McpResult;

    [McpServerTool(Name = "get_book_settings", Title = "How is this book set up?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A book's settings: title, author, status, target, series, the style guide (book, series and merged, as sent with every AI draft) " +
                 "and the publication settings (citation style, quote word limit, publisher, year, formats, print run, territory; each says whether it is inherited from the series).")]
    public async Task<GetBookSettingsResult> GetBookSettingsAsync(
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        CancellationToken cancellationToken = default)
    {
        var target = await refs.BookAsync(book, cancellationToken);
        var dto = await settings.GetAsync(target.Id, cancellationToken);
        var rules = await publication.GetAsync(target.Id, cancellationToken);
        var merged = string.Join("\n\n", new[] { dto.SeriesGuide, dto.StyleGuide }.Where(g => !string.IsNullOrWhiteSpace(g)));
        return new GetBookSettingsResult(
            dto.Title, dto.AuthorName, dto.Status, dto.TargetWords, dto.WordCount, dto.ProgressPercent,
            dto.SeriesId is { } seriesId ? new McpItem(seriesId, "series", dto.SeriesName ?? "", CodexLinks.Series(seriesId)) : null,
            dto.SeriesNumber, new StyleGuide(dto.StyleGuide, dto.SeriesGuide, merged),
            new Publication(
                Of(rules.Style), rules.RulesName, Of(rules.QuoteWordLimit), Of(rules.Publisher), Of(rules.PlannedYear), Of(rules.Formats),
                Of(rules.PrintRun), Of(rules.Territory)),
            dto.Archived)
        {
            Item = new McpItem(target.Id, "book", target.Title, CodexLinks.Settings(target.Id)),
        };
    }

    private static Setting<T> Of<T>(PublicationValueDto<T> value) => new(value.Value, value.Inherited);
}
