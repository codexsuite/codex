using Agoda.IoC.Core;
using Codex.Application.Features.Ai;
using Codex.Application.Features.Library;
using Codex.Application.Features.KnowledgeBase;
using Codex.Application.Features.References;
using Codex.Application.Features.Settings;
using Codex.Application.Features.Manuscript;
using Codex.Application.Features.Print;
using Codex.Application.Features.Print.Layout;
using Codex.Application.Features.Structure;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Library;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Codex.Contracts.References;
using Codex.Contracts.Settings;
using Codex.Contracts.Structure;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.Application.Mcp;

// Runs McpUndoSteps through the same commands the UI's Undo uses (#20 Safety, M14): undo_id reversals and
// edit_outline's roll-back of a batch that failed part way. Prose steps restore the body the write started from with
// the version the write left (a later edit makes that a conflict); every other step calls the feature's command.
[RegisterSingleton]
public sealed class McpUndo(
    IProseStore prose, PositionCommands positions, SceneArchiveCommands archives, ChapterArchiveCommands chapterArchives, ActCommands acts, ChapterCommands chapters,
    SceneCommands scenes, BlockCommands blocks,
    IKbLinks links, DraftCommands drafts, IKbEntryCommands entries, IKbDeleteCommands deletes, LabelCommands labels, NpgsqlDataSource dataSource,
    McpUndoChecks checks, BookLifecycleCommands bookLifecycle, BookSettingsCommands bookSettings, SeriesNumberCommands seriesNumbers,
    PublicationSettingsCommands publicationSettings, ISourceCommands sourceCommands, ISourceExtrasCommands sourceExtras, IUseCommands useCommands, FigureCommands figures, OpenerCommands openers,
    IServiceProvider services, ISourceMergeCommands sourceMerges, IKbCategoryCommands categoryCommands, IKbRelatedCommands relatedCommands, IKbMergeCommands kbMerges, SeriesCommands seriesCommands,
    ISourceRestoreCommands sourceRestores)
{
    // Checks what can be known before anything is written: a step undo cannot run yet (blocked) and prose that has
    // moved on since the write (conflict).
    public async Task CheckAsync(IReadOnlyList<McpUndoStep> steps, CancellationToken cancellationToken)
    {
        await checks.CheckAsync(steps, cancellationToken);
        foreach (var step in steps)
        {
            if (step.Op == "unsupported")
            {
                throw new McpToolException(McpErrorCodes.Blocked, $"This write can't be undone over MCP yet: {step.Title}. Undo it in Plan.")
                {
                    Rule = "undo_unsupported",
                };
            }
            if (step.Op == "prose" && (await prose.GetAsync(step.Id, cancellationToken)).Version is var now && now != step.Version)
            {
                throw new McpToolException(McpErrorCodes.Conflict,
                    $"The scene was edited after this write (it was left at version {step.Version}, it is now at {now}); nothing was undone.");
            }
        }
    }

    public async Task RunAsync(IReadOnlyList<McpUndoStep> steps, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            await RunAsync(step, cancellationToken);
        }
    }

    private async Task RunAsync(McpUndoStep s, CancellationToken ct)
    {
        switch (s.Op)
        {
            case "prose":
                try
                {
                    await prose.UpdateAsync(s.Id, _ => s.Body ?? "", s.Version ?? 0, "restore", ct);
                }
                catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
                {
                    throw new McpToolException(McpErrorCodes.Conflict, $"The scene was edited after this write; {e.Message}");
                }
                break;
            case "move_act":
                await positions.MoveActAsync(s.Id, new MoveActRequest(s.After), ct);
                break;
            case "move_chapter":
                await positions.MoveChapterAsync(s.Id, new MoveChapterRequest(s.Parent!.Value, s.After), ct);
                break;
            case "move_scene":
                await positions.MoveSceneAsync(s.Id, new MoveSceneRequest(s.Parent!.Value, s.After), ct);
                break;
            case "archive_scene":
                await archives.ArchiveAsync(s.Id, ct);
                break;
            case "restore_scene":
                await archives.RestoreAsync(s.Id, ct);
                break;
            case "archive_chapter":
                await chapterArchives.ArchiveAsync(s.Id, ct);
                break;
            case "restore_chapter":
                await chapterArchives.RestoreAsync(s.Id, ct);
                break;
            case "archive_act":
                await acts.ArchiveAsync(s.Id, ct);
                break;
            case "restore_act":
                await acts.RestoreAsync(s.Id, ct);
                break;
            case "patch_scene":
                await scenes.PatchAsync(s.Id, new PatchSceneRequest(s.Summary, s.Title, s.Status, s.Body), ct);
                break;
            case "patch_chapter":
                await chapters.PatchAsync(s.Id, new PatchChapterRequest(s.Title, s.Summary, s.Body, s.Flag), ct);
                break;
            case "patch_act":
                await acts.RenameAsync(s.Id, new PatchActRequest(s.Title ?? ""), ct);
                break;
            case "set_labels":
                await scenes.SetLabelsAsync(s.Id, new SetSceneLabelsRequest(s.Labels ?? []), ct);
                break;
            case "patch_beat":
                await blocks.PatchBeatAsync(s.Id, new PatchBeatRequest(s.Body, s.Flag, s.Version), ct);
                if (s.Version == null)
                {
                    // TODO: BlockCommands.PatchBeatAsync cannot clear target_word_count (null leaves it); a clear option there replaces this.
                    await using var connection = await dataSource.OpenConnectionAsync(ct);
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE beat SET target_word_count = NULL, updated_at = now() WHERE id = @id", new { id = s.Id }, cancellationToken: ct));
                }
                await links.SetBeatContextAsync(s.Id, s.Labels ?? [], ct);
                break;
            case "discard_draft":
                await drafts.DiscardAsync(s.Id, ct);
                break;
            case "undo_apply":
                await drafts.UndoApplyAsync(s.Id, new DraftVersionRequest(s.Version ?? 0), ct);
                break;
            case "restore_draft":
                await drafts.UndoDiscardAsync(s.Id, ct);
                break;
            case "link_scene":
                await links.LinkSceneAsync(s.Parent!.Value, s.Id, ct);
                break;
            case "unlink_scene":
                await links.UnlinkSceneAsync(s.Parent!.Value, s.Id, ct);
                break;
            case "patch_entry":
                await entries.PatchAsync(s.Id, new PatchKbEntryRequest(s.Title, s.Summary, s.Body, s.Fields, null, s.Texts, s.Flag, null, s.Tags, s.Exclude), ct);
                break;
            case "delete_label":
                await labels.DeleteAsync(s.Id, ct);
                break;
            case "archive_book":
                await bookLifecycle.ArchiveAsync(s.Id, ct);
                break;
            case "patch_book_settings":
                await bookSettings.PatchAsync(s.Id, new PatchBookSettingsRequest(s.Title, s.Summary, s.Status, s.Version, s.Body, s.Subtitle, s.Description), ct);
                break;
            case "set_layout":
                await services.GetRequiredService<ILayoutService>().SelectAsync(s.Id, new LayoutSelectionRequest(s.Title ?? "", s.Summary,
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(s.Body ?? "{}", McpJson.Options)), ct);
                break;
            case "set_series_number":
                await seriesNumbers.SetAsync(s.Id, new SetSeriesNumberRequest(s.Version ?? 1), ct);
                break;
            case "reset_publication":
                await publicationSettings.ResetAsync(s.Id, s.Title ?? "", ct);
                break;
            case "set_publication":
                await publicationSettings.PatchAsync(s.Id, PublicationValue(s.Title, s.Body), ct);
                break;
            case "archive_source":
                await sourceCommands.ArchiveAsync(s.Id, ct);
                break;
            case "restore_source":
                await sourceRestores.RestoreAsync(s.Id, s.Labels ?? [], ct);
                break;
            case "patch_source":
                await sourceCommands.PatchAsync(s.Id, new PatchSourceRequest(
                    JsonSerializer.Deserialize<SourceDetailsRequest>(s.Body ?? "null", McpJson.Options), new SourceEntryRequest(s.Parent)), ct);
                break;
            case "set_use_kind":
                await sourceCommands.SetUseKindAsync(s.Id, new PatchUseRequest(s.Title ?? "paraphrase"), ct);
                break;
            case "set_use_locator":
                await useCommands.SetLocatorAsync(s.Id, new SetUseLocatorRequest(s.Title), ct);
                break;
            case "set_source_extras":
            {
                var back = JsonSerializer.Deserialize<SourceExtrasDto>(s.Body ?? "null", McpJson.Options)!;
                await sourceExtras.SetAsync(s.Id, new SourceExtrasRequest(back.CiteKey ?? "", back.PermissionsContact ?? ""), ct);
                break;
            }
            case "withdraw_permission":
                await PermissionTools.CommandsOf(services).WithdrawAsync(s.Id, ct);
                break;
            case "unmerge_source":
                await sourceMerges.UnmergeAsync(JsonSerializer.Deserialize<SourceMergeSnapshot>(s.Body ?? "null", McpJson.Options)!, ct);
                break;
            case "patch_figure":
                await figures.UpdateAsync(s.Id, new PatchFigureRequest(s.Parent, s.Status, s.Summary ?? "", s.Body ?? ""), ct);
                break;
            case "set_opener" when s.Title == "act":
                await openers.SetActAsync(s.Id, new SetOpenerRequest(s.Parent, s.Summary), ct);
                break;
            case "set_opener":
                await openers.SetChapterAsync(s.Id, new SetOpenerRequest(s.Parent, s.Summary), ct);
                break;
            case "archive_series":
                await seriesCommands.ArchiveAsync(s.Id, ct);
                break;
            case "restore_series":
                await seriesCommands.RestoreAsync(s.Id, ct);
                break;
            case "patch_series":
                await seriesCommands.PatchAsync(s.Id, new PatchSeriesRequest(s.Title, s.Description ?? ""), ct);
                break;
            case "set_series_order":
                await seriesCommands.SetOrderAsync(s.Id, new SetSeriesOrderRequest(s.Labels ?? []), ct);
                break;
            case "unsupported":
                // Recorded by older versions for an added chapter; CheckAsync refuses to undo it, and a roll-back has nothing to run.
                break;
            case "archive_entry":
                await deletes.DeleteAsync(s.Id, ct);
                break;
            case "patch_category":
                await categoryCommands.PatchAsync(s.Id, new PatchKbCategoryRequest(s.Title, s.Summary), ct);
                break;
            case "move_category":
                await categoryCommands.ReorderAsync(s.Id, new MoveKbCategoryRequest(s.After), ct);
                break;
            case "delete_category":
                await categoryCommands.DeleteAsync(s.Id, ct);
                break;
            case "restore_category":
                await categoryCommands.UnarchiveAsync(s.Id, ct);
                break;
            case "set_entry_category":
                await entries.PatchAsync(s.Id, new PatchKbEntryRequest(null, null, null, null, null, null, null, s.Parent, null, null), ct);
                break;
            case "relate_entries":
                await relatedCommands.AddAsync(s.Id, s.Parent!.Value, ct);
                break;
            case "unrelate_entries":
                await relatedCommands.RemoveAsync(s.Id, s.Parent!.Value, ct);
                break;
            case "restore_entry":
                await deletes.RestoreAsync(s.Id, JsonSerializer.Deserialize<KbUndoRecordDto>(s.Body ?? "null", McpJson.Options)!, ct);
                break;
            case "unmerge_entry":
                await kbMerges.UnmergeAsync(JsonSerializer.Deserialize<KbMergeSnapshot>(s.Body ?? "null", McpJson.Options)!, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown undo step {s.Op}.");
        }
    }

    // A set_publication step: the field's key and its value as JSON.
    private static PatchPublicationSettingsRequest PublicationValue(string? field, string? json)
    {
        var value = JsonDocument.Parse(json ?? "null").RootElement;
        return field switch
        {
            "style" => new(value.GetString(), null, null, null, null, null, null),
            "quoteWordLimit" => new(null, value.GetInt32(), null, null, null, null, null),
            "publisher" => new(null, null, value.GetString(), null, null, null, null),
            "plannedYear" => new(null, null, null, value.GetInt32(), null, null, null),
            "formats" => new(null, null, null, null, value.Deserialize<List<string>>(), null, null),
            "printRun" => new(null, null, null, null, null, value.GetInt32(), null),
            "territory" => new(null, null, null, null, null, null, value.GetString()),
            _ => throw new InvalidOperationException($"Unknown publication field {field}."),
        };
    }
}
