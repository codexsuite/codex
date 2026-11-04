using System.Globalization;
using System.Text;

namespace Codex.Application.Features.References;

// What a request letter is drafted from: the source, the uses that need permission, and the book's letter settings.
// A setting that is not filled in leaves its sentence out.
public sealed record PermissionLetterFacts(
    string? RightsHolder,
    string Title,
    string Authors,
    int? Year,
    string? SourcePublisher,
    string BookTitle,
    string? BookAuthor,
    int QuotedWords,
    int LyricWords,
    int Figures,
    IReadOnlyList<string> Locators,
    IReadOnlyList<int> Chapters,
    string? Publisher,
    int? PlannedYear,
    int? PrintRun,
    IReadOnlyList<string> Formats,
    string? Territory);

public static class PermissionLetter
{
    public static string Draft(PermissionLetterFacts f)
    {
        var letter = new StringBuilder();
        var holder = Present(f.RightsHolder) ?? Present(f.SourcePublisher) ?? "Rights Holder";
        letter.Append(CultureInfo.InvariantCulture, $"Dear {holder},\n\n");

        var work = f.Year is null ? $"*{f.Title}*" : $"*{f.Title}* ({f.Year})";
        var by = Present(f.Authors) is { } authors ? $" by {authors}" : string.Empty;
        var inBook = Present(f.BookAuthor) is { } author ? $"*{f.BookTitle}* by {author}" : $"*{f.BookTitle}*";
        letter.Append(CultureInfo.InvariantCulture, $"I am writing to ask permission to reproduce material from {work}{by} in {inBook}.\n\n");

        var material = new List<string>();
        if (f.QuotedWords > 0)
        {
            material.Add($"{f.QuotedWords} words of quoted prose");
        }

        if (f.LyricWords > 0)
        {
            material.Add($"{f.LyricWords} words of lyrics or poetry");
        }

        if (f.Figures > 0)
        {
            material.Add(f.Figures == 1 ? "1 figure" : $"{f.Figures} figures");
        }

        var where = new List<string>();
        if (f.Locators.Count > 0)
        {
            where.Add(JoinAnd(f.Locators));
        }

        if (f.Chapters.Count > 0)
        {
            where.Add((f.Chapters.Count == 1 ? "Chapter " : "Chapters ") + JoinAnd(f.Chapters.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToList()));
        }

        var used = material.Count == 0 ? "The material" : $"The material is {JoinAnd(material)}";
        letter.Append(used);
        if (where.Count > 0)
        {
            letter.Append(CultureInfo.InvariantCulture, $" ({string.Join("; ", where)})");
        }

        letter.Append(".\n\n");

        var plan = new List<string>();
        if (Present(f.Publisher) is { } publisher)
        {
            plan.Add($"published by {publisher}");
        }

        if (f.PlannedYear is { } year)
        {
            plan.Add($"in {year}");
        }

        if (f.Formats.Count > 0)
        {
            plan.Add($"in {JoinAnd(f.Formats.Select(FormatWord).ToList())}");
        }

        if (f.PrintRun is { } run)
        {
            plan.Add($"with a first print run of {run.ToString("N0", CultureInfo.InvariantCulture)} copies");
        }

        if (Present(f.Territory) is { } territory)
        {
            plan.Add($"for {territory} distribution");
        }

        if (plan.Count > 0)
        {
            letter.Append(CultureInfo.InvariantCulture, $"The book will be {string.Join(" ", plan)}.\n\n");
        }

        letter.Append("Please let me know the terms, any fee, and the credit line you would like printed with the citation.\n\n");
        letter.Append("Yours sincerely,\n");
        letter.Append(Present(f.BookAuthor) ?? string.Empty);
        return letter.ToString().TrimEnd();
    }

    public static string FormatWord(string format) => format switch
    {
        "pdf" => "PDF",
        _ => format,
    };

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string JoinAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
