using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Prose;

// The pipe-table rows of docs/prose-format.md (#64), as both readers split them: a row starts and ends with `|`, a cell
// is the text between two pipes that no backslash precedes (`\\|` is not a split either, as in the TypeScript reader), and `\|`
// inside a cell is a literal pipe. Pure.
public static partial class ProseTable
{
    [GeneratedRegex("^:?-+:?$")]
    private static partial Regex DelimiterCell();

    // The raw cells of a row (still escaped, trimmed). A missing closing pipe still ends the last cell.
    public static IReadOnlyList<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var text = line.Trim();
        var i = text.StartsWith('|') ? 1 : 0;
        for (; i < text.Length; i++)
        {
            if (text[i] == '|' && (i == 0 || text[i - 1] != '\\'))
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(text[i]);
            }
        }

        if (cell.ToString().Trim().Length > 0)
        {
            cells.Add(cell.ToString().Trim());
        }

        return cells;
    }

    // `\|` becomes `|`; every other escape is left for the inline reader.
    public static string CellText(string cell) => cell.Replace("\\|", "|", StringComparison.Ordinal);

    public static bool IsDelimiterRow(string line, int columns)
    {
        var cells = SplitRow(line);
        return line.StartsWith('|') && cells.Count == columns && cells.All(c => DelimiterCell().IsMatch(c));
    }

    // One entry per delimiter cell: "left", "center", "right" or null.
    public static IReadOnlyList<string?> Alignments(string delimiterRow) => SplitRow(delimiterRow).Select(Alignment).ToList();

    private static string? Alignment(string cell)
    {
        var left = cell.StartsWith(':');
        var right = cell.EndsWith(':');
        if (left && right)
        {
            return "center";
        }

        if (left)
        {
            return "left";
        }

        return right ? "right" : null;
    }
}
