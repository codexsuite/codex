using System.ComponentModel;
using Codex.Application.Features.Ai;
using Codex.Application.Features.Manuscript;
using Codex.Contracts.Ai;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// B. Read: read_manuscript and get_draft (#20, M2). Adapters over the Write screen's reads: ManuscriptQuery (bodies and
// versions), ManuscriptContexts (beats, additions, markers, entry names, pending drafts) and DraftQueries.
[McpServerToolType]
public sealed class ReadTools(
    McpRefs refs, McpQueries queries, ManuscriptQuery manuscript, ManuscriptContexts contexts, DraftQueries drafts)
{
    private const int DefaultMaxWords = 4000;

    // One page at most: about a quarter of a long novel's act, and well inside what a client takes in one result.
    private const int MaxMaxWords = 20000;

    public sealed record SceneText(
        McpItem Item, int Version, int Words, bool Complete, string? Text, string? Body, IReadOnlyList<DraftView>? PendingDrafts);

    public sealed record DraftView(
        McpItem Item, Guid? BeatId, string Kind, string Model, int Attempt, string? Template, int? TargetWords, int Words,
        int Paragraphs, string Status, string Text, int? InputTokens, int? OutputTokens);

    public sealed record ReadManuscriptResult(string Format, IReadOnlyList<SceneText> Scenes, int Words, string? Cursor) : McpResult;

    public sealed record GetDraftResult(McpItem Scene, int SceneVersion, DraftView Draft) : McpResult;

    [McpServerTool(Name = "read_manuscript", Title = "Let me read it", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A book's prose in reading order, one block per scene with its label and version (edit_prose needs it). " +
                 "annotated (default) shows beats, Knowledge base additions, source markers and pending AI drafts as readable text; " +
                 "markdown is the body exactly as stored, with pending drafts listed beside it. Stops near max_words (at most 20000) and returns " +
                 "a cursor to continue; a scene split across pages reads whole when its parts are joined (markdown: as is; annotated: with a blank line).")]
    public async Task<ReadManuscriptResult> ReadManuscriptAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        [Description("\"book\" (default), or an act, chapter or scene: \"act:2\", \"5\", \"5.2\", \"ch:<slug>\" or an id.")] string scope = "book",
        [Description("annotated (default) or markdown.")] string format = "annotated",
        [Description("Show hidden beats in annotated text (default true).")] bool include_hidden_beats = true,
        [Description("Stop after about this many words (default 4000, at most 20000).")] int max_words = DefaultMaxWords,
        [Description("The cursor a previous call returned, to continue from there.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var annotated = format switch
        {
            "annotated" => true,
            "markdown" => false,
            _ => throw McpToolException.Invalid("format", "format is annotated or markdown."),
        };
        if (max_words is < 1 or > MaxMaxWords)
        {
            throw McpToolException.Invalid("max_words", $"max_words is between 1 and {MaxMaxWords}.");
        }
        var target = await refs.BookAsync(book, cancellationToken);
        var outline = await refs.OutlineAsync(target, McpRefs.SessionKey(server), cancellationToken);
        var (scopeItem, scenes) = Scope(outline, scope);
        var context = await contexts.ForBookAsync(target.Id, cancellationToken);
        var bodies = (await manuscript.GetAsync(target.Id, cancellationToken)).Scenes.ToDictionary(s => s.SceneId);

        var (startScene, startBlock) = ParseCursor(cursor, scenes);
        var result = new List<SceneText>();
        var words = 0;
        string? next = null;
        for (var s = startScene; s < scenes.Count && next == null; s++)
        {
            var scene = scenes[s];
            var prose = bodies[scene.Dto.Id];
            var sceneDrafts = context.Drafts.Where(d => d.SceneId == scene.Dto.Id).ToList();
            var sceneBlocks = ManuscriptText.Blocks(prose.Body, context with { Drafts = sceneDrafts }, annotated, include_hidden_beats);
            var first = s == startScene ? startBlock : 0;
            if (first > 0 && first >= sceneBlocks.Count)
            {
                throw McpToolException.Invalid("cursor", "That cursor does not point into this scope any more; read again without one.");
            }
            var taken = new List<string>();
            var b = first;
            for (; b < sceneBlocks.Count; b++)
            {
                if (words > 0 && words + sceneBlocks[b].Words > max_words)
                {
                    next = $"{scene.Dto.Id}:{b}";
                    break;
                }
                words += sceneBlocks[b].Words;
                taken.Add(sceneBlocks[b].Text);
            }
            if (next != null && b == first)
            {
                // Nothing of this scene fits: it starts the next page instead of leaving an empty part here.
                break;
            }
            var complete = first == 0 && b == sceneBlocks.Count;
            // markdown parts are slices of the stored body that meet end to end (the whitespace between blocks goes
            // with the part before it), so the parts concatenate to the body exactly.
            var from = first == 0 ? 0 : sceneBlocks[first].Start;
            var to = b == sceneBlocks.Count ? prose.Body.Length : sceneBlocks[b].Start;
            var text = annotated ? string.Join("\n\n", taken) : prose.Body[from..to];
            result.Add(new SceneText(
                scene.Item(target.Id), prose.Version, scene.Dto.Words, complete,
                annotated ? text : null, annotated ? null : text,
                annotated ? null : sceneDrafts.Select(d => View(d, scene.Item(target.Id), context)).ToList()));
        }
        return new ReadManuscriptResult(format, result, words, next) { Item = scopeItem };
    }

    [McpServerTool(Name = "get_draft", Title = "Show me that draft", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("One AI draft (pending, applied or discarded): its text, model, attempt, template, words, tokens, and the scene's current version.")]
    public async Task<GetDraftResult> GetDraftAsync(
        McpServer server,
        [Description("The draft (generation) id, as resume_work or read_manuscript return it.")] Guid draft,
        CancellationToken cancellationToken = default)
    {
        var bookId = await queries.BookOfAsync(null, draft, cancellationToken)
                     ?? throw McpToolException.NotFound($"No draft {draft}.");
        var dto = await drafts.GetAsync(draft, cancellationToken);
        var outline = await refs.OutlineAsync(await refs.BookAsync(bookId.ToString(), cancellationToken), McpRefs.SessionKey(server), cancellationToken);
        var scene = outline.SceneById(dto.SceneId)?.Item(bookId)
                    ?? throw McpToolException.NotFound($"The scene of draft {draft} is no longer in the outline.");
        var prose = await queries.SceneProseAsync(dto.SceneId, cancellationToken);
        var context = await contexts.ForBookAsync(bookId, cancellationToken);
        var view = View(dto, scene, context);
        return new GetDraftResult(scene, prose.Version, view) { Item = view.Item };
    }

    // What resume_work and read_manuscript say about a draft. Its open_url is the scene in Write, where the draft panel is.
    public static DraftView View(DraftDto draft, McpItem scene, ManuscriptText.Context context)
    {
        var beat = draft.BeatId is { } id && context.Beats.TryGetValue(id, out var b) ? b.Body.Trim() : null;
        var label = beat == null ? $"{scene.Label} · continue" : $"{scene.Label} · beat “{McpLabels.Plain(beat)}”";
        return new DraftView(
            new McpItem(draft.Id, "draft", label, scene.OpenUrl), draft.BeatId, draft.Kind, draft.Model, draft.Attempt, draft.Template,
            draft.TargetWords, draft.Words, ManuscriptText.Paragraphs(draft.Text), draft.Status, draft.Text, draft.InputTokens, draft.OutputTokens);
    }

    private static (McpItem? Item, List<BookOutline.Scene> Scenes) Scope(BookOutline outline, string scope)
    {
        if (string.Equals(scope.Trim(), "book", StringComparison.OrdinalIgnoreCase))
        {
            return (outline.Book.Item, outline.Scenes.ToList());
        }
        var item = McpRefs.Item(outline, scope);
        var scenes = item.Kind switch
        {
            "act" => outline.Scenes.Where(s => s.Chapter.Act.Dto.Id == item.Id),
            "chapter" => outline.Scenes.Where(s => s.Chapter.Dto.Id == item.Id),
            _ => outline.Scenes.Where(s => s.Dto.Id == item.Id),
        };
        return (item, scenes.ToList());
    }

    // "<scene id>:<block index>" → where to start; no cursor starts at the first scene.
    private static (int Scene, int Block) ParseCursor(string? cursor, List<BookOutline.Scene> scenes)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return (0, 0);
        }
        var parts = cursor.Split(':');
        var index = parts.Length == 2 && Guid.TryParse(parts[0], out var sceneId) ? scenes.FindIndex(s => s.Dto.Id == sceneId) : -1;
        if (index < 0 || !int.TryParse(parts[1], out var block) || block < 0)
        {
            throw McpToolException.Invalid("cursor", "That cursor does not point into this scope any more; read again without one.");
        }
        return (index, block);
    }
}
