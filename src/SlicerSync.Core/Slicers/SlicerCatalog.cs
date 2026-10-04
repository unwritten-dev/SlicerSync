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
    string[] UninstallNames)
{
    /// <summary>
    /// Whether an entry of the Windows "installed apps" list belongs to this slicer
    /// (name prefix, ignoring spaces and case, e.g. "PrusaSlicer 2.9.2").
    /// </summary>
    public bool MatchesInstalledApp(string displayName)
    {
        var name = displayName.Replace(" ", "");
        return UninstallNames.Any(n => name.StartsWith(n.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }

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
        new[] { "PrusaSlicer" });

    public static readonly SlicerDefinition SuperSlicer = new(
        SlicerKind.SuperSlicer, "SuperSlicer", "SuperSlicer", ProfileLayout.Flat,
        new[] { "print", "filament", "printer", "physical_printer" },
        new[] { @"SuperSlicer\superslicer.exe" },
        new[] { "superslicer", "SuperSlicer" },
        new[] { "physical_printer" },
        new[] { "SuperSlicer" });

    public static readonly IReadOnlyList<SlicerDefinition> All = new[] { Orca, Bambu, Prusa, SuperSlicer };

    public static SlicerDefinition? Find(string kind) =>
        All.FirstOrDefault(d => string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase));
}

public static class SlicerKind
{
    public const string Orca = "Orca";
    public const string Bambu = "Bambu";
    public const string Prusa = "Prusa";
    public const string SuperSlicer = "SuperSlicer";
    /// <summary>A folder chosen by the user (any other slicer or program).</summary>
    public const string Custom = "Custom";
}
