using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Ai.Providers;

// Reads an event-stream body one line at a time and fails the draft the moment more than `limit` bytes have arrived,
// including inside a line that has not ended yet (StreamReader.ReadLineAsync would buffer a line of any length first).
public sealed class SseLineReader(Stream body, int limit, string provider) : IAsyncDisposable
{
    private readonly PipeReader _reader = PipeReader.Create(body);
    private long _consumed;

    // The next line without its line break, or null at the end of the stream.
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;
            if (_consumed + buffer.Length > limit)
            {
                throw ProviderFailures.Unavailable($"{provider} sent more than this app accepts. Try a shorter draft.");
            }

            var end = buffer.PositionOf((byte)'\n');
            if (end is { } found)
            {
                var line = Decode(buffer.Slice(0, found));
                var next = buffer.GetPosition(1, found);
                _consumed += buffer.Slice(0, next).Length;
                _reader.AdvanceTo(next);
                return line;
            }

            if (result.IsCompleted)
            {
                var rest = buffer.IsEmpty ? null : Decode(buffer);
                _reader.AdvanceTo(buffer.End);
                return rest;
            }

            _reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    public ValueTask DisposeAsync() => _reader.CompleteAsync();

    private static string Decode(ReadOnlySequence<byte> line)
    {
        var text = Encoding.UTF8.GetString(line);
        return text.EndsWith('\r') ? text[..^1] : text;
    }
}
