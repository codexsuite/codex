using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.Application.Features.Print.Export;

// A ManuscriptExportRequest after validation; this is what export_job.settings holds for a docx, markdown or epub job.
public sealed record ManuscriptOptions(string Format, string Scope, Guid? ChapterId, bool IncludeBeats, bool IncludeSummaries)
{
    public static readonly string[] Formats = ["docx", "markdown", "epub"];

    public string Extension => Format switch { "docx" => "docx", "epub" => "epub", _ => "md" };

    public string Mime => Format switch
    {
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "epub" => "application/epub+zip",
        _ => "text/markdown",
    };

    public static ManuscriptOptions From(ManuscriptExportRequest request)
    {
        if (!Formats.Contains(request.Format))
        {
            throw ApiException.Validation($"The format must be {string.Join(", ", Formats)}, not '{request.Format}'.");
        }

        if (request.Scope is not ("book" or "chapter"))
        {
            throw ApiException.Validation("The scope must be book or chapter.");
        }

        if (request.Scope == "chapter" && request.ChapterId is null)
        {
            throw ApiException.Validation("Pick the chapter to export.");
        }

        return new ManuscriptOptions(request.Format, request.Scope, request.Scope == "chapter" ? request.ChapterId : null, request.IncludeBeats, request.IncludeSummaries);
    }
}
