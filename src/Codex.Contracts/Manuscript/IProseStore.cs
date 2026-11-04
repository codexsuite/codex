namespace Codex.Contracts.Manuscript;

// The one server-side way to read and edit a scene's prose. Features other than Manuscript (AI apply, find and
// replace, restore) call UpdateAsync instead of writing prose themselves.
public interface IProseStore
{
    // Throws ApiException.NotFound for an unknown scene.
    Task<ProseDto> GetAsync(Guid sceneId, CancellationToken cancellationToken = default);

    // Runs edit(currentBody) and saves the result in one transaction with the scene row locked. Throws
    // ApiException.Conflict when the stored version is not expectedVersion (the caller reloads and retries; two
    // concurrent edits at the same version never both win), and ApiException.NotFound for an unknown scene. If edit
    // throws, nothing is saved. Returns the new version and publishes ProseSaved. Origin names the writer ("editor",
    // "api", "mcp", "ai_apply", "restore", "manual", "import"); the last four also cut a prose_revision row of that
    // source, any other origin cuts an autosave revision when the scene has had none for ten minutes.
    Task<int> UpdateAsync(
        Guid sceneId,
        Func<string, string> edit,
        int expectedVersion,
        string origin,
        CancellationToken cancellationToken = default);

    // As UpdateAsync, with `options` (a generation id for the revision, steps that run in the same transaction).
    Task<int> UpdateAsync(
        Guid sceneId,
        Func<string, string> edit,
        int expectedVersion,
        string origin,
        ProseWriteOptions options,
        CancellationToken cancellationToken = default);

    // Edits several scenes in one transaction: the scene rows are locked in ascending id order, every expected
    // version is checked before anything is written (a stale one throws Conflict and nothing changes), and one
    // ProseSaved per scene is published after the commit. Returns the new versions in the order of `edits`.
    Task<IReadOnlyList<int>> UpdateManyAsync(
        IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits,
        string origin,
        CancellationToken cancellationToken = default);

    // As UpdateManyAsync, with `options`. With no edits it still runs the options' steps in a transaction.
    Task<IReadOnlyList<int>> UpdateManyAsync(
        IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits,
        string origin,
        ProseWriteOptions options,
        CancellationToken cancellationToken = default);
}
