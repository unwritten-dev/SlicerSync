namespace SlicerSync.Core.Slicers;

public enum ProfileLayout
{
    /// <summary>%AppData%\&lt;app&gt;\user\&lt;set&gt;\&lt;folder&gt; (Orca Slicer, Bambu Studio).</summary>
    UserSets,
    /// <summary>%AppData%\&lt;app&gt;\&lt;folder&gt; (PrusaSlicer, SuperSlicer).</summary>
    Flat,
}

/// <summary>What SlicerSync knows about a slicer: where its profiles live and how its process is called.</summary>
public sealed record SlicerDefinition(
    string Kind,
    string DisplayName,
    string AppDataFolder,
    ProfileLayout Layout,
    string[] Folders,
    string[] ExeCandidates,
    string[] ProcessNames,
    string[] SecretFolders,
    string[] UninstallNames,
    string? Channel = null)
{
    /// <summary>
    /// Whether an entry of the Windows "installed apps" list belongs to this slicer
    /// (name prefix, ignoring spaces and case, e.g. "PrusaSlicer 2.9.2"). If <see cref="Channel"/>
    /// is set ("" = stable, "alpha", "beta"), the entry must also belong to that release channel.
    /// </summary>
    public bool MatchesInstalledApp(string displayName)
    {
        var name = displayName.Replace(" ", "");
        return UninstallNames.Any(n => name.StartsWith(n.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) &&
               (Channel == null || ReleaseChannel(displayName) == Channel);
    }

    private static string ReleaseChannel(string displayName) =>
        displayName.Contains("alpha", StringComparison.OrdinalIgnoreCase) ? "alpha"
        : displayName.Contains("beta", StringComparison.OrdinalIgnoreCase) ? "beta"
        : "";

    /// <summary>Top-level folder inside the repository.</summary>
    public string RepoFolder => Kind;
}

/// <summary>Built-in slicers. Any other program can be added as a custom folder.</summary>
public static class SlicerCatalog
{
    public static readonly SlicerDefinition Orca = new(
        SlicerKind.Orca, "Orca Slicer", "OrcaSlicer", ProfileLayout.UserSets,
        new[] { "filament", "machine", "process" },
        new[] { @"OrcaSlicer\orca-slicer.exe" },
        new[] { "orca-slicer", "OrcaSlicer" },
        Array.Empty<string>(),
        new[] { "OrcaSlicer", "Orca Slicer" });

    public static readonly SlicerDefinition Bambu = new(
        SlicerKind.Bambu, "Bambu Studio", "BambuStudio", ProfileLayout.UserSets,
        new[] { "filament", "machine", "process" },
        new[] { @"Bambu Studio\bambu-studio.exe" },
        new[] { "bambu-studio", "BambuStudio" },
        Array.Empty<string>(),
        new[] { "Bambu Studio" });

    public static readonly SlicerDefinition Prusa = new(
        SlicerKind.Prusa, "PrusaSlicer", "PrusaSlicer", ProfileLayout.Flat,
        new[] { "print", "filament", "printer", "physical_printer" },
        new[] { @"Prusa3D\PrusaSlicer\prusa-slicer.exe" },
        new[] { "prusa-slicer", "PrusaSlicer" },
        new[] { "physical_printer" },
        new[] { "PrusaSlicer" },
        Channel: "");

    // Alpha and beta builds of PrusaSlicer keep their own data folder, separate from the stable
    // version, because their profile format may be newer. They are backed up separately as well.
    // Their process is called like the stable one, which the stable entry already covers.
    public static readonly SlicerDefinition PrusaAlpha = new(
        SlicerKind.PrusaAlpha, "PrusaSlicer Alpha", "PrusaSlicer-alpha", ProfileLayout.Flat,
        Prusa.Folders,
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[] { "physical_printer" },
        new[] { "PrusaSlicer" },
        Channel: "alpha");

    public static readonly SlicerDefinition PrusaBeta = new(
        SlicerKind.PrusaBeta, "PrusaSlicer Beta", "PrusaSlicer-beta", ProfileLayout.Flat,
        Prusa.Folders,
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[] { "physical_printer" },
        new[] { "PrusaSlicer" },
        Channel: "beta");

    public static readonly SlicerDefinition SuperSlicer = new(
        SlicerKind.SuperSlicer, "SuperSlicer", "SuperSlicer", ProfileLayout.Flat,
        new[] { "print", "filament", "printer", "physical_printer" },
        new[] { @"SuperSlicer\superslicer.exe" },
        new[] { "superslicer", "SuperSlicer" },
        new[] { "physical_printer" },
        new[] { "SuperSlicer" });

    public static readonly IReadOnlyList<SlicerDefinition> All = new[] { Orca, Bambu, Prusa, PrusaAlpha, PrusaBeta, SuperSlicer };

    public static SlicerDefinition? Find(string kind) =>
        All.FirstOrDefault(d => string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase));
}

public static class SlicerKind
{
    public const string Orca = "Orca";
    public const string Bambu = "Bambu";
    public const string Prusa = "Prusa";
    public const string PrusaAlpha = "PrusaAlpha";
    public const string PrusaBeta = "PrusaBeta";
    public const string SuperSlicer = "SuperSlicer";
    /// <summary>A folder chosen by the user (any other slicer or program).</summary>
    public const string Custom = "Custom";
}
