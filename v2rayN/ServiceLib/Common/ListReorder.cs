namespace ServiceLib.Common;

/// <summary>
/// Glide shows servers starred-first and through filters (search, Favourites tab), so the list on screen
/// is not the saved order. A move is made on the shown list, then folded back into the full saved
/// order next to its new visible neighbour; servers that aren't shown keep their places.
/// </summary>
public static class ListReorder
{
    /// <summary>The shown list after moving the item at <paramref name="index"/>.</summary>
    public static List<string> Move(IReadOnlyList<string> shown, int index, EMove move, int pos = -1)
    {
        var list = shown.ToList();
        if (index < 0 || index >= list.Count)
        {
            return list;
        }
        var id = list[index];
        list.RemoveAt(index);
        var to = move switch
        {
            EMove.Top => 0,
            EMove.Up => index - 1,
            EMove.Down => index + 1,
            EMove.Bottom => list.Count,
            // As before: lands where the target row was, in front of it.
            EMove.Position => pos > index ? pos - 1 : pos,
            _ => index,
        };
        list.Insert(Math.Clamp(to, 0, list.Count), id);
        return list;
    }

    /// <summary>
    /// The full saved order with the moved ids (a contiguous block in <paramref name="shownAfter"/>)
    /// placed in front of the first shown server after them, or behind the last one before them.
    /// </summary>
    public static List<string> Merge(IReadOnlyList<string> full, IReadOnlyList<string> shownAfter, IReadOnlyCollection<string> moved)
    {
        var movedSet = moved.ToHashSet();
        var block = shownAfter.Where(movedSet.Contains).ToList();
        var result = full.Where(id => !movedSet.Contains(id)).ToList();
        var first = shownAfter.ToList().FindIndex(movedSet.Contains);
        if (first < 0)
        {
            return full.ToList();
        }
        var after = shownAfter.Skip(first).FirstOrDefault(id => !movedSet.Contains(id) && result.Contains(id));
        var before = shownAfter.Take(first).LastOrDefault(result.Contains);
        var at = after != null ? result.IndexOf(after)
            : before != null ? result.IndexOf(before) + 1
            : result.Count;
        result.InsertRange(at, block);
        return result;
    }
}
