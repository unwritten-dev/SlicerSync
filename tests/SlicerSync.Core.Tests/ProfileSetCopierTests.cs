using System.IO.Compression;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;
using SlicerSync.Core.Sync;

namespace SlicerSync.Core.Tests;

public sealed class ProfileSetCopierTests : IDisposable
{
    private const string Account = "bd29f8e8-0297-4b33-ab29-d78b0bd07306";

    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    /// <summary>Like the real situation: a full account folder and an older, smaller "default".</summary>
    private TestPc CreatePcWithAccountAndDefault()
    {
        var pc = _env.CreatePc("A");
        var info = "sync_info = \r\nuser_id = " + Account + "\r\nsetting_id = a0eb5314-3382-5eae\r\nbase_id = GM003\r\nupdated_time = 1789532755\r\n";
        pc.WriteFile(pc.OrcaDir(Account, "machine"), "Custom - Voron.json", "{\"new\":true}");
        pc.WriteFile(pc.OrcaDir(Account, "machine"), "Custom - Voron.info", info);
        pc.WriteFile(pc.OrcaDir(Account, "machine"), "Custom - X1C.json", "{\"x1c\":true}");
        pc.WriteFile(pc.OrcaDir(Account, "machine"), "Custom - X1C.info", info);
        pc.WriteFile(Path.Combine(pc.OrcaDir(Account, "filament"), "base"), "ASA.json", "{}");
        pc.WriteFile(pc.OrcaDir(Account, "process"), "Same.json", "same");
        pc.WriteFile(pc.OrcaDir("default", "machine"), "Custom - Voron.json", "{\"new\":false}");
        pc.WriteFile(pc.OrcaDir("default", "process"), "Same.json", "same");
        pc.WriteFile(pc.OrcaDir("default", "filament"), "Only default.json", "{}");
        pc.NewSetup();
        return pc;
    }

    private ProfileSetCopier Copier(TestPc pc) => new(pc.Backups, pc.Guard, pc.Log);

    [Fact]
    public void AccountSets_ListsAccountsWithFiles_ButNotDefault()
    {
        var pc = CreatePcWithAccountAndDefault();
        Directory.CreateDirectory(Path.Combine(pc.Settings.RepoPath, "Orca", "4288834837", "machine"));

        var sets = ProfileSetCopier.AccountSets(pc.Settings.RepoPath, SlicerCatalog.Orca);

        var set = Assert.Single(sets);
        Assert.Equal(Account, set.Name);
        Assert.Equal(6, set.FileCount);
    }

    [Fact]
    public void Copy_AddsAndUpdates_RemovesAccountIds_AndBacksUpReplacedFiles()
    {
        var pc = CreatePcWithAccountAndDefault();

        var result = Copier(pc).CopyToNoSignIn(pc.Settings, SlicerCatalog.Orca, Account);

        Assert.Equal(4, result.Added);      // Voron.info, X1C.json, X1C.info, base\ASA.json
        Assert.Equal(1, result.Updated);    // Voron.json (older version in default)
        Assert.Equal(1, result.Unchanged);  // Same.json

        // Visible locally through the junction of "default".
        var machine = pc.OrcaDir("default", "machine");
        Assert.Equal("{\"new\":true}", File.ReadAllText(Path.Combine(machine, "Custom - Voron.json")));
        Assert.Equal("{\"x1c\":true}", File.ReadAllText(Path.Combine(machine, "Custom - X1C.json")));
        Assert.True(File.Exists(Path.Combine(pc.OrcaDir("default", "filament"), "base", "ASA.json")));
        Assert.True(File.Exists(Path.Combine(pc.OrcaDir("default", "filament"), "Only default.json")));

        // .info like the slicer writes it without sign-in.
        Assert.Equal(
            "sync_info = update\r\nuser_id = \r\nsetting_id = \r\nbase_id = GM003\r\nupdated_time = 1789532755\r\n",
            File.ReadAllText(Path.Combine(machine, "Custom - Voron.info")));

        // Account folder unchanged.
        Assert.Contains(Account, File.ReadAllText(Path.Combine(pc.OrcaDir(Account, "machine"), "Custom - Voron.info")));

        // The replaced version is in the backup.
        Assert.NotNull(result.BackupZip);
        using var zip = ZipFile.OpenRead(result.BackupZip!);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("Orca/default/machine/Custom - Voron.json", entry.FullName.Replace('\\', '/'));
    }

    [Fact]
    public void Copy_IsCommittedAndReachesSecondPc()
    {
        var a = CreatePcWithAccountAndDefault();
        var b = _env.CreatePc("B");
        b.Setup.RunCloneSetup(b.Settings);

        Copier(a).CopyToNoSignIn(a.Settings, SlicerCatalog.Orca, Account);
        Assert.Equal(SyncOutcome.Success, a.Sync.Sync().Outcome);
        b.Sync.Sync();

        Assert.True(File.Exists(Path.Combine(b.OrcaDir("default", "machine"), "Custom - X1C.json")));
    }

    [Fact]
    public void Copy_SecondRun_ChangesNothing()
    {
        var pc = CreatePcWithAccountAndDefault();
        Copier(pc).CopyToNoSignIn(pc.Settings, SlicerCatalog.Orca, Account);

        var again = Copier(pc).CopyToNoSignIn(pc.Settings, SlicerCatalog.Orca, Account);

        Assert.Equal(0, again.Added + again.Updated);
        Assert.Null(again.BackupZip);
    }

    [Fact]
    public void Copy_RefusesWhileSlicerRuns_OrWhenDefaultIsNotBackedUp()
    {
        var pc = CreatePcWithAccountAndDefault();
        pc.RunningProcesses.Add("orca-slicer");
        Assert.Throws<SlicerRunningException>(() => Copier(pc).CopyToNoSignIn(pc.Settings, SlicerCatalog.Orca, Account));
        pc.RunningProcesses.Clear();

        var defaultMachine = pc.Settings.Mappings.Single(m => m.ProfileSet == "default" && m.Folder == "machine");
        pc.Setup.RemoveMappings(pc.Settings, new[] { defaultMachine });
        var ex = Assert.Throws<SetupException>(() => Copier(pc).CopyToNoSignIn(pc.Settings, SlicerCatalog.Orca, Account));
        Assert.Contains("machine", ex.Message);
    }

    [Fact]
    public void WithoutAccount_KeepsUnknownLinesAndOrder()
    {
        var text = System.Text.Encoding.UTF8.GetString(ProfileSetCopier.WithoutAccount(
            "sync_info = \nuser_id = 123\nsetting_id = abc\nbase_id = GP004\nupdated_time = 1\nextra = x\n"));
        Assert.Equal("sync_info = update\r\nuser_id = \r\nsetting_id = \r\nbase_id = GP004\r\nupdated_time = 1\r\nextra = x\r\n", text);
    }
}
