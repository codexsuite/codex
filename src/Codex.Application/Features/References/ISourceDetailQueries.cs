using Codex.Contracts.References;
using Npgsql;

namespace Codex.Application.Features.References;

public interface ISourceDetailQueries
{
    // Unknown or archived source: ApiException.NotFound.
    Task<SourceDetailDto> GetAsync(Guid sourceId, CancellationToken cancellationToken);

    Task<SourceDetailDto> GetAsync(NpgsqlConnection connection, Guid sourceId, CancellationToken cancellationToken);
}
