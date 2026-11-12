using ServiceLib.Tests.Common;

namespace ServiceLib.Tests.Services;

/// <summary>
/// "Allow Store apps to use the proxy" used to start the third-party EnableLoopback.exe, which Glide may not
/// redistribute. It now drives Windows' own CheckNetIsolation. These tests cover everything that does not change the
/// system: reading its output, validating names, and building the one elevated command line.
/// </summary>
public class LoopbackExemptTests
{
    private const string Calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe";
    private const string Exe = @"C:\Windows\System32\CheckNetIsolation.exe";

    [Test]
    [Arguments(Calc, true)]
    [Arguments("microsoft.windows.cortana_cw5n1h2txyewy", true)]
    [Arguments("A_0123456789abc", true)]
    [Arguments("", false)]
    [Arguments("Calc", false)]
    [Arguments("Microsoft.WindowsCalculator_8wekyb3d8bbw", false)]
    [Arguments("Microsoft.WindowsCalculator_8wekyb3d8bbwe & calc", false)]
    [Arguments("a b_8wekyb3d8bbwe", false)]
    [Arguments("a\"b_8wekyb3d8bbwe", false)]
    [Arguments("a;b_8wekyb3d8bbwe", false)]
    [Arguments("..\\x_8wekyb3d8bbwe", false)]
    public async Task FamilyNames_AreValidatedStrictly(string name, bool valid)
    {
        await LoopbackExempt.IsValidFamilyName(name).Should().BeEqualTo(valid);
    }

    [Test]
    public async Task NullName_IsNotValid()
    {
        await LoopbackExempt.IsValidFamilyName(null).Should().BeFalse();
    }

    [Test]
    public async Task ExemptList_IsReadFromTheSids_NotFromTheTranslatedLabels()
    {
        var english = """
            List Loopback Exempted AppContainers

            [1] -----------------------------------------------------------------
                Name: microsoft.windows.cortana_cw5n1h2txyewy
                SID:  S-1-15-2-1861897761-1695161497-2927542615-642690995-327840285-2659745135-2630312742

            [2] -----------------------------------------------------------------
                Name: microsoft.windowscalculator_8wekyb3d8bbwe
                SID:  S-1-15-2-3314302012-2349481134-1259631190-2347331124-4217893541-2149510547-2312461122

            OK.
            """;
        var persianLabels = "[1] ---\n    نام: x_8wekyb3d8bbwe\n    شناسه: S-1-15-2-1-2-3-4-5-6-7\n";

        var sids = LoopbackExempt.ParseExemptSids(english);
        await sids.Count.Should().BeEqualTo(2);
        await sids.Contains("S-1-15-2-3314302012-2349481134-1259631190-2347331124-4217893541-2149510547-2312461122").Should().BeTrue();
        await LoopbackExempt.ParseExemptSids(persianLabels).Contains("S-1-15-2-1-2-3-4-5-6-7").Should().BeTrue();
    }

    [Test]
    public async Task EmptyOrUnrelatedOutput_MeansNoExemptions()
    {
        await LoopbackExempt.ParseExemptSids("").Count.Should().BeEqualTo(0);
        await LoopbackExempt.ParseExemptSids(null).Count.Should().BeEqualTo(0);
        await LoopbackExempt.ParseExemptSids("List Loopback Exempted AppContainers\n\nOK.\n").Count.Should().BeEqualTo(0);
        // a user SID is not an AppContainer SID
        await LoopbackExempt.ParseExemptSids("S-1-5-21-1-2-3-1001").Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task FriendlyName_UsesTheDisplayName_OrTheFamilyNameForAResourceReference()
    {
        await LoopbackExempt.FriendlyName("Calculator", Calc).Should().BeEqualTo("Calculator");
        await LoopbackExempt.FriendlyName("@{Microsoft.WindowsCalculator?ms-resource://x/y}", Calc).Should().BeEqualTo("Microsoft.WindowsCalculator");
        await LoopbackExempt.FriendlyName("ms-resource:AppName", Calc).Should().BeEqualTo("Microsoft.WindowsCalculator");
        await LoopbackExempt.FriendlyName(null, Calc).Should().BeEqualTo("Microsoft.WindowsCalculator");
        await LoopbackExempt.FriendlyName("  ", Calc).Should().BeEqualTo("Microsoft.WindowsCalculator");
    }

    [Test]
    public async Task OneCommandLine_AppliesEveryChange_WithOnePrompt()
    {
        var line = LoopbackExempt.BuildCommandLine([Calc], ["microsoft.windows.cortana_cw5n1h2txyewy"], Exe);

        await line.Should().BeEqualTo(
            "/d /s /c \"\"C:\\Windows\\System32\\CheckNetIsolation.exe\" LoopbackExempt -a -n=Microsoft.WindowsCalculator_8wekyb3d8bbwe"
            + " & \"C:\\Windows\\System32\\CheckNetIsolation.exe\" LoopbackExempt -d -n=microsoft.windows.cortana_cw5n1h2txyewy\"");
    }

    [Test]
    public async Task NothingToChange_GivesNoCommandLine()
    {
        await LoopbackExempt.BuildCommandLine([], [], Exe).Should().BeNull();
    }

    [Test]
    public async Task AnInvalidName_NeverReachesACommandLine()
    {
        var threw = false;
        try
        {
            LoopbackExempt.BuildCommandLine([Calc, "x & calc"], [], Exe);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        await threw.Should().BeTrue();
    }

    [Test]
    public async Task EnableLoopbackExe_IsNoLongerStartedByGlide()
    {
        var menu = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop", "Views", "MainWindow.axaml.cs"));
        await menu.Contains("ProgramBinFile(\"EnableLoopback").Should().BeFalse();
        await menu.Contains("LoopbackSheet.Open()").Should().BeTrue();
    }
}
