namespace Codex.Application.Identity;

// The avatar's letters: the first letters of the display name's first and last words ("Joel Dickson" -> "JD").
public static class UserInitials
{
    public static string From(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "",
            1 => words[0][..1].ToUpperInvariant(),
            _ => $"{words[0][0]}{words[^1][0]}".ToUpperInvariant(),
        };
    }
}
