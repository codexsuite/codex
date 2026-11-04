using Codex.Contracts.References;

namespace Codex.Application.Features.References;

public interface ISourceQueries
{
    Task<IReadOnlyList<SourceRowDto>> GetSourcesAsync(Guid bookId, CancellationToken cancellationToken);

    // Cited sources only, in marker order: what the bibliography prints.
    Task<IReadOnlyList<SourceCslDto>> GetCslAsync(Guid bookId, CancellationToken cancellationToken);

    // format: bibtex, ris, csljson or permissions. Unknown format is a validation error.
    Task<SourceExportFile> ExportAsync(Guid bookId, string? format, CancellationToken cancellationToken);
}
