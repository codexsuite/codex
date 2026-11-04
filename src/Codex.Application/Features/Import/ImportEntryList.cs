namespace Codex.Application.Features.Import;

// The knowledge base entries a parse collects, by key, up to ImportLimits.MaxEntries. Names past the cap are counted
// (Dropped) and left out, with their scene links.
public sealed class ImportEntryList
{
    private readonly List<ImportEntryPlan> _items = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dropped = new(StringComparer.Ordinal);

    public IReadOnlyList<ImportEntryPlan> Items => _items;

    public int Dropped => _dropped.Count;

    // Swaps in the entry kept under key (its name or text changed after it was added).
    public void Replace(ImportEntryPlan entry)
    {
        var index = _items.FindIndex(e => e.Key == entry.Key);
        if (index >= 0)
        {
            _items[index] = entry;
        }
    }

    // True when the entry is (now) in the list; false when the cap left it out.
    public bool Add(ImportEntryPlan entry)
    {
        if (_keys.Contains(entry.Key))
        {
            return true;
        }
        if (_items.Count >= ImportLimits.MaxEntries)
        {
            _dropped.Add(entry.Key);
            return false;
        }
        _keys.Add(entry.Key);
        _items.Add(entry);
        return true;
    }
}
