using System.Globalization;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

[RegisterSingleton]
public sealed class AddSourceCommands(NpgsqlDataSource dataSource, ISourceLookup lookup, ICurrentUser currentUser, TimeProvider clock, IBookAccess access) : IAddSourceCommands
{
    public const int MaxImportItems = 500;

    // The wire name of an import format -> the reference.external_provider it is stored as (CSL JSON is Zotero's export).
    private static readonly Dictionary<string, string> ImportProviders = new(StringComparer.Ordinal) { ["bibtex"] = "bibtex", ["ris"] = "ris", ["csl"] = "zotero" };

    private static readonly Dictionary<string, string> ImportLabels = new(StringComparer.Ordinal) { ["bibtex"] = "BibTeX", ["ris"] = "RIS", ["csl"] = "CSL JSON" };

    public async Task<LookupSourceResultDto> LookupAsync(Guid bookId, LookupSourceRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var kind = request.Kind ?? string.Empty;
        var value = Canonical(kind, request.Value);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await RequireBookAsync(connection, null, bookId, cancellationToken);

        var existing = await FindDuplicateAsync(connection, null, bookId, new Identifiers(
            kind == SourceLookupKinds.Isbn ? value : null,
            kind == SourceLookupKinds.Doi ? value : null,
            kind == SourceLookupKinds.Url ? value : null,
            null), cancellationToken);
        if (existing is not null)
        {
            return new LookupSourceResultDto(LookupSourceResultDto.ExistingOutcome, value, null, existing.Id, existing.Title, existing.Byline, null);
        }

        var result = await lookup.LookupAsync(kind, value, cancellationToken);
        return new LookupSourceResultDto(result.Outcome, value, result.Source, null, null, null, result.RedirectTo);
    }

    public async Task<AddSourceResult> AddAsync(Guid bookId, AddSourceRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        if (request.Via is not null && !SourceLookupKinds.All.Contains(request.Via, StringComparer.Ordinal))
        {
            throw ApiException.Validation($"'{request.Via}' is not a way to add a source: use isbn, doi or url.");
        }

        var details = Parse(request.Details ?? throw ApiException.Validation("The source details are required."), Today());
        if (details.Title.Length == 0)
        {
            throw ApiException.Validation("A source needs a title.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RequireBookAsync(connection, transaction, bookId, cancellationToken);
        var existing = await FindDuplicateAsync(connection, transaction, bookId, details.Ids, cancellationToken);
        if (existing is not null)
        {
            return new AddSourceResult(null, existing.Id);
        }

        var id = await InsertAsync(connection, transaction, bookId, details, null, null, provenance, request.Via?.ToUpperInvariant(), null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AddSourceResult(id, null);
    }

    public Task<ImportSourcesResult> ImportAsync(Guid bookId, ImportSourcesRequest request, CancellationToken cancellationToken) =>
        RunImportAsync(bookId, request, commit: true, cancellationToken);

    public Task<ImportSourcesResult> PreviewImportAsync(Guid bookId, ImportSourcesRequest request, CancellationToken cancellationToken) =>
        RunImportAsync(bookId, request, commit: false, cancellationToken);

    // The preview runs the same loop and rolls back, so it counts duplicates inside the file too and writes nothing.
    private async Task<ImportSourcesResult> RunImportAsync(Guid bookId, ImportSourcesRequest request, bool commit, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var items = request.Items ?? throw ApiException.Validation("There is nothing to import.");
        if (items.Count > MaxImportItems)
        {
            throw ApiException.Validation($"Import at most {MaxImportItems} entries at a time.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RequireBookAsync(connection, transaction, bookId, cancellationToken);

        var created = 0;
        var duplicates = 0;
        var untitled = 0;
        var skipped = new List<string>();
        var reasons = new List<string>();
        foreach (var item in items)
        {
            if (!ImportProviders.TryGetValue(item.Provider ?? string.Empty, out var storedProvider))
            {
                throw ApiException.Validation($"'{item.Provider}' is not an import format: use bibtex, ris or csl.");
            }

            var details = Parse(item.Details ?? throw ApiException.Validation("An entry has no details."), Today());
            var key = string.IsNullOrWhiteSpace(item.Key) ? null : item.Key.Trim();
            var ids = details.Ids with { CiteKey = string.Equals(item.Provider, "bibtex", StringComparison.Ordinal) ? key : null, Provider = key is null ? null : storedProvider, ExternalKey = key };
            if (details.Title.Length == 0)
            {
                untitled++;
                skipped.Add("(untitled entry)");
                reasons.Add("no title");
                continue;
            }

            if (await FindDuplicateAsync(connection, transaction, bookId, ids, cancellationToken) is not null)
            {
                duplicates++;
                skipped.Add(details.Title);
                reasons.Add("already in References");
                continue;
            }

            await InsertAsync(connection, transaction, bookId, details, ids.CiteKey, key, new EventProvenance("import"), ImportLabels[item.Provider!], storedProvider, cancellationToken);
            created++;
        }

        if (commit)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new ImportSourcesResult(created, skipped.Count, skipped, duplicates, untitled, reasons);
    }

    private async Task<Guid> InsertAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId, ParsedSource d, string? citeKey, string? externalKey,
        EventProvenance? eventProvenance, string? how, string? provider, CancellationToken cancellationToken)
    {
        try
        {
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO reference (book_id, kind, title, authors, year, publisher, edition, isbn, doi, url, accessed_on, licence,
                                       rights_holder, cite_key, external_provider, external_key, created_by)
                VALUES (@bookId, @Kind, @Title, @Authors::jsonb, @Year, @Publisher, @Edition, @Isbn, @Doi, @Url,
                        COALESCE(@Accessed, CASE WHEN @Kind = 'web' AND @Url IS NOT NULL THEN @today END),
                        @Licence, @RightsHolder, @citeKey, @provider, @externalKey, @user)
                RETURNING id
                """,
                new
                {
                    bookId, d.Kind, d.Title, d.Authors, d.Year, d.Publisher, d.Edition, d.Ids.Isbn, d.Ids.Doi, d.Ids.Url, d.Accessed, d.Licence,
                    d.RightsHolder, today = Today(), citeKey, provider, externalKey = provider is null ? null : externalKey, user = currentUser.Id,
                },
                transaction, cancellationToken: cancellationToken));
            await SourceEvents.RecordAsync(connection, transaction, id, currentUser.Id, SourceEvents.Added, new { how }, cancellationToken, eventProvenance);
            return id;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw ApiException.Conflict("A source in this book already uses that cite key.");
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            throw ApiException.Validation(e.MessageText);
        }
    }

    private static string Canonical(string kind, string? raw)
    {
        if (!SourceLookupKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            throw ApiException.Validation($"'{kind}' cannot be looked up: use isbn, doi or url.");
        }

        return kind switch
        {
            SourceLookupKinds.Isbn => SourceIdentifiers.NormaliseIsbn(raw) ?? throw ApiException.Validation("That is not a valid ISBN."),
            SourceLookupKinds.Doi => SourceIdentifiers.NormaliseDoi(raw) ?? throw ApiException.Validation("That is not a DOI: it starts with 10. and has a slash."),
            _ => SourceIdentifiers.NormaliseUrl(raw) ?? throw ApiException.Validation("Enter a web address starting with http:// or https://."),
        };
    }

    private DateOnly Today() => DisplayDate.Today(clock.GetUtcNow(), clock.LocalTimeZone);

    private static ParsedSource Parse(SourceDetailsRequest d, DateOnly today)
    {
        var accessed = AccessedOnDate.Parse(d.AccessedOn, today);

        if (d.Year is < 0 or > 3000)
        {
            throw ApiException.Validation("The year must be between 0 and 3000.");
        }

        return new ParsedSource(
            d.Kind ?? string.Empty, d.Title?.Trim() ?? string.Empty, SourceAuthors.ToJson(d.AuthorsText ?? string.Empty), d.Year, Clean(d.Publisher),
            Clean(d.Edition), accessed, string.IsNullOrWhiteSpace(d.Licence) ? "unknown" : d.Licence, Clean(d.RightsHolder),
            new Identifiers(SourceIdentifiers.CleanIsbn(d.Isbn), SourceIdentifiers.NormaliseDoi(d.Doi) ?? Clean(d.Doi), Clean(d.Url), null));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task RequireBookAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid bookId, CancellationToken cancellationToken)
    {
        var found = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book' AND archived_at IS NULL)",
            new { bookId }, transaction, cancellationToken: cancellationToken));
        if (!found)
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }
    }

    private static async Task<Duplicate?> FindDuplicateAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid bookId, Identifiers ids, CancellationToken cancellationToken) =>
        await connection.QueryFirstOrDefaultAsync<Duplicate>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.title AS Title,
                   concat_ws(' · ', NULLIF(string_agg(DISTINCT coalesce(a ->> 'literal', concat_ws(', ', a ->> 'family', a ->> 'given')), '; '), ''), r.year::text) AS Byline
            FROM reference r
            LEFT JOIN LATERAL jsonb_array_elements(r.authors) a ON true
            WHERE r.book_id = @bookId AND r.archived_at IS NULL AND r.merged_into_id IS NULL
              AND ((@Isbn::text IS NOT NULL AND r.isbn = @Isbn)
                OR (@Doi::text IS NOT NULL AND lower(r.doi) = lower(@Doi))
                OR (@Url::text IS NOT NULL AND lower(rtrim(r.url, '/')) = lower(rtrim(@Url, '/')))
                OR (@CiteKey::text IS NOT NULL AND r.cite_key = @CiteKey)
                OR (@ExternalKey::text IS NOT NULL AND r.external_provider = @Provider AND r.external_key = @ExternalKey))
            GROUP BY r.id
            ORDER BY r.created_at
            LIMIT 1
            """,
            new { bookId, ids.Isbn, ids.Doi, ids.Url, ids.CiteKey, ids.Provider, ids.ExternalKey }, transaction, cancellationToken: cancellationToken));

    private sealed record Identifiers(string? Isbn, string? Doi, string? Url, string? CiteKey, string? Provider = null, string? ExternalKey = null);

    private sealed record ParsedSource(
        string Kind, string Title, string Authors, int? Year, string? Publisher, string? Edition, DateOnly? Accessed, string Licence,
        string? RightsHolder, Identifiers Ids);

    private sealed record Duplicate(Guid Id, string Title, string? Byline);
}
