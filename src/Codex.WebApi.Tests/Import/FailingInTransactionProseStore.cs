using Codex.Contracts.Manuscript;

namespace Codex.WebApi.Tests.Import;

// The real ProseStore, except that a batch write fails after its rows and prose are written, just before commit: the
// shape of a late failure inside an import's one transaction.
public sealed class FailingInTransactionProseStore(IProseStore inner) : IProseStore
{
    public Task<ProseDto> GetAsync(Guid sceneId, CancellationToken cancellationToken = default) => inner.GetAsync(sceneId, cancellationToken);

    public Task<int> UpdateAsync(Guid sceneId, Func<string, string> edit, int expectedVersion, string origin, CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(sceneId, edit, expectedVersion, origin, cancellationToken);

    public Task<int> UpdateAsync(Guid sceneId, Func<string, string> edit, int expectedVersion, string origin, ProseWriteOptions options, CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(sceneId, edit, expectedVersion, origin, options, cancellationToken);

    public Task<IReadOnlyList<int>> UpdateManyAsync(IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits, string origin, CancellationToken cancellationToken = default) =>
        inner.UpdateManyAsync(edits, origin, cancellationToken);

    public Task<IReadOnlyList<int>> UpdateManyAsync(
        IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits, string origin, ProseWriteOptions options, CancellationToken cancellationToken = default) =>
        inner.UpdateManyAsync(edits, origin, options with
        {
            InTransaction = async (connection, transaction, ct) =>
            {
                if (options.InTransaction != null)
                {
                    await options.InTransaction(connection, transaction, ct);
                }
                throw new InvalidOperationException("disk full (test double)");
            },
        }, cancellationToken);
}
