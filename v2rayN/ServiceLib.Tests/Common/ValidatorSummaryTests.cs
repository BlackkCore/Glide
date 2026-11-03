namespace ServiceLib.Tests.Common;

public class ValidatorSummaryTests
{
    [Test]
    public async Task WarningsOnly_BecomeOneLine()
    {
        var warnings = Enumerable.Range(0, 40).Select(i => $"Group Auto child node n{i} warning: WebSocket but ALPN").ToList();
        var text = NoticeManager.SummarizeValidator([], warnings);
        await text.Contains('\n').Should().BeFalse();
        await text.Should().BeEqualTo(string.Format(ResUI.TipValidatorWarnings, 40));
    }

    [Test]
    public async Task Errors_ShowThreeDistinctAndCountTheRest()
    {
        var text = NoticeManager.SummarizeValidator(["e1", "e1", "e2", "e3", "e4"], ["w1"]);
        var lines = text.Split(Environment.NewLine);
        await lines.Length.Should().BeEqualTo(4);
        await lines[3].Should().BeEqualTo(string.Format(ResUI.TipAndMoreInLog, 2));
    }

    [Test]
    public async Task Nothing_IsEmpty()
    {
        await NoticeManager.SummarizeValidator([], []).Should().BeEqualTo(string.Empty);
    }
}
