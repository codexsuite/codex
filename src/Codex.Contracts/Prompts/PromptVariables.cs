using System.Text;

namespace Codex.Contracts.Prompts;

// The names a user template body may use, and how a body becomes the prompt of one draft.
public static class PromptVariables
{
    public const string Beat = "beat";
    public const string Context = "context";
    public const string StyleGuide = "style_guide";
    public const string PreviousProse = "previous_prose";
    public const string Length = "length";

    public static readonly IReadOnlyList<string> All = [Beat, Context, StyleGuide, PreviousProse, Length];

    // What a copy of a built-in template starts as: its instruction, then the variables a draft needs.
    public static string Skeleton(string instruction)
        => new StringBuilder(instruction.Trim())
            .Append("\n\n{{style_guide}}\n\n{{context}}\n\n{{previous_prose}}\n\nWrite the next beat: {{beat}}\nTarget length: about {{length}} words.")
            .ToString();

    public static bool UsesBeat(string body) => body.Contains("{{" + Beat + "}}", StringComparison.Ordinal);

    // Fills {{name}} (spaces inside the braces allowed); a name that is not a variable stays as written.
    public static string Render(string body, IReadOnlyDictionary<string, string> values)
    {
        var text = new StringBuilder(body);
        foreach (var (name, value) in values)
        {
            foreach (var form in new[] { "{{" + name + "}}", "{{ " + name + " }}" })
            {
                text.Replace(form, value);
            }
        }

        return System.Text.RegularExpressions.Regex.Replace(text.ToString(), @"\n{3,}", "\n\n", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
    }
}
