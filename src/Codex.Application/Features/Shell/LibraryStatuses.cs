namespace Codex.Application.Features.Shell;

// Status values the filter accepts: book.status plus "archived".
public static class LibraryStatuses
{
    public const string Archived = "archived";

    public static readonly IReadOnlyList<string> All = ["planning", "draft", "writing", "revising", "published", Archived];
}
