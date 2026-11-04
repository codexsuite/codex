using System.Text;
using Codex.Contracts.Prompts;

namespace Codex.Application.Features.Ai;

// What the model is asked, built from the beat (or the scene's end) and the Knowledge base entries sent as context.
internal static class DraftPrompt
{
    private const int PriorProseLimit = 6000;

    internal sealed record Entry(string Name, string? Summary);

    public static string Instruction(string? templateInstruction, string? styleGuide)
    {
        var parts = new[] { templateInstruction?.Trim(), string.IsNullOrWhiteSpace(styleGuide) ? null : "Style guide:\n" + styleGuide.Trim() };
        return string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    // A user's template: its body with the variables filled in is the whole prompt (there is no separate instruction).
    public static string FromTemplate(string body, string? beat, int targetWords, IReadOnlyList<Entry> context, string priorProse, string? styleGuide)
        => PromptVariables.Render(body, new Dictionary<string, string>
        {
            [PromptVariables.Beat] = string.IsNullOrWhiteSpace(beat) ? "Continue the scene." : beat.Trim(),
            [PromptVariables.Context] = ContextText(context),
            [PromptVariables.StyleGuide] = string.IsNullOrWhiteSpace(styleGuide) ? "" : styleGuide.Trim(),
            [PromptVariables.PreviousProse] = Prior(priorProse),
            [PromptVariables.Length] = targetWords.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    private static string ContextText(IReadOnlyList<Entry> context)
    {
        if (context.Count == 0)
        {
            return "";
        }

        var text = new StringBuilder("Codex:");
        foreach (var entry in context)
        {
            text.AppendLine().Append(string.IsNullOrWhiteSpace(entry.Summary) ? $"- {entry.Name}" : $"- {entry.Name}: {entry.Summary.Trim()}");
        }

        return text.ToString();
    }

    private static string Prior(string priorProse)
    {
        var prior = priorProse.Trim();
        return prior.Length > PriorProseLimit ? prior[^PriorProseLimit..] : prior;
    }

    // `beat` is null for a continuation. `priorProse` is everything in the scene before the point being written.
    public static string Prompt(string? beat, int targetWords, IReadOnlyList<Entry> context, string priorProse)
    {
        var text = new StringBuilder();
        if (context.Count > 0)
        {
            text.AppendLine(ContextText(context)).AppendLine();
        }

        var prior = Prior(priorProse);
        if (prior.Length > 0)
        {
            text.AppendLine("The scene so far:").AppendLine(prior).AppendLine();
        }

        text.AppendLine(beat == null ? "Continue the scene." : $"Write the next beat: {beat.Trim()}");
        text.Append($"Target length: about {targetWords} words.");
        return text.ToString();
    }
}
