namespace Codex.Contracts.Sharing;

// Who could open which of BookIds, and at which role (BookRoles wire value), when IAccessChanges.CaptureAsync ran.
public sealed record AccessSnapshot(
    IReadOnlyCollection<Guid> BookIds,
    Guid? UserId,
    IReadOnlyDictionary<(Guid BookId, Guid UserId), string> Roles);
