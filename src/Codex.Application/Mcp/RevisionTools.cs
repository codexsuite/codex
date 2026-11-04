using System.ComponentModel;
using Codex.Application.Features.Manuscript;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// B. Read, a scene's version history: list_revisions (#20 review 2, "restore an old version": a read tool first), over
// ProseHistory, the History panel in Write (J15). Undo lasts 24 hours; the history keeps the last 50 revisions. Restoring one
// is an ordinary edit: edit_prose with the old text, or Restore in Write.
[McpServerToolType]
public sealed class RevisionTools(McpScenes scenes, ProseHistory history)
{
    public sealed record Revision(
        Guid Id, string Source, string Label, string? Detail, DateTimeOffset CreatedAt, string TimeLabel, int Words, bool Current);

    public sealed record RevisionText(Guid Id, string Label, DateTimeOffset CreatedAt, int Words, string Markdown);

    public sealed record ListRevisionsResult(int Version, int Words, IReadOnlyList<Revision> Revisions, RevisionText? Revision) : McpResult;

    [McpServerTool(Name = "list_revisions", Title = "What did this scene say before?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A scene's saved versions, newest first (the last 50, as History in Write shows them): each revision's id, how it came to be " +
                 "(source: manual, autosave, ai_apply, restore, agent, import, replaced; label: \"Edited\", \"AI draft applied\", \"Edited by <client>\"), " +
                 "when, its words, and which one matches the scene now (current). Give `revision` to read that version's text as stored Markdown. " +
                 "To put an old version back, edit_prose the scene with that text, or restore it from History in Write.")]
    public async Task<ListRevisionsResult> ListRevisionsAsync(
        McpServer server,
        [Description("The scene: an id, or \"5.2\" with `book`.")] string scene,
        [Description("A revision id from this list: returns its text.")] Guid? revision = null,
        [Description("The book, when `scene` is a display number.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var target = await scenes.SceneAsync(server, book, scene, cancellationToken);
        var dto = await history.ListAsync(target.Scene.Dto.Id, cancellationToken);
        var rows = dto.Revisions.Select(r => new Revision(r.Id, r.Source, r.Label, r.Detail, r.CreatedAt, r.TimeLabel, r.Words, r.Current)).ToList();
        RevisionText? text = null;
        if (revision is { } id)
        {
            var found = dto.Revisions.FirstOrDefault(r => r.Id == id)
                        ?? throw McpToolException.NotFound($"No revision {id} among the last {dto.Revisions.Count} of {target.Item.Label}.");
            text = new RevisionText(found.Id, found.Label, found.CreatedAt, found.Words, found.Body);
        }
        return new ListRevisionsResult(dto.Version, dto.CurrentWords, rows, text) { Item = target.Item };
    }
}
