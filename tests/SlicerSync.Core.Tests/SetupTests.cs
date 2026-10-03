using System.IO.Compression;
using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;

namespace SlicerSync.Core.Tests;

public sealed class SetupTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void Detector_FindsOrcaAndPrusaFolders_ButNotPrusaCache()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();

        var candidates = pc.Detector.Detect().Candidates;

        Assert.Equal(
            new[] { @"Orca\default\filament", @"Orca\default\machine", @"Orca\default\process", @"Prusa\print", @"Prusa\filament", @"Prusa\printer" },
            candidates.Select(c => c.RepoRelativePath));
        Assert.DoesNotContain(candidates, c => c.Folder == "cache");
    }

    [Fact]
    public void NewSetup_MovesFolders_CreatesJunctions_BacksUp_CommitsAndPushes()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        var originalFile = Path.Combine(pc.OrcaDir("default", "filament"), "PLA Fein.json");

        var result = pc.NewSetup();

        // Junctions zeigen auf den Repository-Ordner, Dateien sind weiterhin am Originalort lesbar.
        Assert.True(Junction.PointsTo(pc.OrcaDir("default", "filament"), Path.Combine(pc.Settings.RepoPath, @"Orca\default\filament")));
        Assert.True(Junction.PointsTo(pc.PrusaDir("print"), Path.Combine(pc.Settings.RepoPath, @"Prusa\print")));
        Assert.Equal("{\"name\":\"PLA Fein\"}", File.ReadAllText(originalFile));
        Assert.True(File.Exists(Path.Combine(pc.Settings.RepoPath, @"Orca\default\filament\PLA Fein.json")));

        // Nicht gesicherte Prusa-Bestandteile bleiben unberührt.
        Assert.False(Junction.IsJunction(Path.Combine(pc.Paths.RoamingAppData, "PrusaSlicer")));
        Assert.True(File.Exists(Path.Combine(pc.Paths.RoamingAppData, "PrusaSlicer", "PrusaSlicer.ini")));

        // Backup enthält alle Originaldateien.
        Assert.NotNull(result.BackupZip);
        using (var zip = ZipFile.OpenRead(result.BackupZip!))
            Assert.Contains(zip.Entries, e => e.FullName == "original/Orca/default/filament/PLA Fein.json");

        Assert.True(result.Pushed);
        Assert.True(pc.Settings.SetupCompleted);
        Assert.Equal(6, pc.Settings.Mappings.Count);
        Assert.True(File.Exists(Path.Combine(pc.Settings.RepoPath, RepoManifest.FileName)));

        var remoteLog = _env.CreateRunner().RunChecked(_env.RemotePath, "log", "--oneline", "main");
        Assert.Contains("Ersteinrichtung", remoteLog.StdOut);
    }

    [Fact]
    public void NewSetup_RunTwice_SkipsExistingJunctions()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();

        var second = pc.NewSetup();

        Assert.Equal(0, second.LinkedFolders);
        Assert.Equal(6, second.SkippedFolders);
        Assert.Null(second.BackupZip);
        Assert.Equal(6, pc.Settings.Mappings.Count);
    }

    [Fact]
    public void NewSetup_RefusesWhileSlicerRuns_AndChangesNothing()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.RunningProcesses.Add("orca-slicer");

        var ex = Assert.Throws<SlicerRunningException>(() => pc.NewSetup());

        Assert.Contains("Orca Slicer", ex.Message);
        Assert.False(Junction.IsJunction(pc.OrcaDir("default", "filament")));
        Assert.False(Directory.Exists(pc.Settings.RepoPath));
    }

    [Fact]
    public void NewSetup_RefusesNonEmptyRemote()
    {
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        a.NewSetup();

        var b = _env.CreatePc("B");
        b.CreateSampleProfiles();
        Assert.Throws<SetupException>(() => b.NewSetup());
        Assert.False(Junction.IsJunction(b.OrcaDir("default", "filament")));
    }

    [Fact]
    public void NewSetup_WithForeignJunction_Fails()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        var elsewhere = Path.Combine(pc.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.Delete(pc.PrusaDir("print"), true);
        Junction.Create(pc.PrusaDir("print"), elsewhere);

        var candidates = pc.Detector.Detect().Candidates;
        Assert.Throws<SetupException>(() => pc.Setup.RunNewSetup(pc.Settings, candidates));
    }

    [Fact]
    public void CloneSetup_BacksUpLocalProfiles_AndLinksToRepository()
    {
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        a.NewSetup();

        var b = _env.CreatePc("B");
        b.WriteFile(b.OrcaDir("default", "filament"), "Nur lokal.json", "{\"lokal\":true}");

        var result = b.Setup.RunCloneSetup(b.Settings);

        Assert.True(Junction.PointsTo(b.OrcaDir("default", "filament"), Path.Combine(b.Settings.RepoPath, @"Orca\default\filament")));
        Assert.True(File.Exists(Path.Combine(b.OrcaDir("default", "filament"), "PLA Fein.json")));
        Assert.True(File.Exists(Path.Combine(b.PrusaDir("printer"), "MK4.ini")));
        Assert.False(File.Exists(Path.Combine(b.OrcaDir("default", "filament"), "Nur lokal.json")));

        Assert.NotNull(result.BackupZip);
        using (var zip = ZipFile.OpenRead(result.BackupZip!))
            Assert.Contains(zip.Entries, e => e.FullName == "lokal/Orca/default/filament/Nur lokal.json");

        Assert.Equal(6, b.Settings.Mappings.Count);
        Assert.True(b.Settings.SetupCompleted);
    }

    [Fact]
    public void CloneSetup_EmptyRemote_FailsWithHint()
    {
        var b = _env.CreatePc("B");
        var ex = Assert.Throws<SetupException>(() => b.Setup.RunCloneSetup(b.Settings));
        Assert.Contains("keine Slicer-Profile", ex.Message);
    }

    [Fact]
    public void Uninstall_RemovesJunctions_AndRestoresFolders_RepositoryStays()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();

        var count = pc.Setup.Uninstall(pc.Settings);

        Assert.Equal(6, count);
        var folder = pc.OrcaDir("default", "filament");
        Assert.False(Junction.IsJunction(folder));
        Assert.True(File.Exists(Path.Combine(folder, "PLA Fein.json")));
        Assert.True(File.Exists(Path.Combine(pc.Settings.RepoPath, @"Orca\default\filament\PLA Fein.json")));
        Assert.False(pc.Settings.SetupCompleted);
    }

    [Fact]
    public void Uninstall_ThenNewSetupAgain_Works()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup();
        pc.Setup.Uninstall(pc.Settings);
        File.WriteAllText(Path.Combine(pc.PrusaDir("print"), "Fein.ini"), "layer_height = 0.15");

        var result = pc.NewSetup();

        Assert.Equal(6, result.LinkedFolders);
        Assert.Equal("layer_height = 0.15", File.ReadAllText(Path.Combine(pc.Settings.RepoPath, @"Prusa\print\Fein.ini")));
        using var zip = ZipFile.OpenRead(result.BackupZip!);
        Assert.Contains(zip.Entries, e => e.FullName == "repository/Prusa/print/Fein.ini");
    }

    [Fact]
    public void AddAndRemoveMapping_LaterInSettings()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.NewSetup(m => m.Slicer == SlicerKind.Orca);
        Assert.False(Junction.IsJunction(pc.PrusaDir("print")));

        var prusaPrint = pc.Detector.CreateMapping(SlicerKind.Prusa, "", "print");
        pc.Setup.AddMappings(pc.Settings, new[] { prusaPrint });
        Assert.True(Junction.IsJunction(pc.PrusaDir("print")));
        Assert.Contains(RepoManifest.Read(pc.Settings.RepoPath)!.Folders, f => f.Folder == "print");

        pc.Setup.RemoveMappings(pc.Settings, new[] { prusaPrint });
        Assert.False(Junction.IsJunction(pc.PrusaDir("print")));
        Assert.True(File.Exists(Path.Combine(pc.PrusaDir("print"), "Fein.ini")));
        Assert.False(pc.Settings.Mappings.Single(m => m.SameFolderAs(prusaPrint)).Enabled);
    }

    [Fact]
    public void Junction_RemoveKeepsTargetContent()
    {
        var pc = _env.CreatePc("A");
        var target = Path.Combine(pc.Root, "target");
        pc.WriteFile(target, "x.txt", "x");
        var link = Path.Combine(pc.Root, "link");

        Junction.Create(link, target);
        Assert.True(Junction.PointsTo(link, target));
        Assert.True(File.Exists(Path.Combine(link, "x.txt")));

        Junction.Remove(link);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "x.txt")));
    }
}
