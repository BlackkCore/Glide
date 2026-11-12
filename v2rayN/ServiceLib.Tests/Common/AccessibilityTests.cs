using System.Text.RegularExpressions;

namespace ServiceLib.Tests.Common;

public class AccessibilityTests
{
    // Tripwire (the window has no unit-test host): an icon-only button has no text, so screen readers need an explicit name.
    [Test]
    [Arguments("btnTopSubEdit")]
    [Arguments("btnTopSubAdd")]
    public async Task IconOnlyButtons_OfTheMainWindow_HaveAnAutomationName(string name)
    {
        var xaml = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop", "Views", "MainWindow.axaml"));
        var m = Regex.Match(xaml, $@"x:Name=""{name}""[^>]*>", RegexOptions.Singleline);
        await m.Success.Should().BeTrue();
        await m.Value.Contains("AutomationProperties.Name=").Should().BeTrue();
    }
}
