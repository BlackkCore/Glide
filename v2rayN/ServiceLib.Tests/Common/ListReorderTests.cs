namespace ServiceLib.Tests.Common;

public class ListReorderTests
{
    private static string J(IEnumerable<string> ids) => string.Join(",", ids);

    [Test]
    [Arguments(EMove.Top, 2, "c,a,b,d")]
    [Arguments(EMove.Up, 2, "a,c,b,d")]
    [Arguments(EMove.Down, 1, "a,c,b,d")]
    [Arguments(EMove.Bottom, 0, "b,c,d,a")]
    [Arguments(EMove.Up, 0, "a,b,c,d")]
    [Arguments(EMove.Down, 3, "a,b,c,d")]
    public async Task Move_OnShownList(EMove move, int index, string expected)
    {
        await J(ListReorder.Move(["a", "b", "c", "d"], index, move)).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Move_Position_LandsInFrontOfTarget()
    {
        await J(ListReorder.Move(["a", "b", "c", "d"], 0, EMove.Position, 2)).Should().BeEqualTo("b,a,c,d");
        await J(ListReorder.Move(["a", "b", "c", "d"], 3, EMove.Position, 1)).Should().BeEqualTo("a,d,b,c");
    }

    [Test]
    public async Task Merge_FilteredList_KeepsHiddenServersInPlace()
    {
        // Saved order a..f; a search shows only b, d, f. Moving f up past d.
        var full = new[] { "a", "b", "c", "d", "e", "f" };
        var shownAfter = ListReorder.Move(["b", "d", "f"], 2, EMove.Up);

        var order = ListReorder.Merge(full, shownAfter, ["f"]);

        await J(order).Should().BeEqualTo("a,b,c,f,d,e");
    }

    [Test]
    public async Task Merge_StarredFirst_DoesNotMakeStarOrderPermanent()
    {
        // Saved order a,b,c,d with c starred, shown as c,a,b,d. The view model moves d within the
        // unstarred group (a,b,d): Top puts it in front of a; c keeps its saved place behind b.
        var full = new[] { "a", "b", "c", "d" };
        var shownAfter = ListReorder.Move(["a", "b", "d"], 2, EMove.Top);

        var order = ListReorder.Merge(full, shownAfter, ["d"]);

        await J(order).Should().BeEqualTo("d,a,b,c");
    }

    [Test]
    public async Task Merge_MoveToEnd_GoesBehindLastShown()
    {
        var full = new[] { "a", "b", "c", "x" };
        var shownAfter = ListReorder.Move(["a", "b", "c"], 0, EMove.Bottom);

        await J(ListReorder.Merge(full, shownAfter, ["a"])).Should().BeEqualTo("b,c,a,x");
    }

    [Test]
    public async Task Merge_MultiSelectionBlock_StaysTogether()
    {
        var full = new[] { "a", "b", "c", "d", "e" };
        // Drag b and c to the end of a shown list that hides e.
        var shownAfter = new List<string> { "a", "d", "b", "c" };

        await J(ListReorder.Merge(full, shownAfter, ["b", "c"])).Should().BeEqualTo("a,d,b,c,e");
    }
}
