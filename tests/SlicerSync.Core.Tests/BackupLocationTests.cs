using SlicerSync.Core.Sync;

namespace SlicerSync.Core.Tests;

public sealed class BackupLocationTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private string Location(string name)
    {
        var dir = Path.Combine(_env.Root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string HeadOf(string gitDir) =>
        _env.CreateRunner().RunChecked(null, "--git-dir", gitDir, "rev-parse", "main").StdOut.Trim();

    [Fact]
    public void Sync_CopiesCompleteHistoryToEveryLocation()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        var nas = Location("nas");
        var usb = Location("usb");
        pc.Settings.BackupLocations = new List<string> { nas, usb };
        pc.WriteFile(pc.PrusaDir("print"), "New.ini", "x");

        var result = pc.Sync.Sync();

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.Empty(result.Notes);
        foreach (var location in new[] { nas, usb })
        {
            var mirror = BackupLocations.RepositoryPath(location, Environment.MachineName);
            Assert.Equal(pc.Repo.HeadHash(), HeadOf(mirror));
        }
        // Complete history: the initial setup commit is in there too.
        var log = _env.CreateRunner().RunChecked(null, "--git-dir", BackupLocations.RepositoryPath(nas, Environment.MachineName), "log", "--oneline", "main").StdOut;
        Assert.Contains("Initial setup", log);
    }

    [Fact]
    public void UnreachableLocation_IsANote_NotAnError()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        pc.Settings.BackupLocations = new List<string> { Path.Combine(_env.Root, "usb-unplugged") };
        pc.WriteFile(pc.PrusaDir("print"), "New.ini", "x");

        var result = pc.Sync.Sync();

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.False(result.IsProblem);
        Assert.Single(result.Notes);
        Assert.Contains("usb-unplugged", result.Notes[0]);
    }

    [Fact]
    public void RemoteOffline_LocationsAreStillUpdated()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        var nas = Location("nas");
        pc.Settings.BackupLocations = new List<string> { nas };
        pc.Settings.RemoteUrl = Path.Combine(_env.Root, "remote-gone.git");
        pc.WriteFile(pc.PrusaDir("print"), "Offline.ini", "x");

        var result = pc.Sync.Sync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(pc.Repo.HeadHash(), HeadOf(BackupLocations.RepositoryPath(nas, Environment.MachineName)));
    }

    [Fact]
    public void Location_StaysIdenticalAfterRebaseOfLocalCommits()
    {
        // Offline commit goes to the location first; after reconnecting the commit is rebased (new hash).
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        a.NewSetup();
        var b = _env.CreatePc("B");
        b.Setup.RunCloneSetup(b.Settings);
        var nas = Location("nas");
        a.Settings.BackupLocations = new List<string> { nas };

        var remote = a.Settings.RemoteUrl;
        a.Settings.RemoteUrl = Path.Combine(_env.Root, "offline.git");
        a.WriteFile(a.PrusaDir("print"), "A.ini", "a");
        a.Sync.Sync();                                   // offline: only the location gets it
        b.WriteFile(b.PrusaDir("filament"), "B.ini", "b");
        b.Sync.Sync();                                   // other PC pushes meanwhile
        a.Settings.RemoteUrl = remote;

        var result = a.Sync.Sync();                      // online again: rebase rewrites A's commit

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.Empty(result.Notes);
        Assert.Equal(a.Repo.HeadHash(), HeadOf(BackupLocations.RepositoryPath(nas, Environment.MachineName)));
    }

    [Fact]
    public void Location_CanBeUsedToRestoreOnANewPc()
    {
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        a.NewSetup();
        var usb = Location("usb");
        a.Settings.BackupLocations = new List<string> { usb };
        a.Sync.Sync();

        var restored = _env.CreatePc("New");
        restored.Settings.RemoteUrl = BackupLocations.RepositoryPath(usb, Environment.MachineName);
        restored.Setup.RunCloneSetup(restored.Settings);

        Assert.Equal("layer_height = 0.1", File.ReadAllText(Path.Combine(restored.PrusaDir("print"), "Fein.ini")));
    }

    [Fact]
    public void Validate_RejectsRepositoryFolderAndMissingFolders()
    {
        var pc = _env.CreatePc("A");
        var repo = pc.Settings.RepoPath;
        Directory.CreateDirectory(Path.Combine(repo, "sub"));

        Assert.Null(BackupLocations.Validate(Location("nas"), repo));
        Assert.NotNull(BackupLocations.Validate(Path.Combine(repo, "sub"), repo));
        Assert.NotNull(BackupLocations.Validate(pc.Root, repo));
        Assert.NotNull(BackupLocations.Validate(Path.Combine(_env.Root, "missing"), repo));
        Assert.NotNull(BackupLocations.Validate("relative", repo));
    }
}
