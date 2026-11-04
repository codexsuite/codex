using Codex.Contracts.Structure;

namespace Codex.Application.Mcp;

// edit_outline's model of a book's live outline: the order of acts (under the book), chapters (under their act) and
// scenes (under their chapter), changed op by op so each op is placed against the outline the ops before it left,
// as the commands will see it. Chapter numbers are counted the way chapter_outline counts them: numbered chapters in
// reading order.
public sealed class OutlineModel
{
    private readonly Dictionary<Guid, List<Guid>> _children = [];
    private readonly Dictionary<Guid, Guid> _parent = [];
    private readonly HashSet<Guid> _numbered = [];
    private readonly Guid _book;

    private OutlineModel(Guid book) => _book = book;

    public static OutlineModel From(OutlineDto dto)
    {
        var model = new OutlineModel(dto.BookId);
        foreach (var act in dto.Acts)
        {
            model.Insert(act.Id, dto.BookId, model.Last(dto.BookId), false);
            foreach (var chapter in act.Chapters.Where(c => !c.Archived))
            {
                model.Insert(chapter.Id, act.Id, model.Last(act.Id), chapter.Number != null);
                foreach (var scene in chapter.Scenes)
                {
                    model.Insert(scene.Id, chapter.Id, model.Last(chapter.Id), false);
                }
            }
        }
        return model;
    }

    public IReadOnlyList<Guid> Children(Guid parent) => _children.TryGetValue(parent, out var list) ? list : [];

    // Where an item is now: its parent and the sibling before it (null when it is first).
    public (Guid Parent, Guid? After) Locate(Guid id)
    {
        var parent = ParentOf(id);
        var siblings = _children[parent];
        var at = siblings.IndexOf(id);
        return (parent, at > 0 ? siblings[at - 1] : null);
    }

    // Where an item is now, or null when an earlier op archived it.
    public (Guid Parent, Guid? After)? TryLocate(Guid id) => _parent.ContainsKey(id) ? Locate(id) : null;

    public void Insert(Guid id, Guid parent, Guid? after, bool numbered)
    {
        if (!_children.TryGetValue(parent, out var siblings))
        {
            siblings = [];
            _children[parent] = siblings;
        }
        siblings.Insert(after is { } a ? siblings.IndexOf(a) + 1 : 0, id);
        _parent[id] = parent;
        if (numbered)
        {
            _numbered.Add(id);
        }
    }

    // A chapter taken out of the numbering (front matter) or put back in; the ones after it renumber.
    public void SetNumbered(Guid id, bool numbered)
    {
        if (numbered)
        {
            _numbered.Add(id);
        }
        else
        {
            _numbered.Remove(id);
        }
    }

    public void Move(Guid id, Guid parent, Guid? after)
    {
        var numbered = _numbered.Contains(id);
        _children[ParentOf(id)].Remove(id);
        Insert(id, parent, after, numbered);
    }

    // An archived act or chapter takes what is in it out of the batch with it.
    public void Remove(Guid id)
    {
        _children[ParentOf(id)].Remove(id);
        Forget(id);
    }

    private void Forget(Guid id)
    {
        _parent.Remove(id);
        if (_children.Remove(id, out var children))
        {
            children.ForEach(Forget);
        }
    }

    // The item is in the model now: not archived by an earlier op of the batch.
    public void RequireLive(Guid id) => ParentOf(id);

    private Guid ParentOf(Guid id) =>
        _parent.TryGetValue(id, out var parent)
            ? parent
            : throw McpToolException.Invalid("ops", "An op refers to an item that an earlier op of the batch archived (or the chapter or act it is in).");

    public IReadOnlyDictionary<Guid, int> ChapterNumbers()
    {
        var numbers = new Dictionary<Guid, int>();
        var next = 1;
        foreach (var chapter in Children(_book).SelectMany(Children).Where(_numbered.Contains))
        {
            numbers[chapter] = next++;
        }
        return numbers;
    }

    public Guid? Last(Guid parent, Guid? except = null) => Children(parent).Where(id => id != except).Select(id => (Guid?)id).LastOrDefault();
}
