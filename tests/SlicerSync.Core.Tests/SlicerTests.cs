using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;

namespace SlicerSync.Core.Tests;

public sealed class SlicerTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void Detector_FindsBambuStudioAndSuperSlicer()
    {
        var pc = _env.CreatePc("A");
        pc.WriteFile(Path.Combine(pc.Paths.RoamingAppData, @"BambuStudio\user\12345\filament"), "PLA.json", "{}");
        pc.WriteFile(Path.Combine(pc.Paths.RoamingAppData, @"SuperSlicer\print"), "Fine.ini", "x");
        pc.WriteFile(Path.Combine(pc.Paths.RoamingAppData, @"SuperSlicer\physical_printer"), "Voron.ini", "apikey = x");

        var candidates = pc.Detector.Detect().Candidates;

        Assert.Equal(
            new[] { @"Bambu\12345\filament", @"SuperSlicer\print", @"SuperSlicer\physical_printer" },
            candidates.Select(c => c.RepoRelativePath));
        Assert.True(candidates.Single(c => c.Folder == "physical_printer").MayContainSecrets);
        Assert.Equal(@"Bambu Studio – 12345\filament", candidates[0].DisplayName);
    }

    [Fact]
    public void Detector_ReportsInstalledSlicers()
    {
        var pc = _env.CreatePc("A");
        pc.WriteFile(Path.Combine(pc.Paths.ProgramFilesDirs[0], "Bambu Studio"), "bambu-studio.exe", "");

        var slicers = pc.Detector.Detect().Slicers;

        Assert.True(slicers.Single(s => s.Definition.Kind == SlicerKind.Bambu).Installed);
        Assert.False(slicers.Single(s => s.Definition.Kind == SlicerKind.Orca).Installed);
    }

    [Fact]
    public void Detector_FindsSlicersInstalledAnywhere_ViaInstalledAppsList()
    {
        var pc = _env.CreatePc("A");
        pc.InstalledApps.AddRange(new[] { "OrcaSlicer", "PrusaSlicer 2.9.2", "Bambu Studio", "Mozilla Firefox" });

        var slicers = pc.Detector.Detect().Slicers.ToDictionary(s => s.Definition.Kind, s => s.Installed);

        Assert.True(slicers[SlicerKind.Orca]);
        Assert.True(slicers[SlicerKind.Prusa]);
        Assert.True(slicers[SlicerKind.Bambu]);
        Assert.False(slicers[SlicerKind.SuperSlicer]);
    }

    [Theory]
    [InlineData("OrcaSlicer", SlicerKind.Orca, true)]
    [InlineData("Orca Slicer 2.3.0", SlicerKind.Orca, true)]
    [InlineData("BambuStudio", SlicerKind.Bambu, true)]
    [InlineData("PrusaSlicer 2.9.2", SlicerKind.Prusa, true)]
    [InlineData("Prusa Connect", SlicerKind.Prusa, false)]
    [InlineData("SuperSlicer 2.5.59", SlicerKind.SuperSlicer, true)]
    public void MatchesInstalledApp(string displayName, string kind, bool expected) =>
        Assert.Equal(expected, SlicerCatalog.Find(kind)!.MatchesInstalledApp(displayName));

    [Fact]
    public void InstalledApps_ReadsRegistryWithoutError()
    {
        // Only checks that reading the real list works; its content depends on the machine.
        Assert.NotNull(InstalledApps.DisplayNames().ToList());
    }

    [Fact]
    public void ProcessGuard_KnowsAllBuiltInSlicers()
    {
        var guard = new ProcessGuard(() => new[] { "bambu-studio", "superslicer", "notepad" });
        Assert.Equal(new[] { "Bambu Studio", "SuperSlicer" }, guard.RunningSlicers());
    }

    [Fact]
    public void PathTemplate_RoundTripsBetweenPcs()
    {
        var a = _env.CreatePc("A");
        var b = _env.CreatePc("B");
        var pathA = Path.Combine(a.Paths.RoamingAppData, @"cura\5.8\quality_changes");

        var template = a.Detector.ToPathTemplate(pathA);

        Assert.Equal(@"%APPDATA%\cura\5.8\quality_changes", template);
        Assert.Equal(Path.Combine(b.Paths.RoamingAppData, @"cura\5.8\quality_changes"), b.Detector.FromPathTemplate(template));
        Assert.Equal(@"%USERPROFILE%\Profiles", a.Detector.ToPathTemplate(Path.Combine(a.Paths.UserProfile, "Profiles")));
        Assert.Equal(@"D:\Elsewhere", a.Detector.ToPathTemplate(@"D:\Elsewhere"));
    }

    [Fact]
    public void CustomFolder_IsBackedUp_AndArrivesOnSecondPc()
    {
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        var curaA = Path.Combine(a.Paths.RoamingAppData, @"cura\5.8\quality_changes");
        a.WriteFile(curaA, "fine.inst.cfg", "[general]\nname = fine");
        var custom = a.Detector.CreateCustomMapping("Cura", curaA);
        var selected = a.Detector.Detect().Candidates.Append(custom).ToList();

        a.Setup.RunNewSetup(a.Settings, selected);

        Assert.True(Junction.PointsTo(curaA, Path.Combine(a.Settings.RepoPath, @"Custom\Cura")));
        Assert.Equal(@"%APPDATA%\cura\5.8\quality_changes",
            RepoManifest.Read(a.Settings.RepoPath)!.Folders.Single(f => f.Slicer == SlicerKind.Custom).Path);

        var b = _env.CreatePc("B");
        b.Setup.RunCloneSetup(b.Settings);

        var curaB = Path.Combine(b.Paths.RoamingAppData, @"cura\5.8\quality_changes");
        Assert.True(Junction.IsJunction(curaB));
        Assert.Equal("[general]\nname = fine", File.ReadAllText(Path.Combine(curaB, "fine.inst.cfg")));
        Assert.Contains(b.Settings.Mappings, m => m.IsCustom && m.Folder == "Cura");
    }

    [Fact]
    public void ValidateCustomFolder_RejectsDangerousChoices()
    {
        var pc = _env.CreatePc("A");
        var repo = pc.Settings.RepoPath;
        var ok = Path.Combine(pc.Root, "profiles");
        Directory.CreateDirectory(ok);
        Directory.CreateDirectory(Path.Combine(repo, "sub"));
        var existing = new[] { pc.Detector.CreateCustomMapping("Cura", Path.Combine(pc.Root, "cura")) };

        Assert.Null(SetupService.ValidateCustomFolder("MySlicer", ok, repo, existing));
        Assert.NotNull(SetupService.ValidateCustomFolder("My Slicer", ok, repo, existing));        // space
        Assert.NotNull(SetupService.ValidateCustomFolder("Cura", ok, repo, existing));             // name taken
        Assert.NotNull(SetupService.ValidateCustomFolder("X", Path.Combine(pc.Root, "missing"), repo, existing));
        Assert.NotNull(SetupService.ValidateCustomFolder("X", Path.GetPathRoot(ok)!, repo, existing)); // drive
        Assert.NotNull(SetupService.ValidateCustomFolder("X", Path.Combine(repo, "sub"), repo, existing)); // inside repo
        Assert.NotNull(SetupService.ValidateCustomFolder("X", pc.Root, repo, existing));           // contains repo
    }
}
