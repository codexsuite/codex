using System.ComponentModel;
using System.Globalization;
using Codex.Application.Features.Structure;
using Codex.Contracts;
using Codex.Contracts.Structure;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// C. Shape the book: edit_outline (#20, M4, M11). A batch of ops, every ref resolved against the outline as it was
// before the batch (M4), checked and placed on a model of the outline first (that is the dry run), then applied
// through the Plan screen's commands (StructureCreateCommands, ActCommands, PositionCommands, SceneCommands,
// SceneArchiveCommands, ChapterArchiveCommands, LabelCommands). The commands each commit on their own, so if one fails
// the ops already applied are reversed with the same steps undo uses, and the call fails with the first error: the
// batch lands together or not at all.
[McpServerToolType]
public sealed class OutlineTools(
    McpRefs refs, McpQueries queries, StructureCreateCommands creates, ActCommands acts, PositionCommands positions, SceneCommands sceneCommands,
    SceneArchiveCommands archives, ChapterArchiveCommands chapterArchives, ChapterCommands chapters, LabelCommands labels, McpChangeSets changeSets, McpUndo undo, McpMentions mentions,
    IBookAccess access, ILogger<OutlineTools> logger)
{
    public sealed record OutlineOp(
        string Op, string? Item = null, string? Kind = null, string? Title = null, string? Summary = null, string? Status = null,
        IReadOnlyList<string>? Labels = null, IReadOnlyList<string>? AddLabels = null, IReadOnlyList<string>? RemoveLabels = null,
        bool CreateMissingLabels = false, string? Before = null, string? After = null, string? Into = null,
        string? PlanNotes = null, bool? IsNumbered = null, string? ExpectedSummary = null, string? ExpectedPlanNotes = null);

    public sealed record OpResult(int Index, string Op, McpItem? Item, string Summary);

    public sealed record Renumbered(Guid Id, string Item, string From, string To);

    public sealed record EditOutlineResult(bool DryRun, IReadOnlyList<OpResult> Results, IReadOnlyList<Renumbered> Renumbered) : McpWriteResult;

    // A planned op: what it does to the model of the outline and how to run it for real.
    private sealed record Planned(int Index, OutlineOp Op, McpItem? Item, string Summary, Func<List<McpUndoStep>, CancellationToken, Task> Run);

    [McpServerTool(Name = "edit_outline", Title = "Add, rename, move or cut parts of the outline", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("A batch of outline ops, applied in order, all or nothing. Every ref is read against the outline as it was before the " +
                 "batch, so \"6\" means the same chapter in every op. Ops: " +
                 "{op:\"move\", item, before|after|into} for acts, chapters and scenes; " +
                 "{op:\"archive\"|\"restore\", item} for acts, chapters and scenes (restore takes the archived item's id; archiving a " +
                 "chapter or act takes its scenes out with it, and restore brings back the ones that were live); " +
                 "{op:\"update\", item, title?, summary?, plan_notes?, status?, add_labels?, remove_labels?, create_missing_labels?} for a scene; " +
                 "{op:\"update\", item, title?, summary?, plan_notes?, is_numbered?} for a chapter (is_numbered:false takes it out of the numbering, " +
                 "so the chapters after it renumber; its ch:<slug> never changes); {op:\"update\", item, title?} for an act; " +
                 "a scene or chapter update may also give expected_summary? / expected_plan_notes? (the text you read): if the stored text differs the batch fails conflict; " +
                 "{op:\"add\", kind:\"act\"|\"chapter\"|\"scene\", title?, before|after|into} (a scene also takes summary? and labels?; " +
                 "a new act starts with one chapter holding one empty scene). Refs can't name an item the same batch adds: read the " +
                 "outline again for its id. Returns a result per op and the chapters renumbered; dry_run reports the same without writing.")]
    public async Task<EditOutlineResult> EditOutlineAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        [Description("The ops, applied in order.")] IReadOnlyList<OutlineOp> ops,
        [Description("Report the result without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (ops is not { Count: > 0 })
        {
            throw McpToolException.Invalid("ops", "ops needs at least one op.");
        }
        var target = await refs.BookAsync(book, cancellationToken);
        await access.RequireAsync(target.Id, BookRole.Write, cancellationToken);
        var outline = await refs.OutlineAsync(target, McpRefs.SessionKey(server), cancellationToken);
        var model = OutlineModel.From(outline.Dto);
        var bookLabels = await labels.ListAsync(target.Id, cancellationToken);
        var planned = new List<Planned>();
        for (var i = 0; i < ops.Count; i++)
        {
            planned.Add(await PlanAsync(i, ops[i], outline, model, bookLabels, cancellationToken));
        }
        var renumbered = Renumber(outline, model);
        var results = planned.Select(p => new OpResult(p.Index, p.Op.Op, p.Item, p.Summary)).ToList();
        var sideEffects = renumbered.Select(r => $"{r.Item} is now {r.To} (was {r.From}); in-text links to it follow, since they store the chapter id.").ToList();
        var affected = renumbered.Select(r => new McpAffected("chapter", r.Id, "number", r.From, r.To, r.Item)).ToList();
        if (dry_run)
        {
            return new EditOutlineResult(true, results, renumbered) { Item = target.Item, SideEffects = sideEffects, Affected = affected };
        }

        var steps = new List<McpUndoStep>();
        try
        {
            foreach (var op in planned)
            {
                await op.Run(steps, cancellationToken);
            }
        }
        catch (Exception original)
        {
            await RollBackAsync(steps, original);
            throw;
        }
        await refs.OutlineAsync(target, McpRefs.SessionKey(server), cancellationToken);
        var undoId = await changeSets.RecordAsync(target.Id, "edit_outline",
            string.Join("; ", planned.Select(p => p.Summary)), McpCaller.From(server), steps, cancellationToken);
        return new EditOutlineResult(false, results, renumbered) { Item = target.Item, SideEffects = sideEffects, Affected = affected, UndoId = undoId };
    }

    // Every step the batch has pushed so far, newest first, run back; if that fails too, the original error is the one the
    // caller gets and both are logged.
    private async Task RollBackAsync(List<McpUndoStep> steps, Exception original)
    {
        if (steps.Count == 0)
        {
            return;
        }
        try
        {
            await undo.RunAsync(steps, CancellationToken.None);
        }
        catch (Exception rollback)
        {
            logger.LogError(original, "edit_outline failed part way");
            logger.LogError(rollback, "edit_outline could not roll back its {Steps} steps", steps.Count);
        }
    }

    // Where the batch leaves an item, once every op has been planned (the model is final by the time ops run).
    private static McpUndoState? Placed(OutlineModel model, Guid id) =>
        model.TryLocate(id) is { } place ? new McpUndoState(Place: true, Parent: place.Parent, After: place.After) : null;

    // A command just returned: its steps go on the front, so the list always undoes the newest change first.
    private static void Push(List<McpUndoStep> steps, params McpUndoStep[] added) => steps.InsertRange(0, added);

    private async Task<Planned> PlanAsync(
        int index, OutlineOp op, BookOutline outline, OutlineModel model, IReadOnlyList<LabelDto> bookLabels, CancellationToken cancellationToken)
    {
        var field = $"ops[{index.ToString(CultureInfo.InvariantCulture)}]";
        switch (op.Op)
        {
            case "move":
                {
                    var item = Resolve(outline, op.Item, $"{field}.item");
                    var (parent, after) = Place(outline, model, item.Kind, item.Id, op, field);
                    var (oldParent, oldAfter) = model.Locate(item.Id);
                    model.Move(item.Id, parent, after);
                    return new Planned(index, op, item, $"Moved {item.Label}", async (steps, ct) =>
                    {
                        switch (item.Kind)
                        {
                            case "act":
                                await positions.MoveActAsync(item.Id, new MoveActRequest(after), ct);
                                Push(steps, new McpUndoStep("move_act", item.Id, After: oldAfter, Now: Placed(model, item.Id)));
                                break;
                            case "chapter":
                                await positions.MoveChapterAsync(item.Id, new MoveChapterRequest(parent, after), ct);
                                Push(steps, new McpUndoStep("move_chapter", item.Id, oldParent, oldAfter, Now: Placed(model, item.Id)));
                                break;
                            default:
                                await positions.MoveSceneAsync(item.Id, new MoveSceneRequest(parent, after), ct);
                                Push(steps, new McpUndoStep("move_scene", item.Id, oldParent, oldAfter, Now: Placed(model, item.Id)));
                                break;
                        }
                    });
                }
            case "archive":
                {
                    var item = Resolve(outline, op.Item, $"{field}.item");
                    var (oldParent, oldAfter) = model.Locate(item.Id);
                    if (item.Kind == "act" && model.Children(oldParent).Count == 1)
                    {
                        throw McpToolException.Invalid($"{field}.item", "A book needs at least one act.");
                    }
                    model.Remove(item.Id);
                    return new Planned(index, op, item, $"Archived {item.Label}", async (steps, ct) =>
                    {
                        switch (item.Kind)
                        {
                            case "act":
                                await acts.ArchiveAsync(item.Id, ct);
                                Push(steps, new McpUndoStep("restore_act", item.Id), new McpUndoStep("move_act", item.Id, After: oldAfter));
                                break;
                            case "chapter":
                                await chapterArchives.ArchiveAsync(item.Id, ct);
                                Push(steps, new McpUndoStep("restore_chapter", item.Id), new McpUndoStep("move_chapter", item.Id, oldParent, oldAfter));
                                break;
                            default:
                                await archives.ArchiveAsync(item.Id, ct);
                                Push(steps, new McpUndoStep("restore_scene", item.Id), new McpUndoStep("move_scene", item.Id, oldParent, oldAfter));
                                break;
                        }
                    });
                }
            case "restore":
                return await PlanRestoreAsync(index, op, outline, model, field, cancellationToken);
            case "update":
                return await PlanUpdateAsync(index, op, outline, model, bookLabels, field, cancellationToken);
            case "add":
                return await PlanAddAsync(index, op, outline, model, bookLabels, field, cancellationToken);
            default:
                throw McpToolException.Invalid($"{field}.op", "op is add, update, move, archive or restore.");
        }
    }

    // restore takes an archived scene's, chapter's or act's id; the command puts it back at its old place.
    private async Task<Planned> PlanRestoreAsync(
        int index, OutlineOp op, BookOutline outline, OutlineModel model, string field, CancellationToken cancellationToken)
    {
        var id = Guid.TryParse(op.Item?.Trim(), out var parsed)
            ? parsed
            : throw McpToolException.Invalid($"{field}.item", "restore takes the archived scene's, chapter's or act's id.");
        var bookId = outline.Book.Id;
        if (await queries.ArchivedSceneChapterAsync(bookId, id, cancellationToken) is { } chapterId)
        {
            var chapter = outline.Chapters.FirstOrDefault(c => c.Dto.Id == chapterId)
                          ?? throw McpToolException.Invalid($"{field}.item", "That scene's chapter is archived; restore the chapter first.");
            model.RequireLive(chapterId);
            model.Insert(id, chapterId, model.Last(chapterId), numbered: false);
            var item = new McpItem(id, "scene", $"{chapter.Label} · archived scene", CodexLinks.Plan(bookId, chapter: chapter.Number));
            return new Planned(index, op, item, $"Restored a scene to {chapter.Label}", async (steps, ct) =>
            {
                await archives.RestoreAsync(id, ct);
                Push(steps, new McpUndoStep("archive_scene", id));
            });
        }
        if (await queries.ArchivedChapterAsync(bookId, id, cancellationToken) is { } archivedChapter)
        {
            var chapter = outline.ArchivedChapters.FirstOrDefault(c => c.Dto.Id == id);
            if (archivedChapter.ActArchived || chapter == null)
            {
                throw McpToolException.Invalid($"{field}.item", "That chapter's act is archived; restore the act first.");
            }
            model.RequireLive(archivedChapter.ActId);
            model.Insert(id, archivedChapter.ActId, AfterByKey(model, outline, archivedChapter.ActId, archivedChapter.Position), archivedChapter.IsNumbered);
            var item = chapter.Item(bookId);
            return new Planned(index, op, item, $"Restored chapter “{chapter.Dto.Title}” to {chapter.Act.Label}", async (steps, ct) =>
            {
                await chapterArchives.RestoreAsync(id, ct);
                Push(steps, new McpUndoStep("archive_chapter", id));
            });
        }
        if (await queries.ArchivedActAsync(bookId, id, cancellationToken) is { } act)
        {
            model.Insert(id, bookId, AfterByKey(model, outline, bookId, act.Position), numbered: false);
            foreach (var chapter in act.Chapters)
            {
                model.Insert(chapter.Id, id, model.Last(id), chapter.IsNumbered);
            }
            var number = model.Children(bookId).ToList().IndexOf(id) + 1;
            var label = McpLabels.Act(number, act.Title);
            var item = new McpItem(id, "act", label, CodexLinks.Plan(bookId), $"act:{number.ToString(CultureInfo.InvariantCulture)}");
            return new Planned(index, op, item, $"Restored {label}", async (steps, ct) =>
            {
                await acts.RestoreAsync(id, ct);
                Push(steps, new McpUndoStep("archive_act", id));
            });
        }
        throw McpToolException.NotFound($"{outline.Book.Title} has no archived scene, chapter or act {id}.");
    }

    // Where a restored act or chapter lands among its parent's children: after the last one whose key, as the batch
    // read it, sorts at or before its own (the command puts it back at its old key, or right after a live one that took
    // it). Exact unless an earlier op of the batch moved or added its neighbours.
    private static Guid? AfterByKey(OutlineModel model, BookOutline outline, Guid parent, string position)
    {
        var keys = outline.Dto.Acts.Select(a => (a.Id, a.Position))
            .Concat(outline.Dto.Acts.SelectMany(a => a.Chapters).Select(c => (c.Id, c.Position)))
            .ToDictionary(p => p.Id, p => p.Position);
        return model.Children(parent)
            .Where(id => keys.TryGetValue(id, out var key) && string.CompareOrdinal(key, position) <= 0)
            .Select(id => (Guid?)id).LastOrDefault();
    }

    private async Task<Planned> PlanUpdateAsync(
        int index, OutlineOp op, BookOutline outline, OutlineModel model, IReadOnlyList<LabelDto> bookLabels, string field, CancellationToken cancellationToken)
    {
        var item = Resolve(outline, op.Item, $"{field}.item");
        model.RequireLive(item.Id);
        if (item.Kind != "scene")
        {
            return await PlanContainerUpdateAsync(index, op, item, outline, model, field, cancellationToken);
        }
        if (op.IsNumbered != null)
        {
            throw McpToolException.Invalid($"{field}.is_numbered", $"is_numbered is for a chapter; {item.Label} is a scene.");
        }
        var scene = outline.SceneById(item.Id)!.Dto;
        var current = scene.Labels.Select(l => l.Id).ToList();
        var add = LabelIds(op.AddLabels, bookLabels, op.CreateMissingLabels, $"{field}.add_labels");
        var remove = LabelIds(op.RemoveLabels, bookLabels, false, $"{field}.remove_labels");
        var labelsChange = (op.AddLabels?.Count ?? 0) + (op.RemoveLabels?.Count ?? 0) > 0;
        var patch = op.Title != null || op.Summary != null || op.Status != null || op.PlanNotes != null;
        var planNotes = await mentions.ResolveAsync(op.PlanNotes, outline, cancellationToken);
        var storedNotes = op.PlanNotes != null || op.ExpectedPlanNotes != null
            ? (await queries.ScenePlanNotesAsync(outline.Book.Id, cancellationToken)).GetValueOrDefault(item.Id, "")
            : null;
        var oldNotes = op.PlanNotes == null ? null : storedNotes;
        RequireExpected(item, field, op, scene.Summary, storedNotes);
        var summary = await mentions.ResolveAsync(op.Summary, outline, cancellationToken);
        return new Planned(index, op, item, $"Updated {item.Label}", async (steps, ct) =>
        {
            if (patch)
            {
                await sceneCommands.PatchAsync(item.Id, new PatchSceneRequest(summary, op.Title, op.Status, planNotes), ct);
                Push(steps, new McpUndoStep("patch_scene", item.Id, Title: scene.Title ?? "", Summary: scene.Summary ?? "", Status: scene.Status,
                    Body: oldNotes, Now: new McpUndoState(Title: op.Title, Summary: summary, Status: op.Status, Body: planNotes)));
            }
            if (labelsChange)
            {
                var created = await CreateLabelsAsync(outline.Book.Id, add.Missing, steps, ct);
                var next = current.Union(add.Ids).Union(created).Except(remove.Ids).ToList();
                await sceneCommands.SetLabelsAsync(item.Id, new SetSceneLabelsRequest(next), ct);
                Push(steps, new McpUndoStep("set_labels", item.Id, Labels: current, Now: new McpUndoState(Labels: next)));
            }
        });
    }

    // expected_summary / expected_plan_notes: the text the agent last read. If what is stored differs (null and empty are
    // the same), the whole batch fails `conflict` before anything is written.
    private static void RequireExpected(McpItem item, string field, OutlineOp op, string? summary, string? notes)
    {
        foreach (var (name, expected, stored) in new[] { ("summary", op.ExpectedSummary, summary), ("plan_notes", op.ExpectedPlanNotes, notes) })
        {
            if (expected != null && (expected.Length == 0 ? "" : expected) != (stored ?? ""))
            {
                throw McpToolException.Conflict(
                    $"{field}: {item.Label}'s {name} changed since you read it; read it again with get_outline and re-apply. Nothing was written.");
            }
        }
    }

    // update on a chapter (title, summary, plan_notes, is_numbered) or an act (title). A field that does not apply to the
    // kind is invalid, naming it; the undo step puts back only the fields this op changed.
    private async Task<Planned> PlanContainerUpdateAsync(
        int index, OutlineOp op, McpItem item, BookOutline outline, OutlineModel model, string field, CancellationToken cancellationToken)
    {
        var isChapter = item.Kind == "chapter";
        var notForKind = new (string Name, bool Given)[]
        {
            ("status", op.Status != null),
            ("add_labels", op.AddLabels is { Count: > 0 }),
            ("remove_labels", op.RemoveLabels is { Count: > 0 }),
            ("create_missing_labels", op.CreateMissingLabels),
            ("summary", !isChapter && op.Summary != null),
            ("plan_notes", !isChapter && op.PlanNotes != null),
            ("is_numbered", !isChapter && op.IsNumbered != null),
        };
        if (notForKind.FirstOrDefault(f => f.Given) is { Given: true } bad)
        {
            throw McpToolException.Invalid($"{field}.{bad.Name}",
                $"{bad.Name} does not apply to {item.Label}; {(isChapter ? "a chapter takes title, summary, plan_notes and is_numbered" : "an act takes a title only")}.");
        }
        if (op.Title == null && op.Summary == null && op.PlanNotes == null && op.IsNumbered == null)
        {
            throw McpToolException.Invalid(field, $"Nothing to update on {item.Label}: give {(isChapter ? "title, summary, plan_notes or is_numbered" : "a title")}.");
        }
        if (op.Title is { } title && (title.Trim().Length > 200 || (isChapter && title.Trim().Length == 0)))
        {
            throw McpToolException.Invalid($"{field}.title", isChapter ? "A chapter title is 1 to 200 characters." : "An act title is at most 200 characters.");
        }

        if (!isChapter)
        {
            var act = outline.Acts.First(a => a.Dto.Id == item.Id).Dto;
            var newTitle = op.Title!.Trim();
            return new Planned(index, op, item, $"Renamed {item.Label}", async (steps, ct) =>
            {
                await acts.RenameAsync(item.Id, new PatchActRequest(newTitle), ct);
                Push(steps, new McpUndoStep("patch_act", item.Id, Title: act.Title, Now: new McpUndoState(Title: newTitle)));
            });
        }

        var current = await chapters.GetFieldsAsync(item.Id, cancellationToken);
        RequireExpected(item, field, op, current.Summary, current.Notes);
        var summary = await mentions.ResolveAsync(op.Summary, outline, cancellationToken);
        var notes = await mentions.ResolveAsync(op.PlanNotes, outline, cancellationToken);
        if (op.IsNumbered is { } numbered)
        {
            model.SetNumbered(item.Id, numbered);
        }
        var request = new PatchChapterRequest(op.Title, summary, notes, op.IsNumbered);
        return new Planned(index, op, item, $"Updated {item.Label}", async (steps, ct) =>
        {
            await chapters.PatchAsync(item.Id, request, ct);
            Push(steps, new McpUndoStep("patch_chapter", item.Id,
                Title: op.Title == null ? null : current.Title, Summary: summary == null ? null : current.Summary,
                Body: notes == null ? null : current.Notes, Flag: op.IsNumbered == null ? null : current.IsNumbered,
                Now: new McpUndoState(Title: op.Title?.Trim(), Summary: summary, Body: notes, Flag: op.IsNumbered)));
        });
    }

    private async Task<Planned> PlanAddAsync(
        int index, OutlineOp op, BookOutline outline, OutlineModel model, IReadOnlyList<LabelDto> bookLabels, string field, CancellationToken cancellationToken)
    {
        var kind = op.Kind switch
        {
            "act" or "chapter" or "scene" => op.Kind,
            _ => throw McpToolException.Invalid($"{field}.kind", "add takes kind \"act\", \"chapter\" or \"scene\"."),
        };
        if (kind != "scene" && (op.Summary != null || op.Labels is { Count: > 0 }))
        {
            throw McpToolException.Invalid(field, $"An added {kind} takes a title only; summary and labels are for scenes.");
        }
        var placeholder = Guid.CreateVersion7();
        var (parent, after) = Place(outline, model, kind, placeholder, op, field);
        // The create commands append when there is no sibling to follow, so an item that goes first is moved there after.
        var first = after == null && model.Children(parent).Count > 0;
        model.Insert(placeholder, parent, after, numbered: kind == "chapter");
        if (kind == "act")
        {
            // ActCommands.CreateAsync gives a new act one numbered chapter, which renumbers the chapters after it.
            model.Insert(Guid.CreateVersion7(), placeholder, null, numbered: true);
        }
        var labelIds = LabelIds(op.Labels, bookLabels, op.CreateMissingLabels, $"{field}.labels");
        var addSummary = await mentions.ResolveAsync(op.Summary, outline, cancellationToken);
        var summary = $"Added a {kind}{(op.Title is { Length: > 0 } t ? $" “{t}”" : "")}";
        return new Planned(index, op, null, summary, async (steps, ct) =>
        {
            switch (kind)
            {
                case "act":
                    {
                        var act = await acts.CreateAsync(parent, new CreateActRequest(op.Title, after), ct);
                        Push(steps, new McpUndoStep("archive_act", act.Id));
                        if (first)
                        {
                            await positions.MoveActAsync(act.Id, new MoveActRequest(null), ct);
                        }
                        return;
                    }
                case "chapter":
                    {
                        var chapter = await creates.CreateChapterAsync(parent, new CreateChapterRequest(op.Title, after), ct);
                        Push(steps, new McpUndoStep("archive_chapter", chapter.Id));
                        if (first)
                        {
                            await positions.MoveChapterAsync(chapter.Id, new MoveChapterRequest(parent, null), ct);
                        }
                        return;
                    }
            }
            var scene = await creates.CreateSceneAsync(parent, new CreateSceneRequest(op.Title, after), ct);
            Push(steps, new McpUndoStep("archive_scene", scene.Id));
            if (first)
            {
                await positions.MoveSceneAsync(scene.Id, new MoveSceneRequest(parent, null), ct);
            }
            if (addSummary != null)
            {
                await sceneCommands.PatchAsync(scene.Id, new PatchSceneRequest(addSummary, null, null), ct);
            }
            if (labelIds.Ids.Count > 0 || labelIds.Missing.Count > 0)
            {
                var created = await CreateLabelsAsync(outline.Book.Id, labelIds.Missing, steps, ct);
                await sceneCommands.SetLabelsAsync(scene.Id, new SetSceneLabelsRequest(labelIds.Ids.Union(created).ToList()), ct);
            }
        });
    }

    // Labels the batch makes for create_missing_labels, each with the step that deletes it again.
    private async Task<List<Guid>> CreateLabelsAsync(Guid bookId, IReadOnlyList<string> names, List<McpUndoStep> steps, CancellationToken ct)
    {
        var created = new List<Guid>();
        foreach (var name in names)
        {
            var label = await labels.CreateAsync(bookId, new CreateLabelRequest(name, null), ct);
            Push(steps, new McpUndoStep("delete_label", label.Id));
            created.Add(label.Id);
        }
        return created;
    }

    // Where before / after / into put an item of this kind: its new parent and the sibling it follows (null = first).
    private static (Guid Parent, Guid? After) Place(BookOutline outline, OutlineModel model, string kind, Guid moving, OutlineOp op, string field)
    {
        var given = new[] { op.Before, op.After, op.Into }.Count(r => r != null);
        if (given != 1)
        {
            throw McpToolException.Invalid(field, "Give exactly one of before, after or into.");
        }
        if (op.Into != null)
        {
            var into = Resolve(outline, op.Into, $"{field}.into");
            var parentKind = kind switch { "scene" => "chapter", "chapter" => "act", _ => null };
            if (into.Kind != parentKind)
            {
                throw McpToolException.Invalid($"{field}.into", parentKind == null
                    ? "An act has no parent: use before or after."
                    : $"A {kind} goes into a {parentKind}; {into.Label} is a {into.Kind}.");
            }
            return (into.Id, model.Last(into.Id, moving));
        }
        var anchorField = op.Before != null ? $"{field}.before" : $"{field}.after";
        var anchor = Resolve(outline, op.Before ?? op.After, anchorField);
        if (anchor.Kind != kind)
        {
            throw McpToolException.Invalid(anchorField, $"A {kind} goes before or after another {kind}; {anchor.Label} is a {anchor.Kind}.");
        }
        if (anchor.Id == moving)
        {
            throw McpToolException.Invalid(anchorField, "An item cannot go before or after itself.");
        }
        var (parent, _) = model.Locate(anchor.Id);
        if (op.After != null)
        {
            return (parent, anchor.Id);
        }
        var siblings = model.Children(parent).Where(id => id != moving).ToList();
        var at = siblings.IndexOf(anchor.Id);
        return (parent, at > 0 ? siblings[at - 1] : null);
    }

    private static McpItem Resolve(BookOutline outline, string? reference, string field) =>
        string.IsNullOrWhiteSpace(reference)
            ? throw McpToolException.Invalid(field, $"{field} needs an act, chapter or scene ref.")
            : McpRefs.Item(outline, reference);

    private static (List<Guid> Ids, List<string> Missing) LabelIds(IReadOnlyList<string>? names, IReadOnlyList<LabelDto> bookLabels, bool createMissing, string field)
    {
        var ids = new List<Guid>();
        var missing = new List<string>();
        foreach (var name in names ?? [])
        {
            var found = bookLabels.Where(l => string.Equals(l.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 1)
            {
                ids.Add(found[0].Id);
            }
            else if (found.Count > 1)
            {
                throw McpToolException.Invalid(field, $"\"{name}\" names {found.Count} labels in this book's scope.");
            }
            else if (createMissing)
            {
                missing.Add(name.Trim());
            }
            else
            {
                throw McpToolException.Invalid(field, $"No label \"{name}\" in this book; pass create_missing_labels: true to add it.");
            }
        }
        return (ids, missing);
    }

    // The live chapters whose number the batch changes, as "Ch 7" → "Ch 6" (#20's renumbered); "unnumbered" when the batch takes a chapter out of the numbering or puts it in.
    private static List<Renumbered> Renumber(BookOutline outline, OutlineModel model)
    {
        var after = model.ChapterNumbers();
        int? NumberAfter(Guid id) => after.TryGetValue(id, out var n) ? n : default(int?);
        string Name(int? number) => number is { } n ? $"Ch {n}" : "unnumbered";
        return outline.Chapters
            .Where(c => model.TryLocate(c.Dto.Id) != null && NumberAfter(c.Dto.Id) != c.Dto.Number)
            .Select(c => new Renumbered(c.Dto.Id, c.Dto.Title, Name(c.Dto.Number), Name(NumberAfter(c.Dto.Id))))
            .ToList();
    }
}
