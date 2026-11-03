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
