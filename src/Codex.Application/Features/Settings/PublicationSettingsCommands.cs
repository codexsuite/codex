using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Settings;
using Dapper;
using Npgsql;
using System.Text.Json;

namespace Codex.Application.Features.Settings;

// Citations & permissions: a book's own publication_settings row over its series' row. A null column inherits.
// Reading needs view, changing write (#80); a change touches the book's own row only, never the series'.
[RegisterSingleton]
public sealed class PublicationSettingsCommands(NpgsqlDataSource dataSource, IBookAccess access, IBookEvents events)
{
    private const string DefaultStyle = "chicago";
    private const int DefaultLimit = 300;
    private const int MinYear = 1900;
    private const int MaxYear = 2200;
    private static readonly string[] Styles = ["chicago", "apa", "harvard"];
    private static readonly string[] FormatCodes = ["print", "ebook", "pdf", "audio"];

    // The field names DELETE accepts.
    private static readonly string[] Fields = ["style", "quoteWordLimit", "publisher", "plannedYear", "formats", "printRun", "territory"];

    // One row of publication_settings (formats arrive as JSON: Dapper can't bind a text[] to a record).
    private sealed record Values(
        string? CitationStyle, string? RulesName, int? QuoteWordLimit, string? PublisherName, int? PlannedYear,
        string? FormatsJson, int? PrintRun, string? Territory)
    {
        public static Values Empty { get; } = new(null, null, null, null, null, null, null, null);
    }

    private sealed record BookRow(Guid? SeriesId, string? SeriesName);

    public async Task<PublicationSettingsDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, bookId, cancellationToken);
    }

    public async Task<PublicationSettingsDto> PatchAsync(Guid bookId, PatchPublicationSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, "That book doesn't exist.");
        var fields = ChangedFields(request);
        if (fields.Count == 0)
        {
            throw ApiException.Validation("Nothing to change.");
        }
        Validate(request);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var book = await FindBookAsync(connection, bookId, cancellationToken, transaction);
            var series = book.SeriesId is null
                ? Values.Empty
                : await ReadRowAsync(connection, book.SeriesId.Value, cancellationToken, transaction);

            var style = Own(request.Style, series.CitationStyle);
            var limit = Own(request.QuoteWordLimit, series.QuoteWordLimit);
            var publisher = Own(request.Publisher?.Trim(), series.PublisherName);
            var year = request.PlannedYear == 0 ? null : Own(request.PlannedYear, series.PlannedYear);
            var formats = NormaliseFormats(request.Formats ?? []);
            var formatsInherit = request.Formats != null && SameFormats(formats, series.FormatsJson);
            var run = request.PrintRun == 0 ? null : Own(request.PrintRun, series.PrintRun);
            var territory = string.IsNullOrWhiteSpace(request.Territory) ? null : Own(request.Territory.Trim(), series.Territory);

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO publication_settings (book_id) VALUES (@bookId) ON CONFLICT (book_id) DO NOTHING",
                new { bookId }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE publication_settings SET
                    citation_style   = CASE WHEN @setStyle     THEN @style::text      ELSE citation_style END,
                    quote_word_limit = CASE WHEN @setLimit     THEN @limit::int       ELSE quote_word_limit END,
                    publisher_name   = CASE WHEN @setPublisher THEN @publisher::text  ELSE publisher_name END,
                    planned_year     = CASE WHEN @setYear      THEN @year::int        ELSE planned_year END,
                    formats          = CASE WHEN @setFormats   THEN (CASE WHEN @formatsInherit THEN NULL ELSE @formats::text[] END)  ELSE formats END,
                    print_run        = CASE WHEN @setRun       THEN @run::int         ELSE print_run END,
                    territory        = CASE WHEN @setTerritory THEN @territory::text  ELSE territory END,
                    updated_at       = now()
                WHERE book_id = @bookId
                """,
                new
                {
                    bookId,
                    setStyle = request.Style != null, style,
                    setLimit = request.QuoteWordLimit != null, limit,
                    setPublisher = request.Publisher != null, publisher,
                    setYear = request.PlannedYear != null, year,
                    setFormats = request.Formats != null, formats, formatsInherit,
                    setRun = request.PrintRun != null, run,
                    setTerritory = request.Territory != null, territory,
                },
                transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(bookId, fields));
        return await ReadAsync(connection, bookId, cancellationToken);
    }

    // "This book ×": the field goes back to the series value.
    public async Task<PublicationSettingsDto> ResetAsync(Guid bookId, string field, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, "That book doesn't exist.");
        if (!Fields.Contains(field))
        {
            throw ApiException.Validation($"Unknown setting '{field}'. Use one of {string.Join(", ", Fields)}.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await FindBookAsync(connection, bookId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE publication_settings SET
                citation_style   = CASE WHEN @field = 'style'          THEN NULL ELSE citation_style END,
                quote_word_limit = CASE WHEN @field = 'quoteWordLimit' THEN NULL ELSE quote_word_limit END,
                publisher_name   = CASE WHEN @field = 'publisher'      THEN NULL ELSE publisher_name END,
                planned_year     = CASE WHEN @field = 'plannedYear'    THEN NULL ELSE planned_year END,
                formats          = CASE WHEN @field = 'formats'        THEN NULL ELSE formats END,
                print_run        = CASE WHEN @field = 'printRun'       THEN NULL ELSE print_run END,
                territory        = CASE WHEN @field = 'territory'      THEN NULL ELSE territory END,
                updated_at       = now()
            WHERE book_id = @bookId
            """,
            new { bookId, field }, cancellationToken: cancellationToken));
        await events.PublishAsync(new BookMetaChanged(bookId, [field]));
        return await ReadAsync(connection, bookId, cancellationToken);
    }

    private static List<string> ChangedFields(PatchPublicationSettingsRequest request)
    {
        var fields = new List<string>();
        if (request.Style != null) fields.Add("style");
        if (request.QuoteWordLimit != null) fields.Add("quoteWordLimit");
        if (request.Publisher != null) fields.Add("publisher");
        if (request.PlannedYear != null) fields.Add("plannedYear");
        if (request.Formats != null) fields.Add("formats");
        if (request.PrintRun != null) fields.Add("printRun");
        if (request.Territory != null) fields.Add("territory");
        return fields;
    }

    private static void Validate(PatchPublicationSettingsRequest request)
    {
        if (request.Style != null && !Styles.Contains(request.Style))
        {
            throw ApiException.Validation($"Citation style must be one of {string.Join(", ", Styles)}.");
        }
        if (request.QuoteWordLimit is <= 0)
        {
            throw ApiException.Validation("The quote limit must be more than 0 words.");
        }
        if (request.PlannedYear is { } year && year != 0 && (year < MinYear || year > MaxYear))
        {
            throw ApiException.Validation($"Publication year must be between {MinYear} and {MaxYear}.");
        }
        if (request.PrintRun is < 0)
        {
            throw ApiException.Validation("The print run can't be negative.");
        }
        if (request.Formats != null && request.Formats.Any(f => !FormatCodes.Contains(f)))
        {
            throw ApiException.Validation($"Formats must be among {string.Join(", ", FormatCodes)}.");
        }
        if (request.Publisher is { Length: > 200 } || request.Territory is { Length: > 100 })
        {
            throw ApiException.Validation("That value is too long.");
        }
    }

    // A value equal to the series' own goes back to inheriting (stored as NULL).
    private static T? Own<T>(T? value, T? seriesValue)
        where T : struct, IEquatable<T> => value is { } v && seriesValue is { } s && v.Equals(s) ? null : value;

    private static string? Own(string? value, string? seriesValue) => value == seriesValue ? null : value;

    private static string[] NormaliseFormats(IReadOnlyList<string> requested) =>
        requested.Distinct().OrderBy(f => Array.IndexOf(FormatCodes, f)).ToArray();

    private static bool SameFormats(string[] formats, string? seriesJson) =>
        seriesJson != null && ParseFormats(seriesJson).OrderBy(f => f).SequenceEqual(formats.OrderBy(f => f));

    private static IReadOnlyList<string> ParseFormats(string? json)
    {
        return json is null ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    // Access is checked first (view to read, write to change); this only tells a book from a series id.
    private static async Task<BookRow> FindBookAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        await connection.QuerySingleOrDefaultAsync<BookRow>(new CommandDefinition(
            """
            SELECT b.series_id AS SeriesId, s.title AS SeriesName
            FROM book b LEFT JOIN book s ON s.id = b.series_id
            WHERE b.id = @bookId AND b.kind = 'book'
            """,
            new { bookId }, transaction, cancellationToken: cancellationToken))
        ?? throw ApiException.NotFound("That book doesn't exist.");

    private static async Task<Values> ReadRowAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null) =>
        await connection.QuerySingleOrDefaultAsync<Values>(new CommandDefinition(
            """
            SELECT citation_style AS CitationStyle, rules_name AS RulesName, quote_word_limit AS QuoteWordLimit,
                   publisher_name AS PublisherName, planned_year AS PlannedYear, to_json(formats)::text AS FormatsJson,
                   print_run AS PrintRun, territory AS Territory
            FROM publication_settings WHERE book_id = @bookId
            """,
            new { bookId }, transaction, cancellationToken: cancellationToken))
        ?? Values.Empty;

    private async Task<PublicationSettingsDto> ReadAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken)
    {
        var book = await FindBookAsync(connection, bookId, cancellationToken);
        var own = await ReadRowAsync(connection, bookId, cancellationToken);
        var series = book.SeriesId is null ? Values.Empty : await ReadRowAsync(connection, book.SeriesId.Value, cancellationToken);
        var inSeries = book.SeriesId is not null;

        PublicationValueDto<T> Pick<T>(T? ownValue, T? seriesValue, T fallback)
            where T : class => new(ownValue ?? seriesValue ?? fallback, inSeries && ownValue is null);
        PublicationValueDto<int> PickInt(int? ownValue, int? seriesValue, int fallback) =>
            new(ownValue ?? seriesValue ?? fallback, inSeries && ownValue is null);
        PublicationValueDto<int?> PickOptionalInt(int? ownValue, int? seriesValue) =>
            new(ownValue ?? seriesValue, inSeries && ownValue is null);

        return new PublicationSettingsDto(
            bookId,
            book.SeriesName,
            Pick(own.CitationStyle, series.CitationStyle, DefaultStyle),
            own.RulesName ?? series.RulesName ?? "Default",
            PickInt(own.QuoteWordLimit, series.QuoteWordLimit, DefaultLimit),
            new PublicationValueDto<string?>(own.PublisherName ?? series.PublisherName, inSeries && own.PublisherName is null),
            PickOptionalInt(own.PlannedYear, series.PlannedYear),
            new PublicationValueDto<IReadOnlyList<string>>(ParseFormats(own.FormatsJson ?? series.FormatsJson), inSeries && own.FormatsJson is null),
            PickOptionalInt(own.PrintRun, series.PrintRun),
            new PublicationValueDto<string?>(own.Territory ?? series.Territory, inSeries && own.Territory is null));
    }
}
