using SlicerSync.Core.Config;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Slicers;
using SlicerSync.Core.Sync;

namespace SlicerSync.Core.Tests;

public sealed class UnitTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void CommitMessage_ReplacesPlaceholders()
    {
        var text = CommitMessage.Format("{datum} {zeit} {rechner} {benutzer} {anzahl}", new DateTime(2026, 10, 3, 9, 5, 0), "PC1", "max", 7);
        Assert.Equal("2026-10-03 09:05 PC1 max 7", text);
    }

    [Fact]
    public void CommitMessage_EmptyTemplate_UsesDefault()
    {
        var text = CommitMessage.Format("", new DateTime(2026, 1, 2), "PC", "u", 3);
        Assert.StartsWith("Profile sync 2026-01-02", text);
    }

    [Theory]
    [InlineData("2026-10-03 08:00", "18:00", "2026-10-03 18:00")]
    [InlineData("2026-10-03 18:00", "18:00", "2026-10-04 18:00")]
    [InlineData("2026-10-03 23:59", "00:30", "2026-10-04 00:30")]
    public void NextDailyRun(string now, string time, string expected)
    {
        var next = SyncScheduler.NextDailyRun(DateTime.Parse(now), TimeSpan.Parse(time));
        Assert.Equal(DateTime.Parse(expected), next);
    }

    [Theory]
    [InlineData(@"C:\r\Prusa\print\a.ini", true)]
    [InlineData(@"C:\r\.git\index", false)]
    [InlineData(@"C:\r\.git", false)]
    [InlineData(@"C:\r\.gitattributes", true)]
    public void IsRelevantChange(string path, bool expected) =>
        Assert.Equal(expected, SyncScheduler.IsRelevantChange(@"C:\r", path));

    [Fact]
    public async Task Debouncer_RunsOnceAfterBurst()
    {
        var count = 0;
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(200), () => Interlocked.Increment(ref count));
        for (var i = 0; i < 10; i++)
        {
            debouncer.Trigger();
            await Task.Delay(20);
        }
        await Task.Delay(600);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Scheduler_OnChange_TriggersSyncAfterFileChange_ButNotForGitInternals()
    {
        var repo = Path.Combine(_env.Root, "watched");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var count = 0;
        using var scheduler = new SyncScheduler(() => Interlocked.Increment(ref count), new MemoryLogger());
        scheduler.Apply(new AppSettings { RepoPath = repo, SyncMode = SyncMode.OnChange, DebounceSeconds = 1 });

        File.WriteAllText(Path.Combine(repo, ".git", "index"), "x");
        await Task.Delay(1800);
        Assert.Equal(0, count);

        File.WriteAllText(Path.Combine(repo, "a.ini"), "x");
        File.WriteAllText(Path.Combine(repo, "b.ini"), "x");
        await Task.Delay(2500);
        Assert.Equal(1, count);
    }

    [Fact]
    public void SettingsStore_RoundTrip_AndCorruptFileIsKept()
    {
        var file = Path.Combine(_env.Root, "cfg", "settings.json");
        var store = new SettingsStore(file, new MemoryLogger());
        var settings = new AppSettings
        {
            RepoPath = @"C:\x",
            SyncMode = SyncMode.Daily,
            DailyTime = "07:30",
            Mappings = { new ProfileMapping { Slicer = SlicerKind.Prusa, Folder = "print", RepoRelativePath = @"Prusa\print" } },
        };
        store.Save(settings);
        Assert.Contains("\"Daily\"", File.ReadAllText(file));

        var loaded = store.Load();
        Assert.Equal(SyncMode.Daily, loaded.SyncMode);
        Assert.Equal(new TimeSpan(7, 30, 0), loaded.GetDailyTime());
        Assert.Single(loaded.Mappings);

        File.WriteAllText(file, "{ kaputt");
        var fallback = store.Load();
        Assert.False(fallback.SetupCompleted);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "settings.json.broken-*"));
    }

    [Fact]
    public void ProcessGuard_DetectsSlicers()
    {
        var guard = new ProcessGuard(() => new[] { "explorer", "prusa-slicer", "orca-slicer" });
        Assert.Equal(new[] { "Orca Slicer", "PrusaSlicer" }, guard.RunningSlicers());
        Assert.Throws<SlicerRunningException>(guard.EnsureSlicersClosed);
        Assert.Empty(new ProcessGuard(() => new[] { "explorer" }).RunningSlicers());
    }

    [Fact]
    public void ProfileMapping_PhysicalPrinterIsFlaggedAsSensitive()
    {
        var detector = new SlicerDetector(_env.CreatePc("A").Paths);
        Assert.True(detector.CreateMapping(SlicerKind.Prusa, "", "physical_printer").MayContainSecrets);
        Assert.False(detector.CreateMapping(SlicerKind.Prusa, "", "print").MayContainSecrets);
    }

    [Fact]
    public void ParseLog_HandlesRenamesAndUmlauts()
    {
        var raw = "\u001e" + "abc\u001fTest\u001f2026-10-03T10:00:00+02:00\u001fzweiter\u001f\nR100\0a.ini\0b.ini\0M\0ä ö.json\0\0" +
                  "\u001e" + "def\u001fTest\u001f2026-10-02T10:00:00+02:00\u001ferster\u001f\nA\0a.ini\0";
        var commits = GitRepository.ParseLog(raw);

        Assert.Equal(2, commits.Count);
        Assert.Equal("zweiter", commits[0].Message);
        Assert.Equal(new ChangedFile("R", "b.ini", "a.ini"), commits[0].Files[0]);
        Assert.Equal(new ChangedFile("M", "ä ö.json"), commits[0].Files[1]);
        Assert.Single(commits[1].Files);
    }
}
