namespace Codex.Contracts.Ai;

// One streamed piece of a draft. Text chunks carry `Text` and no `Final`; the last chunk of a completed
// stream has empty `Text` and the totals in `Final`. A stopped or failed stream has no final chunk.
public sealed record ProseChunk(string Text, ProseResult? Final = null)
{
    public static ProseChunk Done(ProseResult result) => new(string.Empty, result);
}
