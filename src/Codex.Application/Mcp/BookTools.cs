using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.Library;
using Codex.Application.Features.Print.Layout;
using Codex.Application.Features.Settings;
using Codex.Contracts;
using Codex.Contracts.Library;
using Codex.Contracts.Print;
using Codex.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// C. Shape the book: create_book and update_book (#20 section C), adapters over the Library lane's BookCommands and the
// Settings lane's commands (BookSettingsCommands, PublicationSettingsCommands, SeriesNumberCommands, SeriesMoveCommands).
// Each call records one change set whose steps reverse it; a series move cannot be reversed (the copy-down stays), so a
// call that moves a book is not undoable. Roles (#80): settings need write on the book; adding a book to a series, moving
// it between series or changing its place among the series' books changes the series, so they need the owner.
[McpServerToolType]
public sealed class BookTools(
    McpRefs refs, BookCommands books, BookSettingsCommands settings, PublicationSettingsCommands publicationSettings,
    SeriesNumberCommands numbers, SeriesMoveCommands moves, RulesImpactQueries rulesImpact, IServiceProvider services, McpUndo undo, McpChangeSets changeSets,
    IBookAccess access)
{
    // Resolved on use: the layout service needs the render settings, which create_book does not.
    private ILayoutService Layouts => services.GetRequiredService<ILayoutService>();

    private static readonly string[] Statuses = ["planning", "draft", "writing", "revising", "published"];

    public sealed record CreateBookResult(string Title, string Status, McpItem? Series, int? SeriesNumber) : McpWriteResult;

    // The fields update_book takes (#20 section C).
    public sealed record BookFields(
        string? Title = null, string? AuthorName = null, string? Status = null, int? TargetWordCount = null, string? StyleGuide = null,
        string? Subtitle = null, string? Description = null);

    public sealed record PublicationFields(
        string? CitationStyle = null, int? QuoteWordLimit = null, string? PublisherName = null, int? PlannedYear = null,
        IReadOnlyList<string>? Formats = null, int? PrintRun = null, string? Territory = null);

    // The layout update_book sets, as the Layout screen chooses it: the template by slug, its trim and the options to change
    // (the others keep their stored value). A member left out keeps what the book has now.
    public sealed record LayoutFields(string? Template = null, string? Trim = null, Dictionary<string, JsonElement>? Options = null);

    public sealed record UpdateBookResult(bool DryRun, IReadOnlyList<string> Changes) : McpWriteResult;

    [McpServerTool(Name = "create_book", Title = "Start a new book", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a book: standalone, or in a series (`series`) as the next number unless `series_number` says otherwise. It starts with one empty " +
                 "Act 1. A book in a series shares the series' Knowledge base. Returns the book and the Plan URL. Undo archives it. " +
                 "A starting outline is not supported yet: add acts, chapters and scenes with edit_outline.")]
    public async Task<CreateBookResult> CreateBookAsync(
        McpServer server,
        [Description("The book's title.")] string title,
        [Description("The author's name; default the author.")] string? author_name = null,
        [Description("Add the book to this series (id or title).")] string? series = null,
        [Description("Its number in the series; default the next free number.")] int? series_number = null,
        [Description("The word count the book is aiming for.")] int? target_word_count = null,
        [Description("A subtitle.")] string? subtitle = null,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        var seriesBook = series == null ? null : await refs.SeriesAsync(series, cancellationToken);
        if (seriesBook != null)
        {
            await access.RequireAsync(seriesBook.Id, BookRole.Owner, cancellationToken);
        }
        var created = await books.CreateAsync(new CreateBookRequest(title, subtitle, seriesBook?.Id, series_number, target_word_count, null), cancellationToken);
        if (!string.IsNullOrWhiteSpace(author_name))
        {
            await settings.PatchAsync(created.Id, new PatchBookSettingsRequest(null, author_name, null, null, null), cancellationToken);
        }

        var undoId = await changeSets.RecordAsync(created.Id, "create_book", $"Created the book {created.Title}", caller,
            [new McpUndoStep("archive_book", created.Id)], cancellationToken);
        var item = new McpItem(created.Id, "book", created.Title, CodexLinks.Plan(created.Id));
        List<string> effects = [$"{created.Title} is a new book with an empty Act 1."];
        if (seriesBook != null)
        {
            effects.Add($"It is book {created.SeriesNumber} of {seriesBook.Title}; it shares the series' Characters and Places.");
        }
        return new CreateBookResult(created.Title, created.Status, seriesBook?.Item, created.SeriesNumber)
        {
            Item = item, UndoId = undoId, SideEffects = effects,
            Affected = [new McpAffected("book", created.Id, "created", null, created.Title)],
        };
    }

    [McpServerTool(Name = "update_book", Title = "Change the book's settings", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Changes a book's settings: `fields` (title, subtitle, author_name, description, status, target_word_count, style_guide), `series_number` (its place among the " +
                 "series' books, the rest renumber), `series` or `make_standalone` (a move: the series' Knowledge base is copied onto the book, so it fails " +
                 "needs_confirmation, writing nothing, until you call again with confirm after a dry_run), and `publication` (citation_style chicago/apa/harvard, " +
                 "quote_word_limit, publisher_name, planned_year, print_run, formats, territory), and `layout` (template slug, trim, options: the Layout screen's choice). " +
                 "`dry_run` reports the changes without writing. Undo puts the fields, publication settings and layout back; a call that moves the book between series cannot be undone.")]
    public async Task<UpdateBookResult> UpdateBookAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        [Description("{ title, subtitle, author_name, description, status, target_word_count, style_guide }. An empty subtitle, author_name, description or style_guide clears it; target_word_count 0 clears the target.")] BookFields? fields = null,
        [Description("Publication settings: { citation_style, quote_word_limit, publisher_name, planned_year, print_run, formats, territory }.")] PublicationFields? publication = null,
        [Description("The book's layout: { template (a slug such as \"classic\"), trim, options }. Options are { key: value } and the others keep their stored value.")] LayoutFields? layout = null,
        [Description("The book's number among its series' books (1 is first).")] int? series_number = null,
        [Description("Move the book into this series (id or title).")] string? series = null,
        [Description("Take the book out of its series.")] bool make_standalone = false,
        [Description("Confirms a series move whose dry_run you have read.")] bool confirm = false,
        [Description("Report what would change and write nothing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        var target = await refs.BookAsync(book, cancellationToken);
        if (target.IsSeries)
        {
            throw McpToolException.Invalid("book", $"{target.Title} is a series; update_book changes books.");
        }
        var changesSeries = series_number != null || series != null || make_standalone;
        await access.RequireAsync(target.Id, changesSeries ? BookRole.Owner : BookRole.Write, cancellationToken);
        if (series != null && make_standalone)
        {
            throw McpToolException.Invalid("series", "Give `series` or `make_standalone`, not both.");
        }
        var hasFields = fields is { Title: not null } or { AuthorName: not null } or { Status: not null } or { TargetWordCount: not null } or { StyleGuide: not null }
            or { Subtitle: not null } or { Description: not null };
        var hasLayout = layout != null && layout != new LayoutFields();
        var hasPublication = publication != null && publication != new PublicationFields();
        if (!hasFields && !hasPublication && !hasLayout && series_number == null && series == null && !make_standalone)
        {
            throw McpToolException.Invalid("fields", "Nothing to change: give fields, publication, layout, series_number, series or make_standalone.");
        }
        if (fields?.Status != null && !Statuses.Contains(fields.Status))
        {
            throw McpToolException.Invalid("fields", $"status is one of {string.Join(", ", Statuses)}.");
        }
        if (fields?.Title != null && string.IsNullOrWhiteSpace(fields.Title))
        {
            throw McpToolException.Invalid("fields", "A book needs a title.");
        }
        if (series_number is < 1)
        {
            throw McpToolException.Invalid("series_number", "series_number is 1 or more.");
        }

        var before = await settings.GetAsync(target.Id, cancellationToken);
        var layoutBefore = hasLayout ? await Layouts.GetAsync(target.Id, cancellationToken) : null;
        var layoutPlan = hasLayout ? PlanLayout(layoutBefore!, layout!) : null;
        var rulesBefore = await publicationSettings.GetAsync(target.Id, cancellationToken);
        var changes = new List<string>();
        var affected = new List<McpAffected>();
        var steps = new List<McpUndoStep>();
        var item = new McpItem(target.Id, "book", target.Title, CodexLinks.Settings(target.Id));

        // A move is planned first: it may refuse before anything is written.
        Guid? moveTo = null;
        var isMove = false;
        if (series != null || make_standalone)
        {
            moveTo = series == null ? null : (await refs.SeriesAsync(series, cancellationToken)).Id;
            if (moveTo is { } into)
            {
                await access.RequireAsync(into, BookRole.Owner, cancellationToken);
            }
            isMove = moveTo != before.SeriesId;
            if (isMove)
            {
                var preview = await moves.PreviewAsync(target.Id, new SeriesMoveRequest(moveTo), cancellationToken);
                changes.AddRange(preview.Steps.Select(s => s.Text));
                changes.AddRange(preview.Warnings);
                affected.Add(new McpAffected("book", target.Id, "series", before.SeriesName, preview.To.Name));
                if (!dry_run && !confirm)
                {
                    throw new McpToolException(McpErrorCodes.NeedsConfirmation,
                        "Moving a book between series copies the series' Knowledge base onto it and cannot be undone. Nothing was written. " +
                        $"It would: {string.Join(" ", preview.Steps.Select(s => s.Text))} Call again with confirm: true to go ahead.");
                }
            }
        }

        var patch = new PatchBookSettingsRequest(fields?.Title, fields?.AuthorName, fields?.Status, fields?.TargetWordCount, fields?.StyleGuide, fields?.Subtitle, fields?.Description);
        void Note(string field, string? from, string? to)
        {
            changes.Add($"{field}: {from ?? "(none)"} -> {to ?? "(none)"}");
            affected.Add(new McpAffected("book", target.Id, field, from, to));
        }
        if (fields?.Title != null) Note("title", before.Title, fields.Title.Trim());
        if (fields?.AuthorName != null) Note("author_name", before.AuthorName, fields.AuthorName);
        if (fields?.Status != null) Note("status", before.Status, fields.Status);
        if (fields?.TargetWordCount != null) Note("target_word_count", before.TargetWords?.ToString(), fields.TargetWordCount.ToString());
        if (fields?.StyleGuide != null) Note("style_guide", before.StyleGuide, fields.StyleGuide);
        if (fields?.Subtitle != null) Note("subtitle", before.Subtitle, fields.Subtitle);
        if (fields?.Description != null) Note("description", before.Description, fields.Description);
        if (layoutPlan != null)
        {
            Note("layout_template", layoutPlan.Before.Template, layoutPlan.Request.Template);
            Note("layout_trim", layoutPlan.Before.Trim, layoutPlan.Request.Trim);
            foreach (var (key, value) in layoutPlan.Changed)
            {
                Note($"layout_option.{key}", layoutPlan.BeforeOptions.GetValueOrDefault(key).ToString(), value.ToString());
            }
        }
        if (series_number != null) Note("series_number", before.SeriesNumber?.ToString(), series_number.ToString());

        var request = hasPublication ? PublicationRequest(publication!) : null;
        if (publication?.CitationStyle != null) Note("citation_style", rulesBefore.Style.Value, publication.CitationStyle);
        if (publication?.QuoteWordLimit != null) Note("quote_word_limit", rulesBefore.QuoteWordLimit.Value.ToString(), publication.QuoteWordLimit.ToString());
        if (publication?.PublisherName != null) Note("publisher_name", rulesBefore.Publisher.Value, publication.PublisherName);
        if (publication?.PlannedYear != null) Note("planned_year", rulesBefore.PlannedYear.Value?.ToString(), publication.PlannedYear.ToString());
        if (publication?.PrintRun != null) Note("print_run", rulesBefore.PrintRun.Value?.ToString(), publication.PrintRun.ToString());
        if (publication?.Formats != null) Note("formats", string.Join(", ", rulesBefore.Formats.Value), string.Join(", ", publication.Formats));
        if (publication?.Territory != null) Note("territory", rulesBefore.Territory.Value, publication.Territory);
        if (publication?.CitationStyle != null)
        {
            changes.Add($"The citations and the bibliography use {publication.CitationStyle}.");
        }
        if (publication?.QuoteWordLimit is { } limit)
        {
            var impact = await rulesImpact.GetAsync(target.Id, limit, cancellationToken);
            changes.Add($"At {limit} quoted words, {impact.RiskCount} cited source(s) would be a Copyright risk.");
        }

        if (dry_run)
        {
            return new UpdateBookResult(true, changes) { Item = item, SideEffects = changes, Affected = affected };
        }

        try
        {
            if (isMove)
            {
                await moves.MoveAsync(target.Id, new SeriesMoveRequest(moveTo), cancellationToken);
                steps.Insert(0, new McpUndoStep("unsupported", target.Id, Title: "moving a book between series copies the series' Knowledge base onto it"));
            }
            if (layoutPlan != null)
            {
                await Layouts.SelectAsync(target.Id, layoutPlan.Request, cancellationToken);
                steps.Insert(0, new McpUndoStep("set_layout", target.Id, Title: layoutPlan.Before.Template, Summary: layoutPlan.Before.Trim,
                    Body: JsonSerializer.Serialize(layoutPlan.BeforeOptions),
                    Now: new McpUndoState(Title: layoutPlan.Request.Template, Summary: layoutPlan.Request.Trim)));
            }
            if (hasFields)
            {
                await settings.PatchAsync(target.Id, patch, cancellationToken);
                steps.Insert(0, new McpUndoStep("patch_book_settings", target.Id,
                    Title: fields?.Title == null ? null : before.Title, Summary: fields?.AuthorName == null ? null : before.AuthorName ?? "",
                    Status: fields?.Status == null ? null : before.Status, Version: fields?.TargetWordCount == null ? null : before.TargetWords ?? 0,
                    Body: fields?.StyleGuide == null ? null : before.StyleGuide ?? "",
                    Subtitle: fields?.Subtitle == null ? null : before.Subtitle ?? "", Description: fields?.Description == null ? null : before.Description ?? ""));
            }
            if (request != null)
            {
                await publicationSettings.PatchAsync(target.Id, request, cancellationToken);
                foreach (var step in PublicationUndo(target.Id, publication!, rulesBefore))
                {
                    steps.Insert(0, step);
                }
            }
            if (series_number is { } number)
            {
                var change = await numbers.SetAsync(target.Id, new SetSeriesNumberRequest(number), cancellationToken);
                steps.Insert(0, new McpUndoStep("set_series_number", target.Id, Version: change.PreviousNumber));
            }
        }
        catch
        {
            await undo.RunAsync(steps, CancellationToken.None);
            throw;
        }

        var undoId = await changeSets.RecordAsync(target.Id, "update_book", $"Changed the settings of {target.Title}", caller, steps, cancellationToken);
        var after = await settings.GetAsync(target.Id, cancellationToken);
        return new UpdateBookResult(false, changes)
        {
            Item = new McpItem(target.Id, "book", after.Title, CodexLinks.Settings(target.Id)), UndoId = undoId, SideEffects = changes, Affected = affected,
        };
    }

    private sealed record LayoutPlan(
        LayoutSelectionRequest Request, LayoutStateDto Before, IReadOnlyDictionary<string, JsonElement> BeforeOptions,
        IReadOnlyList<KeyValuePair<string, JsonElement>> Changed);

    // The selection to send, checked against the template's trims and options so a bad value fails invalid (and a
    // dry_run says so) before anything is written. The book's current values are kept for undo.
    private static LayoutPlan PlanLayout(LayoutStateDto current, LayoutFields layout)
    {
        var slug = layout.Template ?? current.Template;
        var template = current.Templates.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase))
                       ?? throw McpToolException.Invalid("layout", $"No layout template \"{slug}\" (it has {string.Join(", ", current.Templates.Select(t => t.Slug))}).");
        var trim = layout.Trim ?? (template.Slug == current.Template ? current.Trim : template.Trims[0]);
        if (!template.Trims.Contains(trim))
        {
            throw McpToolException.Invalid("layout", $"{template.Name} has no trim \"{trim}\" (it has {string.Join(", ", template.Trims)}).");
        }
        var changed = new List<KeyValuePair<string, JsonElement>>();
        foreach (var (key, value) in layout.Options ?? [])
        {
            var option = template.Options.FirstOrDefault(o => o.Key == key)
                         ?? throw McpToolException.Invalid("layout", $"{template.Name} has no option \"{key}\" (it has {string.Join(", ", template.Options.Select(o => o.Key))}).");
            var valid = option.Type == "boolean"
                ? value.ValueKind is JsonValueKind.True or JsonValueKind.False
                : value.ValueKind == JsonValueKind.String && option.Choices.Contains(value.GetString()!);
            if (!valid)
            {
                throw McpToolException.Invalid("layout", option.Type == "boolean"
                    ? $"The option {key} is true or false."
                    : $"The option {key} is one of {string.Join(", ", option.Choices)}.");
            }
            changed.Add(new KeyValuePair<string, JsonElement>(key, value));
        }
        var currentTemplate = current.Templates.First(t => t.Slug == current.Template);
        var beforeOptions = currentTemplate.Options.ToDictionary(o => o.Key, o => o.Value);
        return new LayoutPlan(new LayoutSelectionRequest(template.Slug, trim, layout.Options), new LayoutStateDto(current.Template, current.Trim, current.Templates), beforeOptions, changed);
    }

    private static PatchPublicationSettingsRequest PublicationRequest(PublicationFields p)
    {
        if (p.CitationStyle is not (null or "chicago" or "apa" or "harvard"))
        {
            throw McpToolException.Invalid("publication", "citation_style is chicago, apa or harvard.");
        }
        return new PatchPublicationSettingsRequest(p.CitationStyle, p.QuoteWordLimit, p.PublisherName, p.PlannedYear, p.Formats, p.PrintRun, p.Territory);
    }

    // Puts each changed field back: a value of the book's own is set again, an inherited or empty one goes back to inheriting.
    private static IEnumerable<McpUndoStep> PublicationUndo(Guid bookId, PublicationFields p, PublicationSettingsDto before)
    {
        McpUndoStep Step<T>(string field, PublicationValueDto<T> was, bool empty) => !was.Inherited && !empty
            ? new McpUndoStep("set_publication", bookId, Title: field, Body: JsonSerializer.Serialize(was.Value))
            : new McpUndoStep("reset_publication", bookId, Title: field);

        if (p.CitationStyle != null) yield return Step("style", before.Style, false);
        if (p.QuoteWordLimit != null) yield return Step("quoteWordLimit", before.QuoteWordLimit, false);
        if (p.PublisherName != null) yield return Step("publisher", before.Publisher, before.Publisher.Value == null);
        if (p.PlannedYear != null) yield return Step("plannedYear", before.PlannedYear, before.PlannedYear.Value == null);
        if (p.Formats != null) yield return Step("formats", before.Formats, false);
        if (p.PrintRun != null) yield return Step("printRun", before.PrintRun, before.PrintRun.Value == null);
        if (p.Territory != null) yield return Step("territory", before.Territory, before.Territory.Value == null);
    }
}
