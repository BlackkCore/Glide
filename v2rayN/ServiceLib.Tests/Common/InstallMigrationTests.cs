namespace ServiceLib.Tests.Common;

public class InstallMigrationTests
{
    private static string NewDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-mig-{Guid.NewGuid():N}")).FullName;

    private static void Put(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Test]
    public async Task Run_CopiesOldSettingsAndDataButNoExecutables_DeletesNothing()
    {
        var program = NewDir();
        var data = Path.Combine(NewDir(), "Glide");
        var old = NewDir();
        try
        {
            Put(old, @"guiConfigs\guiNConfig.json", "old-config");
            Put(old, @"guiConfigs\glide-favorites.json", "[\"a\"]");
            Put(old, @"bin\geosite.dat", "old-geo-newer");
            Put(old, @"bin\xray\xray.exe", "planted");
            Put(program, @"bin\geosite.dat", "shipped-geo");
            Put(program, @"bin\geoip.dat", "shipped-geoip");
            Put(program, @"bin\xray\xray.exe", "real-core");

            InstallMigration.Run(program, data, old);

            await File.ReadAllText(Path.Combine(data, @"guiConfigs\guiNConfig.json")).Should().BeEqualTo("old-config");
            await File.ReadAllText(Path.Combine(data, @"guiConfigs\glide-favorites.json")).Should().BeEqualTo("[\"a\"]");
            // The user's updated geo file wins over the shipped one; missing ones are seeded.
            await File.ReadAllText(Path.Combine(data, @"bin\geosite.dat")).Should().BeEqualTo("old-geo-newer");
            await File.ReadAllText(Path.Combine(data, @"bin\geoip.dat")).Should().BeEqualTo("shipped-geoip");
            // Never an executable in the data folder.
            await File.Exists(Path.Combine(data, @"bin\xray\xray.exe")).Should().BeFalse();
            // The old install is left as it was.
            await File.Exists(Path.Combine(old, @"guiConfigs\guiNConfig.json")).Should().BeTrue();
            await File.Exists(Path.Combine(old, @"bin\xray\xray.exe")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(program, true);
            Directory.Delete(Path.GetDirectoryName(data)!, true);
            Directory.Delete(old, true);
        }
    }

    [Test]
    public async Task Run_FailsPartWay_LeavesNoHalfCopiedSettings_AndRetriesNextStart()
    {
        var program = NewDir();
        var data = Path.Combine(NewDir(), "Glide");
        var old = NewDir();
        try
        {
            Put(old, @"guiConfigs\a-first.json", "a");
            Put(old, @"guiConfigs\z-locked.db", "db");
            Put(old, @"guiBackups\b.zip", "backup");

            // The running old Glide holds a file open without allowing reads.
            var locked = new FileStream(Path.Combine(old, @"guiConfigs\z-locked.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var threw = false;
            try
            {
                InstallMigration.Run(program, data, old);
            }
            catch (IOException)
            {
                threw = true;
            }
            finally
            {
                locked.Dispose();
            }
            await threw.Should().BeTrue();

            // Nothing half-copied, and the failure is remembered so the next start tries again.
            await Directory.Exists(Path.Combine(data, "guiConfigs")).Should().BeFalse();
            await Directory.Exists(Path.Combine(data, "guiConfigs.migrating")).Should().BeFalse();
            await File.Exists(Path.Combine(data, InstallMigration.MigratedMarker)).Should().BeFalse();
            await File.Exists(Path.Combine(data, InstallMigration.PendingMarker)).Should().BeTrue();

            // Meanwhile the app started with no migrated settings and created its own (as it really does).
            Put(data, @"guiConfigs\guiNConfig.json", "fresh-default");

            var copied = InstallMigration.Run(program, data, old);

            await copied.Should().BeTrue();
            await File.ReadAllText(Path.Combine(data, @"guiConfigs\a-first.json")).Should().BeEqualTo("a");
            await File.ReadAllText(Path.Combine(data, @"guiConfigs\z-locked.db")).Should().BeEqualTo("db");
            await File.ReadAllText(Path.Combine(data, @"guiBackups\b.zip")).Should().BeEqualTo("backup");
            await File.Exists(Path.Combine(data, InstallMigration.PendingMarker)).Should().BeFalse();
            // What the app had created is kept aside, not deleted.
            var aside = Directory.GetDirectories(data, "guiConfigs.backup-*");
            await aside.Length.Should().BeEqualTo(1);
            await File.ReadAllText(Path.Combine(aside[0], "guiNConfig.json")).Should().BeEqualTo("fresh-default");
        }
        finally
        {
            Directory.Delete(program, true);
            Directory.Delete(Path.GetDirectoryName(data)!, true);
            Directory.Delete(old, true);
        }
    }

    [Test]
    public async Task Run_FailsTwice_GivesUp_AndPutsTheCurrentSettingsBack()
    {
        var program = NewDir();
        var data = Path.Combine(NewDir(), "Glide");
        var old = NewDir();
        try
        {
            Put(old, @"guiConfigs\z-locked.db", "db");
            var locked = new FileStream(Path.Combine(old, @"guiConfigs\z-locked.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            try
            {
                var threw = false;
                try
                {
                    InstallMigration.Run(program, data, old);
                }
                catch (IOException)
                {
                    threw = true;
                }
                await threw.Should().BeTrue();
                Put(data, @"guiConfigs\guiNConfig.json", "fresh-default");

                // Still locked: the retry fails too and must neither throw nor lose the app's settings.
                var copied = InstallMigration.Run(program, data, old);

                await copied.Should().BeFalse();
            }
            finally
            {
                locked.Dispose();
            }
            await File.ReadAllText(Path.Combine(data, @"guiConfigs\guiNConfig.json")).Should().BeEqualTo("fresh-default");
            await File.Exists(Path.Combine(data, InstallMigration.MigratedMarker)).Should().BeTrue();
            await File.Exists(Path.Combine(data, InstallMigration.PendingMarker)).Should().BeFalse();
            await Directory.GetDirectories(data, "guiConfigs.backup-*").Length.Should().BeEqualTo(0);
        }
        finally
        {
            Directory.Delete(program, true);
            Directory.Delete(Path.GetDirectoryName(data)!, true);
            Directory.Delete(old, true);
        }
    }

    [Test]
    public async Task Run_Twice_NeverOverwritesNewerSettings()
    {
        var program = NewDir();
        var data = Path.Combine(NewDir(), "Glide");
        var old = NewDir();
        try
        {
            Put(old, @"guiConfigs\guiNConfig.json", "old-config");
            InstallMigration.Run(program, data, old);
            File.WriteAllText(Path.Combine(data, @"guiConfigs\guiNConfig.json"), "changed-since");
            Put(old, @"guiConfigs\later.json", "x");

            InstallMigration.Run(program, data, old);

            await File.ReadAllText(Path.Combine(data, @"guiConfigs\guiNConfig.json")).Should().BeEqualTo("changed-since");
            await File.Exists(Path.Combine(data, @"guiConfigs\later.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(program, true);
            Directory.Delete(Path.GetDirectoryName(data)!, true);
            Directory.Delete(old, true);
        }
    }

    [Test]
    public async Task Run_ExistingNewSettings_AreNotMixedWithOldOnes()
    {
        var program = NewDir();
        var data = Path.Combine(NewDir(), "Glide");
        var old = NewDir();
        try
        {
            Put(data, @"guiConfigs\guiNConfig.json", "new-config");
            Put(old, @"guiConfigs\guiNConfig.json", "old-config");
            Put(old, @"guiConfigs\glide-favorites.json", "[\"a\"]");

            InstallMigration.Run(program, data, old);

            await File.ReadAllText(Path.Combine(data, @"guiConfigs\guiNConfig.json")).Should().BeEqualTo("new-config");
            await File.Exists(Path.Combine(data, @"guiConfigs\glide-favorites.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(program, true);
            Directory.Delete(Path.GetDirectoryName(data)!, true);
            Directory.Delete(old, true);
        }
    }
}
