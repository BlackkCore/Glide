namespace ServiceLib.Tests.Common;

public class GlidePathsTests
{
    private const string Program = @"C:\Program Files\Glide";
    private const string LocalAppData = @"C:\Users\u\AppData\Local";

    [Test]
    public async Task DataRoot_Installed_IsLocalAppDataGlide()
    {
        await GlidePaths.GetDataRoot(Program, true, LocalAppData).Should().BeEqualTo(@"C:\Users\u\AppData\Local\Glide");
    }

    [Test]
    public async Task DataRoot_Portable_IsTheProgramFolder()
    {
        await GlidePaths.GetDataRoot(@"D:\Glide", false, LocalAppData).Should().BeEqualTo(@"D:\Glide");
    }

    // Found on a Windows runner: an installed Glide whose Program Files is not writable (every ordinary user) kept
    // its data in %LOCALAPPDATA%\v2rayN, the original v2rayN's folder, instead of %LOCALAPPDATA%\Glide.
    [Test]
    public async Task StartupPath_Installed_IsTheGlideDataFolder_EvenWhenTheNoWriteSwitchIsOn()
    {
        var data = GlidePaths.GetDataRoot(Program, true, LocalAppData);
        await GlidePaths.GetStartupPath(true, data, true, LocalAppData, Program + @"\").Should().BeEqualTo(@"C:\Users\u\AppData\Local\Glide");
        await GlidePaths.GetStartupPath(true, data, false, LocalAppData, Program + @"\").Should().BeEqualTo(@"C:\Users\u\AppData\Local\Glide");
    }

    [Test]
    public async Task StartupPath_Portable_KeepsTheUpstreamFallbacks()
    {
        await GlidePaths.GetStartupPath(false, @"D:\Glide", false, LocalAppData, @"D:\Glide\").Should().BeEqualTo(@"D:\Glide\");
        await GlidePaths.GetStartupPath(false, @"D:\Glide", true, LocalAppData, @"D:\Glide\").Should().BeEqualTo(@"C:\Users\u\AppData\Local\v2rayN");
    }

    [Test]
    [Arguments("Xray")]
    [Arguments("sing_box")]
    [Arguments("mihomo")]
    public async Task Installed_CoreExecutables_NeverResolveUnderTheDataFolder(string coreType)
    {
        var data = GlidePaths.GetDataRoot(Program, true, LocalAppData);
        var folder = GlidePaths.GetBinFolder(Program, data, true, coreType);

        await folder.StartsWith(Program + @"\bin\", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        await folder.StartsWith(data, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Test]
    public async Task Installed_SharedBin_IsData()
    {
        // Geo files, rule sets and sing-box's cache.db are written at runtime: they stay writable.
        var data = GlidePaths.GetDataRoot(Program, true, LocalAppData);
        await GlidePaths.GetBinFolder(Program, data, true, null).Should().BeEqualTo(data + @"\bin");
    }

    [Test]
    public async Task Portable_LayoutIsUnchanged()
    {
        await GlidePaths.GetBinFolder(@"D:\Glide", @"D:\Glide", false, "Xray").Should().BeEqualTo(@"D:\Glide\bin\xray");
        await GlidePaths.GetBinFolder(@"D:\Glide", @"D:\Glide", false, null).Should().BeEqualTo(@"D:\Glide\bin");
    }
}
