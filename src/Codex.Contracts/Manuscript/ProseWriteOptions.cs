using System.Data.Common;

namespace Codex.Contracts.Manuscript;

// Extra work an IProseStore write does inside its own transaction, so a feature's rows change together with the prose.
public sealed record ProseWriteOptions
{
    // Stamped on the prose_revision row the write cuts (origins "import", "manual", "ai_apply", "restore"); ignored
    // for other origins, which cut no tagged revision.
    public Guid? GenerationId { get; init; }

    // R6: a save whose expected version is behind the stored one is not a conflict; it wins, and the text it replaces
    // is kept as a 'replaced' revision. Only the editor's own save (PUT prose) sets this; every other writer computes
    // its edit from the body it read, so it still gets Conflict.
    public bool LastWriteWins { get; init; }

    // Runs first on the store's connection and transaction, before the scene rows are locked and their versions
    // checked: a command creates the rows the new bodies point at (a beat, a Codex addition, a reference use, a new
    // scene) so the prose triggers accept them. If it throws, nothing is saved.
    public Func<DbConnection, DbTransaction, CancellationToken, Task>? BeforeWrites { get; init; }

    // Runs on the store's connection and transaction after every prose row is written (and its triggers have run) and
    // before the commit. If it throws, nothing is saved and no ProseSaved is published.
    public Func<DbConnection, DbTransaction, CancellationToken, Task>? InTransaction { get; init; }
}
