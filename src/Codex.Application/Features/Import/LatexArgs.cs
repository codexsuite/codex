namespace Codex.Application.Features.Import;

// Reads the arguments of a LaTeX command: [optional] and {required} groups, balanced, with a backslash escaping the
// next character.
internal static class LatexArgs
{
    // The end (exclusive) of the balanced group opening at text[start], or -1.
    public static int GroupEnd(string text, int start)
    {
        var (open, close) = text[start] == '[' ? ('[', ']') : ('{', '}');
        var depth = 0;
        var i = start;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close && --depth == 0)
            {
                return i + 1;
            }
            i++;
        }
        return -1;
    }

    // The end of the command at text[start] (a backslash and its letters) with the groups that follow it directly
    // (or after whitespace, when skipSpace). An unbalanced group is left out.
    public static int CommandEnd(string text, int start, bool skipSpace = false)
    {
        var i = start + 1;
        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            i++;
        }
        var end = i;
        while (true)
        {
            var next = i;
            while (skipSpace && next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }
            var group = next < text.Length && text[next] is '[' or '{' ? GroupEnd(text, next) : -1;
            if (group < 0)
            {
                return end;
            }
            i = end = group;
        }
    }

    // The contents of the groups after a command name, in order ([ and { alike).
    public static IReadOnlyList<string> Groups(string text, int from, int to)
    {
        var groups = new List<string>();
        var i = from;
        while (i < to)
        {
            if (text[i] is '[' or '{')
            {
                var end = GroupEnd(text, i);
                groups.Add(text[(i + 1)..(end - 1)]);
                i = end;
            }
            else
            {
                i++;
            }
        }
        return groups;
    }
}
