namespace Codex.Application.Features.Shell;

public static class LibraryGroups
{
    public const string Series = "series";
    public const string Status = "status";
    public const string None = "none";

    public static readonly IReadOnlyList<string> All = [Series, Status, None];
}
