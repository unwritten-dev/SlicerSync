using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;
using SlicerSync.Core.Sync;

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
    [InlineData("PrusaSlicer 3.0.0-alpha1", SlicerKind.Prusa, false)]
    [InlineData("PrusaSlicer 3.0.0-alpha1", SlicerKind.Prusa3, true)]
    [InlineData("PrusaSlicer 3.0.0-alpha1", SlicerKind.PrusaAlpha, false)]
    [InlineData("PrusaSlicer 2.9.0-alpha1", SlicerKind.PrusaAlpha, true)]
    [InlineData("PrusaSlicer 2.9.0-beta2", SlicerKind.PrusaBeta, true)]
    [InlineData("PrusaSlicer 2.9.2", SlicerKind.PrusaAlpha, false)]
    public void MatchesInstalledApp(string displayName, string kind, bool expected) =>
        Assert.Equal(expected, SlicerCatalog.Find(kind)!.MatchesInstalledApp(displayName));

    [Fact]
    public void Detector_FindsPrusaAlphaSeparately()
    {
        var pc = _env.CreatePc("A");
        pc.InstalledApps.Add("PrusaSlicer 2.9.0-alpha1");
        pc.WriteFile(Path.Combine(pc.Paths.RoamingAppData, @"PrusaSlicer-alpha\print"), "Fine.ini", "x");

        var result = pc.Detector.Detect();

        var alpha = Assert.Single(result.Candidates);
        Assert.Equal(@"PrusaAlpha\print", alpha.RepoRelativePath);
        Assert.Equal("PrusaSlicer Alpha – print", alpha.DisplayName);
        Assert.True(result.Slicers.Single(s => s.Definition.Kind == SlicerKind.PrusaAlpha).Installed);
        Assert.False(result.Slicers.Single(s => s.Definition.Kind == SlicerKind.Prusa).Installed);
    }

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
    public void Prusa3_BacksUpOnlyUserPresets_NotVendorDataOrLogs()
    {
        var pc = _env.CreatePc("A");
        var root = Path.Combine(pc.Paths.RoamingAppData, "PrusaSlicer3-dev");
        pc.WriteFile(Path.Combine(root, @"presets\user\prusa-research-fff\PrusaResearch"), "print-0.20mm - Copy.yaml", "x");
        pc.WriteFile(Path.Combine(root, @"presets\local\prusa-research-fff\PrusaResearch\assets"), "mk4_bed.stl", "x");
        pc.WriteFile(Path.Combine(root, "shared_runtime"), "log.txt", "x");

        var candidate = Assert.Single(pc.Detector.Detect().Candidates);
        Assert.Equal(@"Prusa3\presets\user", candidate.RepoRelativePath);
        Assert.Equal(@"PrusaSlicer 3 – presets\user", candidate.DisplayName);

        pc.Setup.RunNewSetup(pc.Settings, new[] { candidate });

        Assert.True(Junction.IsJunction(Path.Combine(root, @"presets\user")));
        Assert.False(Junction.IsJunction(root));
        Assert.True(File.Exists(Path.Combine(pc.Settings.RepoPath, @"Prusa3\presets\user\prusa-research-fff\PrusaResearch\print-0.20mm - Copy.yaml")));
        Assert.False(Directory.Exists(Path.Combine(pc.Settings.RepoPath, @"Prusa3\presets\local")));
    }

    [Fact]
    public void SwitchFromWholeCustomFolder_ToBuiltInPrusa3_InOneStep()
    {
        // The situation after adding all of %AppData%\PrusaSlicer3-dev as a custom folder.
        var pc = _env.CreatePc("A");
        var root = Path.Combine(pc.Paths.RoamingAppData, "PrusaSlicer3-dev");
        pc.WriteFile(Path.Combine(root, @"presets\user"), "print.yaml", "mine");
        pc.WriteFile(Path.Combine(root, "shared_runtime"), "log.txt", "x");
        var whole = pc.Detector.CreateCustomMapping("PrusaSlicer", root, "PrusaSlicer");
        pc.Setup.RunNewSetup(pc.Settings, new[] { whole });
        Assert.True(Junction.IsJunction(root));

        // Adding the inner folder while the whole folder is linked is refused (would nest junctions).
        var userPresets = pc.Detector.CreateMapping(SlicerKind.Prusa3, "", @"presets\user");
        Assert.Throws<SetupException>(() => pc.Setup.AddMappings(pc.Settings, new[] { userPresets }));

        // Settings dialog order: remove first, then add.
        pc.Setup.RemoveMappings(pc.Settings, new[] { whole });
        pc.Setup.AddMappings(pc.Settings, new[] { userPresets });

        Assert.False(Junction.IsJunction(root));
        Assert.True(Junction.IsJunction(Path.Combine(root, @"presets\user")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(root, @"presets\user\print.yaml")));
        Assert.True(File.Exists(Path.Combine(root, @"shared_runtime\log.txt")));
        // A newly set up PC must not link the old whole folder again.
        var manifest = RepoManifest.Read(pc.Settings.RepoPath)!;
        Assert.DoesNotContain(manifest.Folders, f => f.Slicer == SlicerKind.Custom);
        Assert.Contains(manifest.Folders, f => f.Slicer == SlicerKind.Prusa3);
    }

    /// <summary>Main PC: whole Prusa 3 folder as custom folder, set up, then switched in the wizard.</summary>
    private (TestPc Pc, ProfileMapping Whole, ProfileMapping UserPresets, string Root) PcWithWholePrusa3Folder()
    {
        var pc = _env.CreatePc("A");
        var root = Path.Combine(pc.Paths.RoamingAppData, "PrusaSlicer3-dev");
        pc.WriteFile(Path.Combine(root, @"presets\user"), "print.yaml", "mine");
        pc.WriteFile(Path.Combine(root, "shared_runtime"), "log.txt", "x");
        var whole = pc.Detector.CreateCustomMapping("PrusaSlicer", root, "PrusaSlicer");
        pc.Setup.RunNewSetup(pc.Settings, new[] { whole });
        var userPresets = pc.Detector.CreateMapping(SlicerKind.Prusa3, "", @"presets\user");
        return (pc, whole, userPresets, root);
    }

    [Fact]
    public void Wizard_AfterRestore_SwitchToBuiltIn_DisablesOldCustomFolder()
    {
        // Exactly the log of the main PC: restore, then new setup with only the built-in folder selected.
        var (pc, _, userPresets, _) = PcWithWholePrusa3Folder();
        pc.Setup.Uninstall(pc.Settings);

        pc.Setup.RunNewSetup(pc.Settings, new[] { userPresets });

        Assert.False(pc.Settings.Mappings.Single(m => m.IsCustom).Enabled);
        Assert.Empty(SyncService.CheckJunctions(pc.Settings)); // no "no longer linked" warning
        Assert.DoesNotContain(RepoManifest.Read(pc.Settings.RepoPath)!.Folders, f => f.Slicer == SlicerKind.Custom);
    }

    [Fact]
    public void Wizard_WithoutRestore_SwitchToBuiltIn_UnlinksOldCustomFolder()
    {
        var (pc, _, userPresets, root) = PcWithWholePrusa3Folder();

        pc.Setup.RunNewSetup(pc.Settings, new[] { userPresets });

        Assert.False(Junction.IsJunction(root));
        Assert.True(Junction.IsJunction(Path.Combine(root, @"presets\user")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(root, @"presets\user\print.yaml")));
        Assert.Empty(SyncService.CheckJunctions(pc.Settings));
    }

    [Fact]
    public void DisableSuperseded_RepairsSettingsLeftByOlderVersion()
    {
        // State after the bug in 1.0.3: custom folder still enabled but unlinked, built-in folder linked.
        var (pc, whole, userPresets, _) = PcWithWholePrusa3Folder();
        pc.Setup.Uninstall(pc.Settings);
        pc.Setup.RunNewSetup(pc.Settings, new[] { userPresets });
        pc.Settings.Mappings.Single(m => m.IsCustom).Enabled = true;
        RepoManifest.AddFolders(pc.Settings.RepoPath, new[] { whole }, pc.Detector);
        Assert.NotEmpty(SyncService.CheckJunctions(pc.Settings));

        var disabled = SetupService.DisableSuperseded(pc.Settings);

        Assert.Single(disabled);
        Assert.Empty(SyncService.CheckJunctions(pc.Settings));
        Assert.DoesNotContain(RepoManifest.Read(pc.Settings.RepoPath)!.Folders, f => f.Slicer == SlicerKind.Custom);
        Assert.Empty(SetupService.DisableSuperseded(pc.Settings)); // nothing more to do
    }

    [Fact]
    public void CloneSetup_SkipsCustomFolderCoveredByBuiltIn()
    {
        // The repository as it is now: manifest lists the whole custom folder AND the built-in subfolder.
        var (a, whole, userPresets, _) = PcWithWholePrusa3Folder();
        a.Setup.Uninstall(a.Settings);
        a.Setup.RunNewSetup(a.Settings, new[] { userPresets });
        RepoManifest.AddFolders(a.Settings.RepoPath, new[] { whole }, a.Detector);
        a.Sync.Sync();

        var b = _env.CreatePc("B");
        var result = b.Setup.RunCloneSetup(b.Settings);

        Assert.Contains(result.Warnings, w => w.Contains("PrusaSlicer"));
        var root = Path.Combine(b.Paths.RoamingAppData, "PrusaSlicer3-dev");
        Assert.False(Junction.IsJunction(root));
        Assert.True(Junction.IsJunction(Path.Combine(root, @"presets\user")));
        Assert.DoesNotContain(b.Settings.Mappings, m => m.IsCustom);
    }

    [Fact]
    public void CloneSetupAgain_AfterMainPcSwitched_DropsOldCustomFolder()
    {
        // Second PC set up while the main PC still backed up the whole Prusa 3 folder.
        var (a, whole, userPresets, _) = PcWithWholePrusa3Folder();
        var b = _env.CreatePc("B");
        b.Setup.RunCloneSetup(b.Settings);
        var rootB = Path.Combine(b.Paths.RoamingAppData, "PrusaSlicer3-dev");
        Assert.True(Junction.IsJunction(rootB));

        // Main PC switches to the built-in folder and syncs.
        a.Setup.RunNewSetup(a.Settings, new[] { userPresets });
        a.Sync.Sync();

        // Second PC runs the second-PC setup again.
        var result = b.Setup.RunCloneSetup(b.Settings);

        Assert.False(Junction.IsJunction(rootB));
        Assert.True(Junction.IsJunction(Path.Combine(rootB, @"presets\user")));
        Assert.False(b.Settings.Mappings.Single(m => m.IsCustom).Enabled);
        Assert.Empty(SyncService.CheckJunctions(b.Settings));
    }

    [Fact]
    public void NewSetup_RefusesNestedFolders()
    {
        var pc = _env.CreatePc("A");
        var root = Path.Combine(pc.Paths.RoamingAppData, "PrusaSlicer3-dev");
        pc.WriteFile(Path.Combine(root, @"presets\user"), "print.yaml", "x");
        var whole = pc.Detector.CreateCustomMapping("PrusaSlicer", root);
        var inner = pc.Detector.CreateMapping(SlicerKind.Prusa3, "", @"presets\user");

        Assert.Throws<SetupException>(() => pc.Setup.RunNewSetup(pc.Settings, new[] { whole, inner }));
        Assert.False(Junction.IsJunction(root));
    }

    [Fact]
    public void ProcessGuard_ChecksProgramsOfCustomFolders()
    {
        var pc = _env.CreatePc("A");
        var cura = pc.Detector.CreateCustomMapping("Cura", pc.Root, "UltiMaker-Cura");
        var unknown = pc.Detector.CreateCustomMapping("Other", pc.Root);
        var guard = new ProcessGuard(() => new[] { "UltiMaker-Cura", "explorer" });

        Assert.Empty(guard.RunningSlicers());
        Assert.Equal(new[] { "Cura" }, guard.RunningSlicers(new[] { cura, unknown }));
    }

    [Fact]
    public void Setup_RefusesWhileCustomProgramRuns_AndProcessNameReachesSecondPc()
    {
        var a = _env.CreatePc("A");
        a.CreateSampleProfiles();
        var curaDir = Path.Combine(a.Paths.RoamingAppData, @"cura\5.8");
        a.WriteFile(curaDir, "x.cfg", "x");
        var cura = a.Detector.CreateCustomMapping("Cura", curaDir, "UltiMaker-Cura");
        var selected = a.Detector.Detect().Candidates.Append(cura).ToList();

        a.RunningProcesses.Add("UltiMaker-Cura");
        Assert.Throws<SlicerRunningException>(() => a.Setup.RunNewSetup(a.Settings, selected));
        Assert.False(Junction.IsJunction(curaDir));

        a.RunningProcesses.Clear();
        a.Setup.RunNewSetup(a.Settings, selected);

        var b = _env.CreatePc("B");
        b.Setup.RunCloneSetup(b.Settings);
        Assert.Equal("UltiMaker-Cura", b.Settings.Mappings.Single(m => m.IsCustom).ProcessName);
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
