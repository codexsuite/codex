using Agoda.IoC.Core;
using Codex.Application.Features.Ai;
using Codex.Application.Features.KnowledgeBase;
using Codex.Application.Features.Manuscript;
using Codex.Application.Features.References;

namespace Codex.Application.Mcp;

// Everything annotated prose and draft labels need about a book, from the queries the Write screen uses: beats
// (BlockCommands), Knowledge base additions (KbAdditionCommands), source markers (IReferenceQueries), entry names in
// scope (IKbQueries) and the pending drafts (DraftQueries).
[RegisterSingleton]
public sealed class ManuscriptContexts(
    BlockCommands blocks, KbAdditionCommands additions, IReferenceQueries references, IKbQueries kb, DraftQueries drafts)
{
    public async Task<ManuscriptText.Context> ForBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var beats = await blocks.ListAsync(bookId, cancellationToken);
        var additionList = await additions.ListAsync(bookId, cancellationToken);
        var markers = await references.GetMarkersAsync(bookId, cancellationToken);
        var entries = await kb.GetBookKbAsync(bookId, cancellationToken);
        var pending = await drafts.ListAsync(bookId, cancellationToken);
        return new ManuscriptText.Context(
            beats.Beats.ToDictionary(b => b.Id),
            additionList.Additions.ToDictionary(a => a.Id),
            entries.Entries.ToDictionary(e => e.Id, e => e.Name),
            markers.GroupBy(m => m.UseId).ToDictionary(g => g.Key, g => g.First()),
            pending.Drafts);
    }
}
