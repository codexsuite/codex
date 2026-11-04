using Agoda.IoC.Core;

namespace Codex.Application.Mcp;

// What each MCP session last saw a book's display numbers mean ("5.2" → scene id), so a number that has since moved
// to something else fails with stale_ref instead of silently naming the other scene (#20 2.1, M11). Every tool that
// reads an outline records it (McpRefs.OutlineAsync). Sessions are kept in memory, the least recently used dropped past
// MaxSessions; a forgotten session simply has nothing to be stale against.
[RegisterSingleton]
public sealed class McpRefMemory
{
    private const int MaxSessions = 256;

    // Per session: when it was last used, and per book the numbers it saw.
    private readonly Dictionary<string, (long LastUsed, Dictionary<Guid, IReadOnlyDictionary<string, Guid>> Books)> _sessions =
        new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _tick;

    // Returns what the session saw for the book before, and records `numbers` as what it sees now.
    public IReadOnlyDictionary<string, Guid>? Exchange(string sessionKey, Guid bookId, IReadOnlyDictionary<string, Guid> numbers)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionKey, out var session))
            {
                if (_sessions.Count >= MaxSessions)
                {
                    _sessions.Remove(_sessions.MinBy(s => s.Value.LastUsed).Key);
                }
                session = (0, []);
            }
            _sessions[sessionKey] = (++_tick, session.Books);
            session.Books.TryGetValue(bookId, out var previous);
            session.Books[bookId] = numbers;
            return previous;
        }
    }
}
