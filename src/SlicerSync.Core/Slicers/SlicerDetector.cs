using SlicerSync.Core.Config;

namespace SlicerSync.Core.Slicers;

public sealed record SlicerInfo(string Kind, string DisplayName, bool Installed, string ProfileRoot, bool ProfileRootExists);

public sealed record DetectionResult(IReadOnlyList<SlicerInfo> Slicers, IReadOnlyList<ProfileMapping> Candidates);

/// <summary>Findet installierte Slicer und deren Profilordner.</summary>
public sealed class SlicerDetector
{
    public static readonly string[] OrcaFolders = { "filament", "machine", "process" };
    public static readonly string[] PrusaFolders = { "print", "filament", "printer", "physical_printer" };

    private readonly AppPaths _paths;

    public SlicerDetector(AppPaths paths) => _paths = paths;

    public string OrcaUserRoot => Path.Combine(_paths.RoamingAppData, "OrcaSlicer", "user");
    public string PrusaRoot => Path.Combine(_paths.RoamingAppData, "PrusaSlicer");

    public DetectionResult Detect()
    {
        var slicers = new List<SlicerInfo>
        {
            new(SlicerKind.Orca, "Orca Slicer", IsInstalled(@"OrcaSlicer\orca-slicer.exe"), OrcaUserRoot, Directory.Exists(OrcaUserRoot)),
            new(SlicerKind.Prusa, "PrusaSlicer", IsInstalled(@"Prusa3D\PrusaSlicer\prusa-slicer.exe"), PrusaRoot, Directory.Exists(PrusaRoot)),
        };

        var candidates = new List<ProfileMapping>();

        if (Directory.Exists(OrcaUserRoot))
        {
            foreach (var setDir in Directory.EnumerateDirectories(OrcaUserRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var set = Path.GetFileName(setDir);
                foreach (var folder in OrcaFolders)
                {
                    if (Directory.Exists(Path.Combine(setDir, folder)))
                        candidates.Add(CreateMapping(SlicerKind.Orca, set, folder));
                }
            }
        }

        if (Directory.Exists(PrusaRoot))
        {
            foreach (var folder in PrusaFolders)
            {
                if (Directory.Exists(Path.Combine(PrusaRoot, folder)))
                    candidates.Add(CreateMapping(SlicerKind.Prusa, "", folder));
            }
        }

        return new DetectionResult(slicers, candidates);
    }

    /// <summary>Baut die Zuordnung Originalort ↔ Repository-Ordner für diesen Rechner.</summary>
    public ProfileMapping CreateMapping(string slicer, string profileSet, string folder)
    {
        if (slicer == SlicerKind.Orca)
        {
            return new ProfileMapping
            {
                Slicer = slicer,
                ProfileSet = profileSet,
                Folder = folder,
                OriginalPath = Path.Combine(OrcaUserRoot, profileSet, folder),
                RepoRelativePath = Path.Combine("Orca", profileSet, folder),
            };
        }
        if (slicer == SlicerKind.Prusa)
        {
            return new ProfileMapping
            {
                Slicer = slicer,
                ProfileSet = "",
                Folder = folder,
                OriginalPath = Path.Combine(PrusaRoot, folder),
                RepoRelativePath = Path.Combine("Prusa", folder),
            };
        }
        throw new ArgumentException($"Unbekannter Slicer: {slicer}");
    }

    /// <summary>
    /// Zuordnungen aus einem bestehenden Repository ableiten (Zweit-PC). Bevorzugt das Manifest,
    /// sonst die Ordnerstruktur Orca\&lt;set&gt;\&lt;ordner&gt; bzw. Prusa\&lt;ordner&gt;.
    /// </summary>
    public List<ProfileMapping> MappingsFromRepository(string repoPath)
    {
        var manifest = RepoManifest.Read(repoPath);
        if (manifest != null && manifest.Folders.Count > 0)
            return manifest.Folders.Select(f => CreateMapping(f.Slicer, f.ProfileSet, f.Folder)).ToList();

        var result = new List<ProfileMapping>();
        var orca = Path.Combine(repoPath, "Orca");
        if (Directory.Exists(orca))
        {
            foreach (var setDir in Directory.EnumerateDirectories(orca))
                foreach (var folder in OrcaFolders.Where(f => Directory.Exists(Path.Combine(setDir, f))))
                    result.Add(CreateMapping(SlicerKind.Orca, Path.GetFileName(setDir), folder));
        }
        var prusa = Path.Combine(repoPath, "Prusa");
        if (Directory.Exists(prusa))
        {
            foreach (var folder in PrusaFolders.Where(f => Directory.Exists(Path.Combine(prusa, f))))
                result.Add(CreateMapping(SlicerKind.Prusa, "", folder));
        }
        return result;
    }

    private bool IsInstalled(string relativeExe) =>
        _paths.ProgramFilesDirs.Any(dir => File.Exists(Path.Combine(dir, relativeExe)));
}
