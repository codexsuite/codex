namespace Codex.Contracts.Import;

// The Codex backup .zip (the source export in Settings → Archive & export, read by Library → Import → Codex backup).
// Version is written to book.json as formatVersion; a backup with a higher one was made by a newer Codex.
public static class BackupFormat
{
    public const int Version = 1;
}
