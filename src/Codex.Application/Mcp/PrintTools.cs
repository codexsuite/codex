using System.ComponentModel;
using Codex.Application.Features.Print.Export;
using Codex.Application.Features.Print.Layout;
using Codex.Application.Features.Print.Proof;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Print;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// G. Publish, print side (#20 section G): preview_layout and export_for_printer, adapters over the Print feature's
// commands (ILayoutService, IProofService, IExportService); nothing here writes SQL. Both open the Layout screen.
// Scopes: preview_layout is on codex:read because a quick preview writes nothing, and kind "proof" additionally asks for
// codex:write (it queues a job row the worker compiles). export_for_printer is codex:read for preflight, which queues nothing, and mode "queue" additionally asks for codex:write. The print
// toolchain (pandoc, Tectonic) lives in the web and worker processes; a quick preview from a stdio process without pandoc
// fails not_configured, and neither tool waits for a compile: a job is read with get_job. Roles (#80) follow the same
// split: a quick preview or a preflight needs view on the book, a proof or a queued export needs write.
[McpServerToolType]
public sealed class PrintTools(
    McpRefs refs, McpQueries queries, McpAccess access, IBookAccess bookAccess, ILayoutService layout, IProofService proofs, IExportService exports)
{
    private const int FrontMatterPages = 4;
    private static readonly string[] Kinds = ["quick", "proof"];
    private static readonly string[] Modes = ["preflight", "queue"];

    public sealed record WarningLine(string Kind, string Detail, string? Scene, bool? Kept);

    public sealed record PreviewLayoutResult(
        string Kind, string Template, string TemplateName, string Trim, int EstimatedPages, IReadOnlyList<WarningLine> Warnings,
        Guid? JobId, string? State, bool? Cached, string? Message) : McpResult;

    public sealed record PreflightLine(string Severity, string Kind, string Detail, McpItem? Scene, McpItem? Source);

    public sealed record ExportForPrinterResult(
        string Mode, string? Verdict, bool? CanQueue, IReadOnlyList<PreflightLine> Blocks, IReadOnlyList<PreflightLine> Warnings,
        IReadOnlyList<PreflightLine> Notes, string Template, string Trim, int? EstimatedMegabytes, Guid? JobId, string? State) : McpWriteResult;

    [McpServerTool(Name = "preview_layout", Title = "How will this look in print?", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("How a scene, chapter or the whole book will look in print. kind \"quick\" (default) renders the Layout preview and returns an estimated " +
                 "page count and the warnings found (unknown directives, raw LaTeX the template skips, missing images); open_url opens it in Layout. " +
                 "kind \"proof\" queues a one-chapter PDF compiled by the worker (`scope` is a chapter, or a scene's chapter) and returns a job_id to read " +
                 "with get_job; an unchanged chapter answers with its earlier proof (`cached`). `template` tries another layout without changing the book. " +
                 "A proof needs the codex:write scope; a quick preview needs pandoc (not_configured without it).")]
    public async Task<PreviewLayoutResult> PreviewLayoutAsync(
        McpServer server,
        [Description("A scene, chapter or act: an id, \"5\", \"5.2\", \"act:2\" or \"ch:<slug>\". Omit for the whole book (quick only).")] string? scope = null,
        [Description("The book (default: the one the scope names, else the most recently edited).")] string? book = null,
        [Description("quick (default) or proof.")] string kind = "quick",
        [Description("A template slug to try instead of the book's, e.g. \"manuscript\".")] string? template = null,
        CancellationToken cancellationToken = default)
    {
        if (!Kinds.Contains(kind, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("kind", $"\"{kind}\" is not a kind of preview: use quick or proof.");
        }
        if (kind == "proof")
        {
            McpScopes.RequireScope(access.Current, "preview_layout", ApiTokenScopes.Write);
        }

        var owner = await refs.BookAsync(await BookRefAsync(book, scope, cancellationToken), cancellationToken);
        if (kind == "proof")
        {
            await bookAccess.RequireAsync(owner.Id, BookRole.Write, cancellationToken);
        }
        var outline = await refs.OutlineAsync(owner, McpRefs.SessionKey(server), cancellationToken);
        var item = scope == null ? null : McpRefs.Item(outline, scope);
        var state = await layout.GetAsync(owner.Id, cancellationToken);

        if (kind == "proof")
        {
            var chapter = item switch
            {
                null => throw McpToolException.Invalid("scope", "A proof is of one chapter: name it, or a scene in it."),
                { Kind: "act" } => throw McpToolException.Invalid("scope", $"{item.Label} is an act: a proof is of one chapter, so name a chapter or a scene in it."),
                { Kind: "scene" } => outline.SceneById(item.Id)!.Chapter,
                _ => outline.Chapters.First(c => c.Dto.Id == item.Id),
            };
            var proof = await proofs.RequestAsync(owner.Id, new ProofRequest(chapter.Dto.Id, template), cancellationToken);
            var proofTemplate = state.Templates.FirstOrDefault(t => t.Slug == proof.TemplateSlug);
            return new PreviewLayoutResult(
                kind, proof.TemplateSlug, proofTemplate?.Name ?? proof.TemplateSlug, proof.Trim,
                proof.PageCount ?? Pages(outline, scope: chapter.Dto.Id, proof.TemplateSlug, proofTemplate?.Columns ?? 1, proof.Trim), [],
                proof.Id, Joblike(proof.Status), proof.Cached, proof.Error ?? proof.Step)
            {
                Item = LayoutItem(owner, chapter.Item(owner.Id)),
            };
        }

        var preview = await layout.PreviewAsync(owner.Id, PreviewRequest(item, template), cancellationToken);
        var columns = state.Templates.FirstOrDefault(t => t.Slug == preview.Template)?.Columns ?? 1;
        var warnings = preview.Notices.Select(n => new WarningLine("notice", n, null, null))
            .Concat(preview.Flags.Select(f => new WarningLine(f.Kind, f.Detail, f.SceneId is { } id ? outline.SceneById(id)?.Dto.Number : null, f.Kept)))
            .ToList();
        return new PreviewLayoutResult(
            kind, preview.Template, preview.TemplateName, preview.Trim, Pages(outline, item?.Id, preview.Template, columns, preview.Trim), warnings,
            null, null, null, null)
        {
            Item = item == null ? new McpItem(owner.Id, "book", owner.Label, CodexLinks.Layout(owner.Id)) : LayoutItem(owner, item),
        };
    }

    [McpServerTool(Name = "export_for_printer", Title = "Build the print PDF", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Export for printer: the whole book as a print PDF. mode \"preflight\" lists what stands in the way and queues nothing: blocks " +
                 "(copyright risks from the print gate, missing images), warnings (images under the target dpi, unknown directives, raw LaTeX the " +
                 "template skips) and notes. mode \"queue\" runs preflight again and, when nothing blocks, queues the export and returns a job_id to read with " +
                 "get_job; any block, a copyright risk included, fails blocked and queues nothing, and a book with an export already queued or running fails " +
                 "conflict. Settings default to the book's layout: trim (e.g. 6x9), bleed (none, 3mm, 0.125in), crop_marks (needs a bleed), " +
                 "image_quality (print = 300 dpi, screen = 150 dpi). A queued export is built by the worker and can't be undone from here. " +
                 "Preflight needs codex:read; queue also needs codex:write.")]
    public async Task<ExportForPrinterResult> ExportForPrinterAsync(
        McpServer server,
        [Description("preflight or queue.")] string mode,
        [Description("The book (default: the most recently edited).")] string? book = null,
        [Description("A template slug to export with instead of the book's.")] string? template = null,
        [Description("Trim size slug, e.g. 6x9, 5.5x8.5, us-letter, a4.")] string? trim = null,
        [Description("none (default), 3mm or 0.125in.")] string? bleed = null,
        [Description("Crop marks (needs a bleed).")] bool crop_marks = false,
        [Description("print (300 dpi, default) or screen (150 dpi).")] string? image_quality = null,
        CancellationToken cancellationToken = default)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("mode", $"\"{mode}\" is not a mode: use preflight or queue.");
        }

        if (mode == "queue")
        {
            McpScopes.RequireScope(access.Current, "export_for_printer", ApiTokenScopes.Write);
        }

        var owner = await refs.BookAsync(book, cancellationToken);
        if (mode == "queue")
        {
            await bookAccess.RequireAsync(owner.Id, BookRole.Write, cancellationToken);
        }
        var outline = await refs.OutlineAsync(owner, McpRefs.SessionKey(server), cancellationToken);
        var request = new ExportRequest(template, trim, null, bleed ?? "none", crop_marks, image_quality ?? "print");
        var preflight = await exports.PreflightAsync(owner.Id, request, cancellationToken);
        var blocks = Lines(owner, outline, preflight.Blocks);
        var warnings = Lines(owner, outline, preflight.Warnings);
        var notes = Lines(owner, outline, preflight.Notes);

        if (mode == "preflight")
        {
            return new ExportForPrinterResult(
                mode, preflight.Verdict, preflight.Blocks.Count == 0, blocks, warnings, notes, preflight.Template, preflight.Trim,
                preflight.EstimatedMegabytes, null, null)
            {
                Item = new McpItem(owner.Id, "book", owner.Label, CodexLinks.Layout(owner.Id)),
            };
        }

        var export = await exports.RequestAsync(owner.Id, request, cancellationToken);
        var state = Joblike(export.Status);
        return new ExportForPrinterResult(
            mode, preflight.Verdict, true, blocks, warnings, notes, preflight.Template, preflight.Trim, preflight.EstimatedMegabytes, export.Id, state)
        {
            Item = new McpItem(export.Id, "export", $"Print PDF · {owner.Label}", CodexLinks.Layout(owner.Id)),
            SideEffects =
            [
                $"Queued a print PDF of {owner.Label} ({preflight.Template}, {preflight.Trim}, {preflight.Warnings.Count} warnings). Read its progress with get_job.",
            ],
            Affected = [new McpAffected("export", export.Id, "status", null, export.Status)],
        };
    }

    // A scope named by id says which book it is in when `book` was left out.
    private async Task<string?> BookRefAsync(string? book, string? scope, CancellationToken cancellationToken)
    {
        if (book != null || scope == null || !Guid.TryParse(scope.Trim(), out var id))
        {
            return book;
        }
        return (await queries.BookOfAsync(id, null, cancellationToken) ?? await queries.BookOfActOrChapterAsync(id, cancellationToken))?.ToString();
    }

    private static LayoutPreviewRequest PreviewRequest(McpItem? item, string? template) => item switch
    {
        null => new LayoutPreviewRequest("book", Template: template),
        { Kind: "scene" } => new LayoutPreviewRequest("scene", item.Id, template),
        { Kind: "chapter" } => new LayoutPreviewRequest("chapter", item.Id, template),
        _ => throw McpToolException.Invalid("scope", $"{item.Label} is an act: preview a chapter or a scene, or leave scope out for the whole book."),
    };

    private static McpItem LayoutItem(McpBook book, McpItem item) => item.Kind == "scene"
        ? item with { OpenUrl = CodexLinks.Layout(book.Id, scene: item.Number) }
        : item with { OpenUrl = CodexLinks.Layout(book.Id, chapter: item.Number) };

    // The Layout view's estimate (features/layout/pageEstimate.ts): words at the template's page, four front-matter pages
    // before the first chapter, each chapter starting on a new page.
    private static int Pages(BookOutline outline, Guid? scope, string template, int columns, string trim)
    {
        var perPage = WordsPerPage(template, columns, trim);
        int ForChapter(BookOutline.Chapter c) => (int)Math.Ceiling(outline.Scenes.Where(s => s.Chapter.Dto.Id == c.Dto.Id).Sum(s => s.Dto.Words) / perPage);
        if (scope is { } id && outline.SceneById(id) is { } scene)
        {
            return (int)Math.Ceiling(scene.Dto.Words / perPage);
        }
        if (scope is { } chapterId)
        {
            return outline.Chapters.Where(c => c.Dto.Id == chapterId).Sum(ForChapter);
        }
        return FrontMatterPages + outline.Chapters.Sum(ForChapter);
    }

    private static double WordsPerPage(string template, int columns, string trim)
    {
        if (template == "manuscript")
        {
            return 235;
        }
        if (template.StartsWith("novel", StringComparison.Ordinal))
        {
            return trim == "6x9" ? 125 : 104;
        }
        return (columns == 2 ? 185 : 165) * (trim == "a4" ? 1.04 : 1);
    }

    private static IReadOnlyList<PreflightLine> Lines(McpBook book, BookOutline outline, IReadOnlyList<PreflightItemDto> items) =>
        items.Select(i => new PreflightLine(
            i.Severity, i.Kind, i.Detail,
            i.SceneId is { } scene ? outline.SceneById(scene)?.Item(book.Id) : null,
            i.ReferenceId is { } source ? new McpItem(source, "source", i.Detail, CodexLinks.References(book.Id, source)) : null)).ToList();

    // The job states get_job reports (succeeded reads done, cancelled reads failed).
    private static string Joblike(string status) => status switch { "succeeded" => "done", "cancelled" => "failed", _ => status };
}
