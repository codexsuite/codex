using Codex.Contracts.References;

namespace Codex.Application.Features.References;

// Adding sources to a book: look an identifier up, add one source, import many. Unknown or archived book 404.
public interface IAddSourceCommands
{
    Task<LookupSourceResultDto> LookupAsync(Guid bookId, LookupSourceRequest request, CancellationToken cancellationToken);

    Task<AddSourceResult> AddAsync(Guid bookId, AddSourceRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);

    Task<ImportSourcesResult> ImportAsync(Guid bookId, ImportSourcesRequest request, CancellationToken cancellationToken);

    // What ImportAsync would do, written nowhere.
    Task<ImportSourcesResult> PreviewImportAsync(Guid bookId, ImportSourcesRequest request, CancellationToken cancellationToken);
}
