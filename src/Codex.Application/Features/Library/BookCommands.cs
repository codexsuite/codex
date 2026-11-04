using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Library;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Library;

// The access rule (#80): a new book belongs to its creator, so it can only join a series the creator owns (the
// owner_id filters below stay on purpose); a series shared with them, even to edit, is refused like any non-series.
[RegisterSingleton]
public sealed class BookCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events)
{
    private const int MaxTitleLength = 200;
    private const string UniqueViolation = "23505";
    private static readonly string[] Statuses = ["planning", "draft", "writing", "revising", "published"];

    public async Task<CreatedBookDto> CreateAsync(CreateBookRequest request, CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim() ?? "";
        if (title.Length == 0)
        {
            throw ApiException.Validation("A book needs a title.");
        }
        if (title.Length > MaxTitleLength)
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }
        var status = request.Status ?? "planning";
        if (!Statuses.Contains(status))
        {
            throw ApiException.Validation($"Status must be one of {string.Join(", ", Statuses)}.");
        }
        if (request.TargetWords is <= 0)
        {
            throw ApiException.Validation("Target words must be more than 0.");
        }
        if (request.SeriesNumber is <= 0)
        {
            throw ApiException.Validation("The series number must be 1 or more.");
        }
        if (request.SeriesId == null && request.SeriesNumber != null)
        {
            throw ApiException.Validation("A series number needs a series.");
        }
        var subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim();

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            int? number = null;
            if (request.SeriesId is { } seriesId)
            {
                // Same lock order as series reorders: the series row, then its books by id. Concurrent creators
                // queue on the series row, so each one sees the previous book's number.
                await LockSeriesAsync(connection, transaction, seriesId, cancellationToken);
                var highest = await HighestNumberAsync(connection, transaction, seriesId, cancellationToken);
                number = request.SeriesNumber ?? highest + 1;
                if (request.SeriesNumber != null && await NumberTakenAsync(connection, transaction, seriesId, number.Value, cancellationToken))
                {
                    throw ApiException.Conflict($"Book {number} of this series already exists.");
                }
            }

            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO book (owner_id, title, subtitle, series_id, series_number, status, target_word_count)
                VALUES (@owner, @title, @subtitle, @seriesId, @number, @status, @targetWords)
                RETURNING id
                """,
                new { owner = currentUser.Id, title, subtitle, request.SeriesId, number, status, targetWords = request.TargetWords },
                transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            await events.PublishAsync(new BookMetaChanged(id, ["created"]));
            return new CreatedBookDto(id, title, subtitle, request.SeriesId, number, status, request.TargetWords);
        }
        catch (PostgresException e) when (e.SqlState == UniqueViolation)
        {
            // The (series, number) constraint is deferred: a clash that slipped past the check shows at commit.
            throw ApiException.Conflict("That book number is already taken in this series.");
        }
    }

    public async Task<NextSeriesNumberDto> NextNumberAsync(Guid seriesId, CancellationToken cancellationToken = default)
    {
        // Only the owner adds books to a series, so only the owner asks for its next number.
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, "Series not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner AND archived_at IS NULL)",
            new { seriesId, owner = currentUser.Id }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound("Series not found.");
        }
        var highest = await HighestNumberAsync(connection, null, seriesId, cancellationToken);
        return new NextSeriesNumberDto(seriesId, highest + 1);
    }

    private async Task LockSeriesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid seriesId, CancellationToken cancellationToken)
    {
        var locked = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner AND archived_at IS NULL FOR NO KEY UPDATE",
            new { seriesId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        if (locked == null)
        {
            throw ApiException.Validation("seriesId must be a series.");
        }
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM book WHERE series_id = @seriesId ORDER BY id FOR UPDATE",
            new { seriesId }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task<int> HighestNumberAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid seriesId, CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT coalesce(max(series_number), 0) FROM book WHERE series_id = @seriesId",
            new { seriesId }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task<bool> NumberTakenAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid seriesId, int number, CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE series_id = @seriesId AND series_number = @number)",
            new { seriesId, number }, transaction, cancellationToken: cancellationToken));
    }
}
