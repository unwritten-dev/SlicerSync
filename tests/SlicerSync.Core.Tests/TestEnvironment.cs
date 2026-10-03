using SlicerSync.Core;
using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;
using SlicerSync.Core.Sync;

namespace SlicerSync.Core.Tests;

/// <summary>
/// Ein simulierter „PC“ in einem temporären Ordner: eigenes AppData, eigenes Repository,
/// isolierte Git-Konfiguration. Echte Profil- oder Einstellungsordner werden nie berührt.
/// </summary>
public sealed class TestPc
{
    public TestPc(TestEnvironment env, string name)
    {
        Root = Path.Combine(env.Root, name);
        Paths = new AppPaths(
            Path.Combine(Root, "Roaming"),
            Path.Combine(Root, "Local"),
            Path.Combine(Root, "Home"),
            new[] { Path.Combine(Root, "ProgramFiles") });
        Directory.CreateDirectory(Paths.RoamingAppData);

        Git = env.CreateRunner();
        Log = new MemoryLogger();
        Backups = new BackupService(Paths.BackupDir, Log);
        Detector = new SlicerDetector(Paths);
        Guard = new ProcessGuard(() => RunningProcesses);
        Setup = new SetupService(Git, Backups, Guard, Detector, Log);
        Settings = new AppSettings
        {
            RepoPath = Paths.DefaultRepoPath,
            RemoteUrl = env.RemoteUrl,
            Branch = "main",
        };
        Sync = new SyncService(() => Settings, Git, Log);
    }

    public string Root { get; }
    public AppPaths Paths { get; }
    public GitRunner Git { get; }
    public MemoryLogger Log { get; }
    public BackupService Backups { get; }
    public SlicerDetector Detector { get; }
    public ProcessGuard Guard { get; }
    public SetupService Setup { get; }
    public SyncService Sync { get; }
    public AppSettings Settings { get; }
    public List<string> RunningProcesses { get; } = new();

    public GitRepository Repo => new(Git, Settings.RepoPath);

    public string OrcaDir(string set, string folder) => Path.Combine(Paths.RoamingAppData, "OrcaSlicer", "user", set, folder);
    public string PrusaDir(string folder) => Path.Combine(Paths.RoamingAppData, "PrusaSlicer", folder);

    public string WriteFile(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);
        File.WriteAllText(file, content);
        return file;
    }

    /// <summary>Legt typische Orca- und Prusa-Profile an, inkl. Prusa-Cache, der nicht gesichert werden darf.</summary>
    public void CreateSampleProfiles()
    {
        WriteFile(OrcaDir("default", "filament"), "PLA Fein.json", "{\"name\":\"PLA Fein\"}");
        WriteFile(OrcaDir("default", "filament"), "PLA Fein.info", "sync_info = ");
        WriteFile(OrcaDir("default", "process"), "0.2mm Schnell.json", "{\"layer_height\":\"0.2\"}");
        WriteFile(OrcaDir("default", "machine"), "Mein Drucker.json", "{\"printer\":\"x\"}");
        WriteFile(PrusaDir("print"), "Fein.ini", "layer_height = 0.1");
        WriteFile(PrusaDir("filament"), "PETG.ini", "temperature = 240");
        WriteFile(PrusaDir("printer"), "MK4.ini", "bed_shape = 0x0");
        WriteFile(Path.Combine(Paths.RoamingAppData, "PrusaSlicer", "cache"), "vendor.idx", "cache");
        WriteFile(Path.Combine(Paths.RoamingAppData, "PrusaSlicer"), "PrusaSlicer.ini", "window_pos = 1");
    }

    public SetupResult NewSetup(Func<ProfileMapping, bool>? filter = null)
    {
        var candidates = Detector.Detect().Candidates.Where(filter ?? (_ => true)).ToList();
        return Setup.RunNewSetup(Settings, candidates);
    }
}

public sealed class TestEnvironment : IDisposable
{
    public TestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "SlicerSyncTests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Root);

        GitExe = GitLocator.Find() ?? throw new InvalidOperationException("Für die Tests wird git.exe benötigt.");
        GlobalConfig = Path.Combine(Root, "gitconfig");
        File.WriteAllText(GlobalConfig, "[user]\n\tname = Test\n\temail = test@example.invalid\n[init]\n\tdefaultBranch = main\n");

        RemotePath = Path.Combine(Root, "remote.git");
        CreateRunner().RunChecked(null, "init", "--bare", "-b", "main", RemotePath);
    }

    public string Root { get; }
    public string GitExe { get; }
    public string GlobalConfig { get; }
    public string RemotePath { get; }
    public string RemoteUrl => RemotePath;

    public GitRunner CreateRunner()
    {
        var runner = new GitRunner(GitExe);
        runner.Environment["GIT_CONFIG_GLOBAL"] = GlobalConfig;
        runner.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        return runner;
    }

    public TestPc CreatePc(string name) => new(this, name);

    public void Dispose()
    {
        try
        {
            // Erst alle Junctions entfernen, dann den Rest löschen.
            foreach (var dir in Directory.EnumerateDirectories(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
                         .Where(Junction.IsJunction).ToList())
                Directory.Delete(dir, false);
            foreach (var file in Directory.EnumerateFiles(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
        catch (Exception)
        {
            // Aufräumen ist best effort.
        }
    }
}
