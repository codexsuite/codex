using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts.Manuscript;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

// PUT /api/scenes/{id}/prose: the editor's save. Every body, from Rich or Source, is checked against the scene's own
// rows (EditorProseRules) inside the store's transaction, after its ownership check. A stale version still saves
// (last write wins, R6): the store keeps the replaced text as a revision.
[RegisterSingleton]
public sealed class EditorProseSave(IProseStore prose)
{
    private const string EditorOrigin = "editor";

    public async Task<SaveProseResult> SaveAsync(Guid sceneId, SaveProseRequest request, CancellationToken cancellationToken)
    {
        // A missing body is refused by the store ("Prose body is required."), after its ownership check.
        IReadOnlyList<(string Kind, Guid Id)> owned = [];
        var options = new ProseWriteOptions
        {
            LastWriteWins = true,
            BeforeWrites = async (connection, transaction, ct) =>
            {
                if (request.Body != null)
                {
                    owned = await EditorProseRules.OwnedIdsAsync((NpgsqlConnection)connection, (NpgsqlTransaction)transaction, sceneId, request.Body, ct);
                }
            },
        };
        var version = await prose.UpdateAsync(sceneId, current => Edited(current, request.Body, owned), request.Version, EditorOrigin, options, cancellationToken);
        return new SaveProseResult(version);
    }

    private static string Edited(string current, string? body, IReadOnlyList<(string Kind, Guid Id)> owned)
    {
        if (body != null)
        {
            EditorProseRules.RefuseForeign(current, body, owned);
        }
        return body!;
    }
}
