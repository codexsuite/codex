using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Codex.Application.Features.KnowledgeBase;
using Codex.Application.Features.Manuscript;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// D. Draft and revise: edit_prose and upsert_beat (#20, M11, M12). edit_prose writes through IProseStore (origin
// "agent") and cuts the session's 'agent' revision in the same transaction; upsert_beat is an adapter over the Write
// screen's beat commands (BlockCommands) and + Context (IKbLinks).
[McpServerToolType]
public sealed class ProseTools(
    McpScenes scenes, McpRefs refs, McpQueries queries, IProseStore prose, BlockCommands blocks, IKbLinks links, McpChangeSets changeSets, McpMentions mentions, McpUndo undo,
    IBookAccess access)
{
    public const string Origin = "agent";

    public sealed record ProseEdit(string Old, string New, int? Occurrence = null);

    public sealed record Anchor(string Text, int? Occurrence = null);

    public sealed record EditProseResult(bool DryRun, int Words, int WordDelta, string? Body) : McpWriteResult;

    public sealed record BeatResult(McpItem Beat, string Instruction, int? TargetWordCount, bool IsHidden, IReadOnlyList<McpItem> Context)
        : McpWriteResult;

    [McpServerTool(Name = "edit_prose", Title = "Fix this passage", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Exact-match replacements in one scene's prose, all or nothing: an `old` that is missing, or that matches more than once " +
                 "without `occurrence` (1-based), fails the whole call and writes nothing. Takes expected_version from read_manuscript; a stale " +
                 "one fails with conflict. An edit may not add ::beat or ::kb-addition lines or ref: links (use upsert_beat, link_kb_entry, " +
                 "cite_source); removing one archives it, as deleting it in Write does. dry_run returns the new body without writing. " +
                 "The scene's prose before this session's first edit is kept as a revision (Restore in Write), and undo_id reverses the edit.")]
    public async Task<EditProseResult> EditProseAsync(
        McpServer server,
        [Description("The scene: an id, or \"4.1\" with `book`.")] string scene,
        [Description("The scene's version from read_manuscript.")] int expected_version,
        [Description("Replacements applied in order: { old, new, occurrence? }.")] IReadOnlyList<ProseEdit> edits,
        [Description("The book, when `scene` is a display number (default: the most recently edited book).")] string? book = null,
        [Description("Report the result without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (edits is not { Count: > 0 })
        {
            throw McpToolException.Invalid("edits", "edits needs at least one { old, new }.");
        }
        var target = await scenes.SceneAsync(server, book, scene, cancellationToken);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
        var sceneId = target.Scene.Dto.Id;
        var current = await prose.GetAsync(sceneId, cancellationToken);
        if (current.Version != expected_version)
        {
            throw Conflict(target, expected_version, current.Version);
        }
        var edited = Apply(current.Body, edits);
        var removed = Removed(current.Body, edited);
        var sideEffects = removed.Select(e => $"{Describe(e.Kind)} removed from {target.Item.Label}: it is archived, as deleting it in Write does.").ToList();
        var affected = removed.Select(e => new McpAffected(EmbedKind(e.Kind), e.Id, "archived", "false", "true")).ToList();
        foreach (var use in McpProseRules.RefsTouched(current.Body, edited))
        {
            sideEffects.Add($"The passage of a source use in {target.Item.Label} changed; its excerpt and word count change, which can change clearance. Check References.");
            affected.Add(new McpAffected("source_use", use, "excerpt", null, null));
        }
        if (dry_run)
        {
            var words = ManuscriptText.CountWords(edited);
            return new EditProseResult(true, words, words - ManuscriptText.CountWords(current.Body), edited)
            {
                Item = target.Item, Version = current.Version, SideEffects = sideEffects, Affected = affected,
            };
        }

        var caller = McpCaller.From(server);
        string? before = null;
        int version;
        try
        {
            version = await prose.UpdateAsync(sceneId, body =>
                {
                    before = body;
                    return Apply(body, edits);
                }, expected_version, Origin,
                new ProseWriteOptions { InTransaction = changeSets.AgentRevision(sceneId, () => before, caller) }, cancellationToken);
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
        {
            throw Conflict(target, expected_version, (await prose.GetAsync(sceneId, cancellationToken)).Version);
        }
        var after = await scenes.ReloadAsync(server, target, cancellationToken);
        var undoId = await changeSets.RecordAsync(after.Outline.Book.Id, "edit_prose", $"Edited {target.Item.Label}", caller,
            [new McpUndoStep("prose", sceneId, Body: before, Version: version)], cancellationToken);
        return new EditProseResult(false, after.Scene.Dto.Words, after.Scene.Dto.Words - target.Scene.Dto.Words, null)
        {
            Item = after.Item, Version = version, SideEffects = sideEffects, Affected = affected, UndoId = undoId,
        };
    }

    [McpServerTool(Name = "upsert_beat", Title = "Plan what a passage should say", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a beat (omit `beat`; give `scene`, `at` and expected_version, since its ::beat line changes the prose) or updates one " +
                 "(`beat` id): instruction, target_word_count, is_hidden, and + Context entries (context_add / context_remove by name or alias). " +
                 "`at` is \"start\", \"end\", {\"after\": {\"text\": …}} or {\"before\": {\"text\": …}}; inside a paragraph the beat splits it.")]
    public async Task<BeatResult> UpsertBeatAsync(
        McpServer server,
        [Description("The beat to update; omit to create one.")] Guid? beat = null,
        [Description("Creating: the scene, an id or \"5.2\" with `book`.")] string? scene = null,
        [Description("Creating: where the ::beat line goes.")] JsonElement? at = null,
        [Description("Creating: the scene's version from read_manuscript.")] int? expected_version = null,
        [Description("What the passage should say.")] string? instruction = null,
        [Description("How long the drafted passage should be (e.g. 200, 400, 600).")] int? target_word_count = null,
        [Description("Knowledge base entries to add to the beat's context.")] IReadOnlyList<string>? context_add = null,
        [Description("Knowledge base entries to take out of the beat's context.")] IReadOnlyList<string>? context_remove = null,
        [Description("Hide the beat in reading views.")] bool? is_hidden = null,
        [Description("The book, when `scene` is a display number.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        var steps = new List<McpUndoStep>();
        McpScenes.Target target;
        Guid beatId;
        int? version = null;
        List<Guid> add;
        List<Guid> remove;
        if (target_word_count is <= 0)
        {
            throw McpToolException.Invalid("target_word_count", "target_word_count must be positive.");
        }
        if (beat is { } existing)
        {
            var sceneId = await queries.SceneOfBeatAsync(existing, cancellationToken) ?? throw McpToolException.NotFound($"No beat {existing}.");
            target = await scenes.SceneAsync(server, null, sceneId.ToString(), cancellationToken);
            await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
            beatId = existing;
            instruction = await mentions.ResolveAsync(instruction, target.Outline, cancellationToken);
            add = await EntriesAsync(context_add, target.Outline.Book, cancellationToken);
            remove = await EntriesAsync(context_remove, target.Outline.Book, cancellationToken);
        }
        else
        {
            if (scene == null || at == null || expected_version == null)
            {
                throw McpToolException.Invalid("scene", "Creating a beat needs scene, at and expected_version.");
            }
            target = await scenes.SceneAsync(server, book, scene, cancellationToken);
            await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
            instruction = await mentions.ResolveAsync(instruction, target.Outline, cancellationToken);
            add = await EntriesAsync(context_add, target.Outline.Book, cancellationToken);
            remove = await EntriesAsync(context_remove, target.Outline.Book, cancellationToken);
            var current = await prose.GetAsync(target.Scene.Dto.Id, cancellationToken);
            if (current.Version != expected_version)
            {
                throw Conflict(target, expected_version.Value, current.Version);
            }
            beatId = Guid.CreateVersion7();
            var body = InsertBeatLine(current.Body, at.Value, beatId);
            try
            {
                version = (await blocks.InsertBeatAsync(target.Scene.Dto.Id, new InsertBeatRequest(beatId, body, current.Version), cancellationToken)).Version;
            }
            catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
            {
                throw Conflict(target, expected_version.Value, (await prose.GetAsync(target.Scene.Dto.Id, cancellationToken)).Version);
            }
            await changeSets.CutAgentRevisionAsync(target.Scene.Dto.Id, current.Body, caller, cancellationToken);
            steps.Add(new McpUndoStep("prose", target.Scene.Dto.Id, Body: current.Body, Version: version));
        }

        var bookId = target.Outline.Book.Id;
        BeatDto afterBeat;
        try
        {
            var beforeBeat = (await blocks.ListAsync(bookId, cancellationToken)).Beats.SingleOrDefault(b => b.Id == beatId)
                             ?? throw McpToolException.NotFound($"No live beat {beatId}.");
            if (beat != null)
            {
                var contextAfter = add.Count > 0 || remove.Count > 0 ? beforeBeat.ContextEntryIds.Union(add).Except(remove).ToList() : null;
                steps.Add(new McpUndoStep("patch_beat", beatId, Body: beforeBeat.Body, Version: beforeBeat.TargetWordCount, Flag: beforeBeat.IsHidden,
                    Labels: beforeBeat.ContextEntryIds, Now: new McpUndoState(Body: instruction, Flag: is_hidden, Labels: contextAfter)));
            }
            if (instruction != null || target_word_count != null || is_hidden != null)
            {
                await blocks.PatchBeatAsync(beatId,
                    new PatchBeatRequest(instruction, is_hidden, target_word_count), cancellationToken);
            }
            if (add.Count > 0 || remove.Count > 0)
            {
                var context = beforeBeat.ContextEntryIds.Union(add).Except(remove).ToList();
                await links.SetBeatContextAsync(beatId, context, cancellationToken);
            }

            afterBeat = (await blocks.ListAsync(bookId, cancellationToken)).Beats.Single(b => b.Id == beatId);
        }
        catch (Exception)
        {
            // Everything was validated first, so this is a fault; leave no half-written beat behind.
            await undo.RunAsync(steps, CancellationToken.None);
            throw;
        }
        await scenes.ReloadAsync(server, target, cancellationToken);
        var undoId = await changeSets.RecordAsync(bookId, "upsert_beat",
            beat == null ? $"Added a beat to {target.Item.Label}" : $"Updated a beat in {target.Item.Label}", caller, steps, cancellationToken);
        var names = await ContextItemsAsync(afterBeat.ContextEntryIds, target.Outline.Book, cancellationToken);
        return new BeatResult(
            new McpItem(beatId, "beat", $"{target.Item.Label} · beat “{McpLabels.Plain(afterBeat.Body)}”", target.Item.OpenUrl),
            afterBeat.Body, afterBeat.TargetWordCount, afterBeat.IsHidden, names)
        {
            Item = target.Item, Version = version ?? (await prose.GetAsync(target.Scene.Dto.Id, cancellationToken)).Version, UndoId = undoId,
            SideEffects = beat == null ? [$"A ::beat line was added to {target.Item.Label}."] : [],
            Affected = [new McpAffected("beat", beatId, beat == null ? "created" : "updated", null, null)],
        };
    }

    // The conflict #20 2.3 describes: the current version, so the agent rereads before trying again.
    public static McpToolException Conflict(McpScenes.Target target, int expected, int current) =>
        new(McpErrorCodes.Conflict,
            $"{target.Item.Label} changed since version {expected}; it is now at version {current}. Read it again (read_manuscript) and retry.")
        {
            Candidates = [target.Item],
        };

    // All edits, in order, or an invalid error naming the first that cannot apply (M11: all or nothing).
    public static string Apply(string body, IReadOnlyList<ProseEdit> edits)
    {
        var text = body;
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            var field = $"edits[{i.ToString(CultureInfo.InvariantCulture)}].old";
            if (string.IsNullOrEmpty(edit.Old))
            {
                throw McpToolException.Invalid(field, "An edit needs a non-empty `old`.");
            }
            var at = Find(text, edit.Old, edit.Occurrence, field);
            text = string.Concat(text.AsSpan(0, at), edit.New ?? "", text.AsSpan(at + edit.Old.Length));
        }
        McpProseRules.RefuseInvented("edits", body, text);
        return text;
    }

    private static int Find(string text, string needle, int? occurrence, string field)
    {
        var matches = new List<int>();
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            matches.Add(at);
        }
        if (matches.Count == 0)
        {
            throw McpToolException.Invalid(field, $"\"{needle}\" is not in the scene's prose; nothing was changed.");
        }
        if (occurrence is { } n)
        {
            return n >= 1 && n <= matches.Count
                ? matches[n - 1]
                : throw McpToolException.Invalid(field, $"\"{needle}\" occurs {matches.Count} times; occurrence {n} is out of range.");
        }
        return matches.Count == 1
            ? matches[0]
            : throw McpToolException.Invalid(field, $"\"{needle}\" occurs {matches.Count} times; pass occurrence (1–{matches.Count}).");
    }

    private static string InsertBeatLine(string body, JsonElement at, Guid beatId) => InsertLine(body, at, $"::beat{{id=\"{beatId}\"}}");

    // A directive line (::beat, ::kb-addition) at `at` — "start", "end", {after: Anchor} or {before: Anchor} — as its own block.
    public static string InsertLine(string body, JsonElement at, string line)
    {
        int offset;
        if (at.ValueKind == JsonValueKind.String && at.GetString() is "start" or "end")
        {
            offset = at.GetString() == "start" ? 0 : body.Length;
        }
        else if (at.ValueKind == JsonValueKind.Object
                 && (at.TryGetProperty("after", out var anchor) || at.TryGetProperty("before", out anchor))
                 && anchor.Deserialize<Anchor>(McpJson.Options) is { Text.Length: > 0 } a)
        {
            var found = Find(body, a.Text, a.Occurrence, "at");
            offset = at.TryGetProperty("after", out _) ? found + a.Text.Length : found;
        }
        else
        {
            throw McpToolException.Invalid("at", "at is \"start\", \"end\", {\"after\": {\"text\": …}} or {\"before\": {\"text\": …}}.");
        }
        var left = body[..offset].TrimEnd();
        var right = body[offset..].TrimStart();
        return string.Join("\n\n", new[] { left, line, right }.Where(p => p.Length > 0)) + (right.Length == 0 ? "\n" : "");
    }

    private static HashSet<(ProseEmbedKind Kind, Guid Id)> Embeds(string body) =>
        ProseDocument.Parse(body).ListEmbeds().Select(e => (e.Kind, e.Id)).ToHashSet();

    private static List<(ProseEmbedKind Kind, Guid Id)> Removed(string before, string after) =>
        Embeds(before).Except(Embeds(after)).Where(e => e.Kind is ProseEmbedKind.Beat or ProseEmbedKind.KbAddition or ProseEmbedKind.Ref).ToList();

    private static string Describe(ProseEmbedKind kind) => kind switch
    {
        ProseEmbedKind.Beat => "a ::beat",
        ProseEmbedKind.KbAddition => "a ::kb-addition",
        _ => "a source use (ref:)",
    };

    private static string EmbedKind(ProseEmbedKind kind) => kind switch
    {
        ProseEmbedKind.Beat => "beat",
        ProseEmbedKind.KbAddition => "kb_addition",
        _ => "source_use",
    };

    private async Task<List<Guid>> EntriesAsync(IReadOnlyList<string>? names, McpBook book, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        foreach (var name in names ?? [])
        {
            ids.Add((await refs.EntryAsync(name, book, cancellationToken)).Id);
        }
        return ids;
    }

    private async Task<IReadOnlyList<McpItem>> ContextItemsAsync(IReadOnlyList<Guid> ids, McpBook book, CancellationToken cancellationToken)
    {
        var items = new List<McpItem>();
        foreach (var id in ids)
        {
            items.Add((await refs.EntryAsync(id.ToString(), book, cancellationToken)).Item);
        }
        return items;
    }
}
