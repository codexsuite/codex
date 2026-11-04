namespace Codex.Contracts.Ai;

// Streams a draft. Stop is cancellation: cancelling the token ends the stream with OperationCanceledException
// and the caller keeps the chunks it already received. A provider failure surfaces as an exception from the
// enumeration, after the chunks that were produced before it.
public interface IProseGenerator
{
    IAsyncEnumerable<ProseChunk> GenerateAsync(ProseRequest request, CancellationToken cancellationToken);
}
