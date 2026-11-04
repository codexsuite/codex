using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Jobs;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Jobs;

public interface IJobQueries
{
    Task<JobDto> GetJobAsync(Guid jobId, CancellationToken cancellationToken);
}

// Jobs are export_job rows. succeeded reads as done; cancelled reads as failed with the message "Cancelled".
[RegisterSingleton]
public sealed class JobQueries(NpgsqlDataSource dataSource, IBookAccess access) : IJobQueries
{
    public async Task<JobDto> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.ExportJob, jobId, BookRole.Write, cancellationToken, $"Job {jobId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<JobDto>(new CommandDefinition(
            """
            SELECT id, book_id AS BookId, kind,
                   CASE status WHEN 'succeeded' THEN 'done' WHEN 'cancelled' THEN 'failed' ELSE status END AS State,
                   CASE WHEN status = 'succeeded' THEN 100
                        ELSE round(coalesce(progress, 0) * 100)::integer END AS Percent,
                   CASE WHEN status = 'cancelled' THEN 'Cancelled' ELSE coalesce(error, step) END AS Message,
                   CASE WHEN status = 'succeeded' THEN '/api/assets/' || output_asset_id END AS ResultUrl,
                   created_at AS CreatedAt,
                   coalesce(finished_at, started_at, created_at) AS UpdatedAt
            FROM export_job WHERE id = @jobId
            """,
            new { jobId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Job {jobId} was not found.");
    }
}
