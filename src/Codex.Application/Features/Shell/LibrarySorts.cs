namespace Codex.Application.Features.Shell;

public static class LibrarySorts
{
    public const string LastEdited = "lastEdited";
    public const string Title = "title";
    public const string Series = "series";
    public const string Progress = "progress";

    public static readonly IReadOnlyList<string> All = [LastEdited, Title, Series, Progress];
}
