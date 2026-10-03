using SlicerSync.Core.Config;

namespace SlicerSync.Core.Slicers;

/// <summary>
/// slicersync.json in the repository: which profile folders are backed up. Lets a second PC
/// take over the mappings even for folders that are (still) empty, and custom folders.
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
        /// <summary>Custom folders only: original location with %APPDATA%/%LOCALAPPDATA%/%USERPROFILE% tokens.</summary>
        public string Path { get; set; } = "";
    }

    public static RepoManifest? Read(string repoPath)
    {
        var file = System.IO.Path.Combine(repoPath, FileName);
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

    /// <summary>Adds the given folders to the list (never removes entries of other PCs).</summary>
    public static void AddFolders(string repoPath, IEnumerable<ProfileMapping> mappings, SlicerDetector detector)
    {
        var manifest = Read(repoPath) ?? new RepoManifest();
        foreach (var m in mappings)
        {
            var exists = manifest.Folders.Any(f =>
                f.Slicer == m.Slicer &&
                string.Equals(f.ProfileSet, m.ProfileSet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Folder, m.Folder, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                manifest.Folders.Add(new ManifestFolder
                {
                    Slicer = m.Slicer,
                    ProfileSet = m.ProfileSet,
                    Folder = m.Folder,
                    Path = m.IsCustom ? detector.ToPathTemplate(m.OriginalPath) : "",
                });
        }
        File.WriteAllText(System.IO.Path.Combine(repoPath, FileName), SettingsStore.Serialize(manifest));
    }
}
