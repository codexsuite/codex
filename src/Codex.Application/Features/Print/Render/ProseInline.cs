using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Print.Render;

// The inline grammar of docs/prose-format.md, ported from src/prose/markdown/inlineParse.ts (variant prose: links read,
// a newline is a soft break): backslash escapes, code spans, `*`/`_` emphasis by CommonMark's delimiter algorithm with
// the whitespace-only flanking rule and the rule of three, [x](kb:id) mentions, [x](chapter:id) and [x](ref:id) marks,
// and [^](ref:id) markers, ~~strike~~, [x](https://…) external links and `source`{=latex} raw LaTeX spans (#64).
// Anything else is text. Pure.
public static partial class ProseInline
{
    private const string RawLatexSuffix = "{=latex}";
    private const string FenceBreak = "<!---->";
    private const string AsciiPunctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    [GeneratedRegex("^\\((kb|chapter|ref):([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex("^\\((https?://[^\\s)]+|mailto:[^\\s)]+)\\)")]
    private static partial Regex ExternalTarget();

    public static IReadOnlyList<InlineLeaf> Parse(string source) => Flatten(ResolveEmphasis(Tokenize(source)), []);

    private interface IToken
    {
    }

    private sealed record TextToken(string Value) : IToken;

    private sealed record LeavesToken(IReadOnlyList<InlineLeaf> Leaves) : IToken;

    private sealed record BreakToken : IToken
    {
    }

    private sealed record StyleToken(InlineMarkKind Style, List<IToken> Children) : IToken;

    // Count shrinks as emphasis consumes the run; Orig keeps the run's length for the rule of three.
    private sealed record DelimToken(char Ch, int Orig, bool CanOpen, bool CanClose) : IToken
    {
        public int Count { get; set; } = Orig;
    }

    private static List<IToken> Tokenize(string s)
    {
        var tokens = new List<IToken>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0)
            {
                tokens.Add(new TextToken(text.ToString()));
                text.Clear();
            }
        }

        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length && AsciiPunctuation.Contains(s[i + 1]))
            {
                text.Append(s[i + 1]);
                i += 2;
            }
            else if (c == '\n')
            {
                Flush();
                tokens.Add(new BreakToken());
                i++;
            }
            else if (c == '`')
            {
                var run = RunLength(s, i, '`');
                var close = FindBacktickRun(s, i + run, run);
                if (close < 0)
                {
                    text.Append('`', run);
                    i += run;
                }
                else
                {
                    Flush();
                    var source = Unpad(s[(i + run)..close]);
                    i = close + run;
                    if (string.CompareOrdinal(s, i, RawLatexSuffix, 0, RawLatexSuffix.Length) == 0)
                    {
                        tokens.Add(new LeavesToken([new InlineLeaf(InlineLeafKind.RawLatex, source, [])]));
                        i += RawLatexSuffix.Length;
                    }
                    else
                    {
                        tokens.Add(new LeavesToken([new InlineLeaf(InlineLeafKind.Text, source, [new InlineMark(InlineMarkKind.Code)])]));
                    }
                }
            }
            else if (c == '<' && text.Length == 0 && tokens.Count > 0 && tokens[^1] is LeavesToken && BetweenFences(s, i))
            {
                i += FenceBreak.Length;
            }
            else if (c == '[' && ReadLink(s, i) is { } link)
            {
                Flush();
                tokens.Add(new LeavesToken(link.Leaves));
                i = link.End;
            }
            else if (c == '~' && RunLength(s, i, '~') != 2)
            {
                // Only a run of exactly two tildes is a strike delimiter.
                var run = RunLength(s, i, '~');
                text.Append('~', run);
                i += run;
            }
            else if (c == '~')
            {
                var prev = i > 0 ? s[i - 1] : (char?)null;
                var next = i + 2 < s.Length ? s[i + 2] : (char?)null;
                Flush();
                tokens.Add(new DelimToken('~', 2, next is { } n && !char.IsWhiteSpace(n), prev is { } p && !char.IsWhiteSpace(p)));
                i += 2;
            }
            else if (c is '*' or '_')
            {
                var run = RunLength(s, i, c);
                var prev = i > 0 ? s[i - 1] : (char?)null;
                var next = i + run < s.Length ? s[i + run] : (char?)null;
                Flush();
                tokens.Add(new DelimToken(c, run, next is { } n && !char.IsWhiteSpace(n), prev is { } p && !char.IsWhiteSpace(p)));
                i += run;
            }
            else
            {
                text.Append(c);
                i++;
            }
        }

        Flush();
        return tokens;
    }

    // CommonMark: one space on both sides of a span's text is padding, unless the text is only spaces.
    private static string Unpad(string source) =>
        source.Length > 1 && source[0] == ' ' && source[^1] == ' ' && source.Trim().Length > 0 ? source[1..^1] : source;

    // `<!---->` right after a closing fence and before an opening one keeps the two apart; it is nothing else.
    private static bool BetweenFences(string s, int at) =>
        string.CompareOrdinal(s, at, FenceBreak, 0, FenceBreak.Length) == 0
        && s[at - 1] == '`'
        && at + FenceBreak.Length < s.Length
        && s[at + FenceBreak.Length] == '`';

    private static int RunLength(string s, int start, char ch)
    {
        var n = 0;
        while (start + n < s.Length && s[start + n] == ch)
        {
            n++;
        }

        return n;
    }

    private static int FindBacktickRun(string s, int from, int length)
    {
        var i = from;
        while (i < s.Length)
        {
            if (s[i] == '`')
            {
                var run = RunLength(s, i, '`');
                if (run == length)
                {
                    return i;
                }

                i += run;
            }
            else
            {
                i++;
            }
        }

        return -1;
    }

    private static (IReadOnlyList<InlineLeaf> Leaves, int End)? ReadLink(string s, int start)
    {
        var close = FindClosingBracket(s, start);
        if (close < 0)
        {
            return null;
        }

        var after = s[(close + 1)..Math.Min(s.Length, close + 1 + 300)];
        var external = ExternalTarget().Match(after);
        if (external.Success && close > start + 1)
        {
            var link = new InlineMark(InlineMarkKind.Link, null, external.Groups[1].Value);
            return (Parse(s[(start + 1)..close]).Select(leaf => leaf.Kind == InlineLeafKind.Break ? leaf : leaf with { Marks = [link, .. leaf.Marks] }).ToList(), close + 1 + external.Length);
        }

        var target = LinkTarget().Match(after);
        if (!target.Success)
        {
            return null;
        }

        var scheme = target.Groups[1].Value;
        var id = Guid.Parse(target.Groups[2].Value);
        var inner = s[(start + 1)..close];
        var end = close + 1 + target.Length;
        if (scheme == "kb")
        {
            return ([new InlineLeaf(InlineLeafKind.Mention, Unescape(inner), [], id)], end);
        }

        if (scheme == "ref" && inner == "^")
        {
            return ([new InlineLeaf(InlineLeafKind.RefMarker, string.Empty, [], id)], end);
        }

        var mark = new InlineMark(scheme == "ref" ? InlineMarkKind.Ref : InlineMarkKind.ChapterLink, id);
        return (Parse(inner).Select(leaf => leaf.Kind == InlineLeafKind.Break ? leaf : leaf with { Marks = [.. leaf.Marks, mark] }).ToList(), end);
    }

    private static int FindClosingBracket(string s, int start)
    {
        var depth = 0;
        var i = start;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\')
            {
                i += 2;
            }
            else if (c == '`')
            {
                var run = RunLength(s, i, '`');
                var close = FindBacktickRun(s, i + run, run);
                i = close < 0 ? i + run : close + run;
            }
            else if (c == '[')
            {
                if (depth > 0)
                {
                    return -1;
                }

                depth++;
                i++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }

                i++;
            }
            else
            {
                i++;
            }
        }

        return -1;
    }

    private static string Unescape(string source)
    {
        var text = new StringBuilder();
        var i = 0;
        while (i < source.Length)
        {
            var escaped = source[i] == '\\' && i + 1 < source.Length && AsciiPunctuation.Contains(source[i + 1]);
            text.Append(source[escaped ? i + 1 : i]);
            i += escaped ? 2 : 1;
        }

        return text.ToString();
    }

    private static List<IToken> ResolveEmphasis(List<IToken> tokens)
    {
        var closerIndex = 0;
        while (closerIndex < tokens.Count)
        {
            if (tokens[closerIndex] is not DelimToken { CanClose: true } closer)
            {
                closerIndex++;
                continue;
            }

            var openerIndex = closerIndex - 1;
            while (openerIndex >= 0 && !CanMatch(tokens[openerIndex], closer))
            {
                openerIndex--;
            }

            if (openerIndex < 0)
            {
                closerIndex++;
                continue;
            }

            var opener = (DelimToken)tokens[openerIndex];
            var used = opener.Ch == '~' || opener.Count >= 2 && closer.Count >= 2 ? 2 : 1;
            var children = tokens.GetRange(openerIndex + 1, closerIndex - openerIndex - 1);
            tokens.RemoveRange(openerIndex + 1, closerIndex - openerIndex - 1);
            tokens.Insert(openerIndex + 1, new StyleToken(StyleOf(opener.Ch, used), ResolveEmphasis(children)));
            opener.Count -= used;
            closer.Count -= used;
            closerIndex = openerIndex + 2;
            if (closer.Count == 0)
            {
                tokens.RemoveAt(closerIndex);
            }

            if (opener.Count == 0)
            {
                tokens.RemoveAt(openerIndex);
                closerIndex--;
            }
        }

        return tokens;
    }

    private static InlineMarkKind StyleOf(char delimiter, int used)
    {
        if (delimiter == '~')
        {
            return InlineMarkKind.Strike;
        }

        return used == 2 ? InlineMarkKind.Bold : InlineMarkKind.Italic;
    }

    private static bool CanMatch(IToken candidate, DelimToken closer)
    {
        if (candidate is not DelimToken opener || opener.Ch != closer.Ch || !opener.CanOpen || opener.Count == 0)
        {
            return false;
        }

        var bothFlank = opener.CanClose || closer.CanOpen;
        var sum = opener.Orig + closer.Orig;
        return !(bothFlank && sum % 3 == 0 && !(opener.Orig % 3 == 0 && closer.Orig % 3 == 0));
    }

    private static List<InlineLeaf> Flatten(List<IToken> tokens, IReadOnlyList<InlineMark> marks)
    {
        var leaves = new List<InlineLeaf>();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken t:
                    leaves.Add(new InlineLeaf(InlineLeafKind.Text, t.Value, marks));
                    break;
                case DelimToken { Count: > 0 } d:
                    leaves.Add(new InlineLeaf(InlineLeafKind.Text, new string(d.Ch, d.Count), marks));
                    break;
                case BreakToken:
                    leaves.Add(new InlineLeaf(InlineLeafKind.Break, string.Empty, []));
                    break;
                case LeavesToken l:
                    leaves.AddRange(l.Leaves.Select(leaf => leaf.Kind == InlineLeafKind.Break ? leaf : leaf with { Marks = [.. marks, .. leaf.Marks] }));
                    break;
                case StyleToken s:
                    leaves.AddRange(Flatten(s.Children, [.. marks, new InlineMark(s.Style)]));
                    break;
            }
        }

        return leaves;
    }
}
