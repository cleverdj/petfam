namespace PetFam.Core.Talk;

/// <summary>What the pets see around them on one screen: the wallpaper in a few words, and the one before it.</summary>
/// <param name="Now">The current view ("a lake below snowy mountains, pine trees on the left").</param>
/// <param name="Before">The previous view, when the wallpaper has changed.</param>
/// <param name="Fresh">The view has just changed and the pets haven't talked about it yet.</param>
public sealed record Scenery(string Now, string? Before, bool Fresh);

/// <summary>
/// The view on each screen, as the user's AI described the wallpaper. When the wallpaper changes the old view is kept,
/// so the pets can notice the change ("we're by the sea now!") and compare ("I liked the mountains better").
/// </summary>
public sealed class SceneryBook
{
    private sealed class Entry
    {
        public required string Key;
        public required string Now;
        public string? Before;
        public bool Fresh;
    }

    private readonly Dictionary<string, Entry> _cells = new();

    /// <summary>
    /// The view on a screen. <paramref name="key"/> names the wallpaper: the same key again (re-read, re-detected)
    /// is no change. <paramref name="before"/> is the view before this one, when known from an earlier run.
    /// Returns true when the view changed from one the pets had seen.
    /// </summary>
    public bool Set(string cellId, string key, string text, string? before = null)
    {
        if (_cells.TryGetValue(cellId, out var e))
        {
            if (e.Key == key)
            {
                e.Now = text;
                return false;
            }
            _cells[cellId] = new Entry { Key = key, Now = text, Before = e.Now, Fresh = true };
            return true;
        }
        _cells[cellId] = new Entry { Key = key, Now = text, Before = before, Fresh = before is not null };
        return before is not null;
    }

    public Scenery? For(string? cellId) =>
        cellId is not null && _cells.TryGetValue(cellId, out var e) ? new Scenery(e.Now, e.Before, e.Fresh) : null;

    /// <summary>The screens whose view has just changed, not yet talked about.</summary>
    public IEnumerable<string> FreshCells => _cells.Where(kv => kv.Value.Fresh).Select(kv => kv.Key).ToList();

    /// <summary>The pets have talked about the new view.</summary>
    public void Noticed(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var e)) e.Fresh = false;
    }

    public void Forget(string cellId) => _cells.Remove(cellId);
}
