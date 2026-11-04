namespace Codex.Contracts.Sharing;

// Publishes AccessChanged (#80) for whatever a command did to who can open which book: a grant added, changed or
// removed, a book archived (its grants go) or moved between series (inherited access moves with it), a series
// deleted. Capture the books' access before the change; after it commits, PublishAsync reads book_access again and
// raises one AccessChanged per (book, user) whose effective role differs, with the new role or null.
public interface IAccessChanges
{
    // userId narrows the snapshot to one user (a grant change); null takes everyone with access to those books.
    Task<AccessSnapshot> CaptureAsync(IReadOnlyCollection<Guid> bookIds, Guid? userId, CancellationToken cancellationToken = default);

    Task PublishAsync(AccessSnapshot before, CancellationToken cancellationToken = default);
}
