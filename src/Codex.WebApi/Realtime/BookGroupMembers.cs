using System.Collections.Concurrent;

namespace Codex.WebApi.Realtime;

// Which of a user's hub connections joined which book, so the relay can drop them from a book's group when the user's
// access to it ends (#80). Groups know their connections but not their users; this keeps the other half, per process,
// as the groups themselves are.
public sealed class BookGroupMembers
{
    private readonly ConcurrentDictionary<(string ConnectionId, Guid BookId), Guid> _joined = new();

    public void Joined(string connectionId, Guid userId, Guid bookId) => _joined[(connectionId, bookId)] = userId;

    public void Left(string connectionId, Guid bookId) => _joined.TryRemove((connectionId, bookId), out _);

    public void Disconnected(string connectionId)
    {
        foreach (var key in _joined.Keys.Where(k => k.ConnectionId == connectionId))
        {
            _joined.TryRemove(key, out _);
        }
    }

    // Forgets and returns userId's connections that joined bookId.
    public IReadOnlyList<string> Remove(Guid userId, Guid bookId)
    {
        var removed = new List<string>();
        foreach (var (key, user) in _joined)
        {
            if (key.BookId == bookId && user == userId && _joined.TryRemove(key, out _))
            {
                removed.Add(key.ConnectionId);
            }
        }
        return removed;
    }
}
