namespace ServiceLib.Tests.Handler;

public class ConfigImportHandlerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"glide-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateInstall(string root)
    {
        var configDir = Path.Combine(root, "guiConfigs");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, Global.ConfigFileName), "{}");
        return configDir;
    }

    [Test]
    public async Task GetConfigDirFromFolder_InstallFolder_ReturnsItsGuiConfigs()
    {
        var root = NewTempDir();
        try
        {
            var configDir = CreateInstall(root);

            var result = ConfigImportHandler.GetConfigDirFromFolder(root);

            await result.Should().BeEqualTo(configDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task GetConfigDirFromFolder_GuiConfigsFolderItself_ReturnsIt()
    {
        var root = NewTempDir();
        try
        {
            var configDir = CreateInstall(root);

            var result = ConfigImportHandler.GetConfigDirFromFolder(configDir + Path.DirectorySeparatorChar);

            await result.Should().BeEqualTo(configDir);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task GetConfigDirFromFolder_FolderWithoutSettings_ReturnsNull()
    {
        var root = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "guiConfigs"));

            var result = ConfigImportHandler.GetConfigDirFromFolder(root);

            await (result is null).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task GetConfigDirFromFolder_MissingOrEmptyPath_ReturnsNull()
    {
        await (ConfigImportHandler.GetConfigDirFromFolder(null) is null).Should().BeTrue();
        await (ConfigImportHandler.GetConfigDirFromFolder(string.Empty) is null).Should().BeTrue();
        await (ConfigImportHandler.GetConfigDirFromFolder(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}")) is null).Should().BeTrue();
    }

    [Test]
    public async Task CopyDataOnly_SkipsRunnableFiles_AndKeepsData_InNestedFolders()
    {
        var src = NewTempDir();
        var dst = Path.Combine(NewTempDir(), "out");
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            foreach (var name in new[] { "a.json", "a.db", "geosite.dat", "sub\\list.srs", "note.txt", "no-extension" })
            {
                File.WriteAllText(Path.Combine(src, name), "data");
            }
            foreach (var name in new[] { "x.exe", "x.DLL", "x.ps1", "x.bat", "x.lnk", "sub\\y.exe", "sub\\y.vbs" })
            {
                File.WriteAllText(Path.Combine(src, name), "MZ");
            }

            ConfigImportHandler.CopyDataOnly(src, dst);

            foreach (var name in new[] { "a.json", "a.db", "geosite.dat", "sub\\list.srs", "note.txt", "no-extension" })
            {
                await File.Exists(Path.Combine(dst, name)).Should().BeTrue();
            }
            foreach (var name in new[] { "x.exe", "x.DLL", "x.ps1", "x.bat", "x.lnk", "sub\\y.exe", "sub\\y.vbs" })
            {
                await File.Exists(Path.Combine(dst, name)).Should().BeFalse();
            }
        }
        finally
        {
            Directory.Delete(src, true);
            Directory.Delete(Path.GetDirectoryName(dst)!, true);
        }
    }

    [Test]
    public async Task CopyDataOnly_DoesNotFollowAJunction()
    {
        var src = NewTempDir();
        var outside = NewTempDir();
        var dst = Path.Combine(NewTempDir(), "out");
        var junction = Path.Combine(src, "link");
        try
        {
            File.WriteAllText(Path.Combine(src, "a.json"), "data");
            File.WriteAllText(Path.Combine(outside, "secret.json"), "elsewhere");
            using (var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
            {
                mk!.WaitForExit();
            }
            if (!Directory.Exists(junction))
            {
                return; // Not a Windows host, or links are not allowed here.
            }

            ConfigImportHandler.CopyDataOnly(src, dst);

            await File.Exists(Path.Combine(dst, "a.json")).Should().BeTrue();
            await Directory.Exists(Path.Combine(dst, "link")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction); // removes the link only
            }
            Directory.Delete(src, true);
            Directory.Delete(outside, true);
            Directory.Delete(Path.GetDirectoryName(dst)!, true);
        }
    }

    [Test]
    [NotInParallel("stage-import")]
    public async Task StageImport_CopiesSettingsButNeverCoresOrPrograms()
    {
        var root = NewTempDir();
        var staging = Path.Combine(Utils.StartupPath(), "guiConfigs.import");
        try
        {
            var configDir = CreateInstall(root);
            File.WriteAllText(Path.Combine(configDir, "custom.json"), "{}");
            File.WriteAllText(Path.Combine(configDir, "planted.exe"), "MZ");
            // The other install's cores sit next to guiConfigs, as in a real v2rayN folder.
            var coreDir = Path.Combine(root, "bin", "xray");
            Directory.CreateDirectory(coreDir);
            File.WriteAllText(Path.Combine(coreDir, "xray.exe"), "MZ");
            File.WriteAllText(Path.Combine(root, "v2rayN.exe"), "MZ");

            var staged = ConfigImportHandler.StageImport(configDir);

            await staged.Should().BeTrue();
            await File.Exists(Path.Combine(staging, Global.ConfigFileName)).Should().BeTrue();
            await File.Exists(Path.Combine(staging, "custom.json")).Should().BeTrue();
            await File.Exists(Path.Combine(staging, "planted.exe")).Should().BeFalse();
            // No core was brought over into this installation's bin.
            await File.Exists(Path.Combine(Utils.GetBinPath(""), "xray", Utils.GetExeName("xray"))).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    [Test]
    [NotInParallel("stage-import")]
    public async Task StageImport_SourceWithoutSettings_FailsWithoutLeavingStagedFiles()
    {
        var root = NewTempDir();
        try
        {
            var staging = Path.Combine(Utils.StartupPath(), "guiConfigs.import");

            var staged = ConfigImportHandler.StageImport(root);

            await staged.Should().BeFalse();
            await Directory.Exists(staging).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
