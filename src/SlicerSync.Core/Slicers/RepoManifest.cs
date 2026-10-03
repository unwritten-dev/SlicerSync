using SlicerSync.Core.Config;

namespace SlicerSync.Core.Slicers;

/// <summary>
/// slicersync.json im Repository: welche Profilordner gesichert werden. Damit kann ein
/// zweiter PC die Zuordnungen auch für (noch) leere Ordner übernehmen.
/// </summary>
public sealed class RepoManifest
{
    public const string FileName = "slicersync.json";

    public int Version { get; set; } = 1;
    public List<ManifestFolder> Folders { get; set; } = new();

    public sealed class ManifestFolder
    {
        public string Slicer { get; set; } = "";
        public string ProfileSet { get; set; } = "";
        public string Folder { get; set; } = "";
    }

    public static RepoManifest? Read(string repoPath)
    {
        var file = Path.Combine(repoPath, FileName);
        if (!File.Exists(file))
            return null;
        try
        {
            return SettingsStore.Deserialize<RepoManifest>(File.ReadAllText(file));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Ergänzt die Liste um die angegebenen Ordner (entfernt nie Einträge anderer PCs).</summary>
    public static void AddFolders(string repoPath, IEnumerable<ProfileMapping> mappings)
    {
        var manifest = Read(repoPath) ?? new RepoManifest();
        foreach (var m in mappings)
        {
            var exists = manifest.Folders.Any(f =>
                f.Slicer == m.Slicer &&
                string.Equals(f.ProfileSet, m.ProfileSet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Folder, m.Folder, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                manifest.Folders.Add(new ManifestFolder { Slicer = m.Slicer, ProfileSet = m.ProfileSet, Folder = m.Folder });
        }
        File.WriteAllText(Path.Combine(repoPath, FileName), SettingsStore.Serialize(manifest));
    }
}
