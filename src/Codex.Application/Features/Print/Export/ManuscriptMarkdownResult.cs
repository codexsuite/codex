namespace Codex.Application.Features.Print.Export;

// The manuscript as Markdown (YAML title block first), the file name stem for it and what it left out.
public sealed record ManuscriptMarkdownResult(string Title, string? Author, string Slug, string Markdown, IReadOnlyList<string> Notes);
