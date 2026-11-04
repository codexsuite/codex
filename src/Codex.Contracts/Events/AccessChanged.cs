namespace Codex.Contracts.Events;

// A user's access to a book changed (#80): a grant on the book or its series was added, changed or removed. Role is
// the user's effective role on the book afterwards ("view", "comment", "write"), null when they can no longer open
// it. The hub sends it to that user's connections only, not to the book's group, and drops them from the group when
// Role is null. A change to a series grant publishes one per book in the series, plus one for the series row.
public sealed record AccessChanged(Guid BookId, Guid UserId, string? Role) : BookEvent(BookId);
