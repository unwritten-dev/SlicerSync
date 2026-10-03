using SlicerSync.Core.Config;
using SlicerSync.Core.History;
using SlicerSync.Core.Sync;

namespace SlicerSync.Core.Tests;

public sealed class SyncTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private (TestPc A, TestPc B) TwoPcs()
    {
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        a.NewSetup();
        var b = _env.CreatePc("B");
        b.Setup.RunCloneSetup(b.Settings);
        return (a, b);
    }

    [Fact]
    public void Sync_WithoutChanges_DoesNotCommit()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        var head = pc.Repo.HeadHash();

        var result = pc.Sync.Sync();

        Assert.Equal(SyncOutcome.NoChanges, result.Outcome);
        Assert.Equal(head, pc.Repo.HeadHash());
    }

    [Fact]
    public void Sync_ChangeThroughJunction_IsCommittedAndPushed_WithTemplateMessage()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        pc.Settings.CommitMessageTemplate = "Test {rechner} {anzahl}";

        // Der Slicer schreibt in den Originalordner (= Junction).
        pc.WriteFile(pc.OrcaDir("default", "filament"), "PETG.json", "{}");
        pc.WriteFile(pc.PrusaDir("print"), "Fein.ini", "layer_height = 0.12");

        var result = pc.Sync.Sync();

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.Equal(2, result.FilesCommitted);
        Assert.True(result.Pushed);
        var log = _env.CreateRunner().RunChecked(_env.RemotePath, "log", "-1", "--format=%s", "main").StdOut.Trim();
        Assert.Equal($"Test {Environment.MachineName} 2", log);
    }

    [Fact]
    public void Sync_TransfersChangesBetweenPcs()
    {
        var (a, b) = TwoPcs();

        a.WriteFile(a.PrusaDir("filament"), "ASA.ini", "temperature = 260");
        Assert.Equal(SyncOutcome.Success, a.Sync.Sync().Outcome);

        var result = b.Sync.Sync();

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.True(result.PulledChanges);
        Assert.Equal("temperature = 260", File.ReadAllText(Path.Combine(b.PrusaDir("filament"), "ASA.ini")));
    }

    [Fact]
    public void Sync_BothPcsChangeDifferentFiles_RebaseMergesCleanly()
    {
        var (a, b) = TwoPcs();
        a.WriteFile(a.PrusaDir("filament"), "ASA.ini", "a");
        a.Sync.Sync();
        b.WriteFile(b.PrusaDir("print"), "Neu.ini", "b");

        var result = b.Sync.Sync();

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.True(result.Pushed);
        Assert.True(File.Exists(Path.Combine(b.PrusaDir("filament"), "ASA.ini")));
        Assert.Equal(SyncOutcome.Success, a.Sync.Sync().Outcome);
        Assert.True(File.Exists(Path.Combine(a.PrusaDir("print"), "Neu.ini")));
    }

    [Fact]
    public void Sync_Conflict_AbortsRebase_AndKeepsLocalVersion()
    {
        var (a, b) = TwoPcs();
        a.WriteFile(a.PrusaDir("print"), "Fein.ini", "layer_height = 0.11 (A)");
        a.Sync.Sync();
        b.WriteFile(b.PrusaDir("print"), "Fein.ini", "layer_height = 0.13 (B)");

        var result = b.Sync.Sync();

        Assert.Equal(SyncOutcome.Conflict, result.Outcome);
        Assert.False(b.Repo.IsRebaseInProgress());
        Assert.Equal("layer_height = 0.13 (B)", File.ReadAllText(Path.Combine(b.PrusaDir("print"), "Fein.ini")));
        // Der lokale Commit existiert weiterhin.
        Assert.Contains("1", b.Repo.CommitsAhead("main")?.ToString() ?? "");
        // Remote unverändert (Stand von A).
        var remote = _env.CreateRunner().RunChecked(_env.RemotePath, "show", "main:Prusa/print/Fein.ini").StdOut;
        Assert.Equal("layer_height = 0.11 (A)", remote);
    }

    [Fact]
    public void Sync_WithoutRemote_CommitsLocally()
    {
        var pc = _env.CreatePc("A");
        pc.Settings.RemoteUrl = "";
        pc.CreateSampleProfiles();
        pc.NewSetup();
        pc.WriteFile(pc.PrusaDir("print"), "X.ini", "x");

        var result = pc.Sync.Sync();

        Assert.Equal(SyncOutcome.Success, result.Outcome);
        Assert.False(result.Pushed);
    }

    [Fact]
    public void Sync_ReportsBrokenJunction()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        // Simuliert ein Slicer-Update, das den Ordner neu anlegt.
        Directory.Delete(pc.PrusaDir("print"), false);
        pc.WriteFile(pc.PrusaDir("print"), "Neu.ini", "x");

        var result = pc.Sync.Sync();

        Assert.True(result.IsProblem);
        Assert.Contains(result.Warnings, w => w.Contains("print"));
    }

    [Fact]
    public void Sync_ConcurrentCall_ReturnsBusy()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        SyncResult? inner = null;
        pc.Sync.Started += () => inner ??= pc.Sync.Sync();

        pc.Sync.Sync();

        Assert.Equal(SyncOutcome.Busy, inner!.Outcome);
    }

    [Fact]
    public void History_ListsCommits_AndRestoresOldFileVersion_WithBackup()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        var file = Path.Combine(pc.PrusaDir("print"), "Fein.ini");
        File.WriteAllText(file, "layer_height = 0.3");
        pc.Sync.Sync();

        var history = new HistoryService(() => pc.Settings, pc.Git, pc.Backups, pc.Log);
        var commits = history.GetCommits();
        Assert.Equal(2, commits.Count);
        Assert.Contains(commits[0].Files, f => f.Path == "Prusa/print/Fein.ini" && f.Status == "M");
        var initial = commits[1];
        Assert.Contains(initial.Files, f => f.Path == "Orca/default/filament/PLA Fein.json" && f.Status == "A");

        var backup = history.RestoreFile(initial.Hash, "Prusa/print/Fein.ini");

        Assert.Equal("layer_height = 0.1", File.ReadAllText(file));
        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void History_RestoreDeletedFile_UsesStateBeforeDeletion()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        var file = Path.Combine(pc.PrusaDir("printer"), "MK4.ini");
        File.Delete(file);
        pc.Sync.Sync();
        var history = new HistoryService(() => pc.Settings, pc.Git, pc.Backups, pc.Log);
        var deletion = history.GetCommits()[0];
        Assert.Contains(deletion.Files, f => f.Path == "Prusa/printer/MK4.ini" && f.Status == "D");

        var backup = history.RestoreFile(deletion.Hash, "Prusa/printer/MK4.ini");

        Assert.Null(backup);
        Assert.Equal("bed_shape = 0x0", File.ReadAllText(file));
    }
}
