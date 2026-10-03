using SlicerSync.Core.Config;

namespace SlicerSync.Core.Slicers;

public sealed record SlicerInfo(SlicerDefinition Definition, bool Installed, string ProfileRoot, bool ProfileRootExists);

public sealed record DetectionResult(IReadOnlyList<SlicerInfo> Slicers, IReadOnlyList<ProfileMapping> Candidates);

/// <summary>Finds installed slicers and their profile folders; builds mappings for this computer.</summary>
public sealed class SlicerDetector
{
    private readonly AppPaths _paths;

    public SlicerDetector(AppPaths paths) => _paths = paths;

    /// <summary>Folder that contains the profile folders (Orca/Bambu: the "user" folder).</summary>
    public string ProfileRoot(SlicerDefinition d) => d.Layout == ProfileLayout.UserSets
        ? Path.Combine(_paths.RoamingAppData, d.AppDataFolder, "user")
        : Path.Combine(_paths.RoamingAppData, d.AppDataFolder);

    public DetectionResult Detect()
    {
        var slicers = new List<SlicerInfo>();
        var candidates = new List<ProfileMapping>();

        foreach (var d in SlicerCatalog.All)
        {
            var root = ProfileRoot(d);
            var installed = d.ExeCandidates.Any(exe => _paths.ProgramFilesDirs.Any(dir => File.Exists(Path.Combine(dir, exe))));
            slicers.Add(new SlicerInfo(d, installed, root, Directory.Exists(root)));
            if (!Directory.Exists(root))
                continue;

            var sets = d.Layout == ProfileLayout.UserSets
                ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string?> { "" };
            foreach (var set in sets)
                foreach (var folder in d.Folders)
                {
                    var mapping = CreateMapping(d.Kind, set!, folder);
                    if (Directory.Exists(mapping.OriginalPath))
                        candidates.Add(mapping);
                }
        }

        return new DetectionResult(slicers, candidates);
    }

    /// <summary>Builds the original location ↔ repository folder mapping of a built-in slicer.</summary>
    public ProfileMapping CreateMapping(string slicer, string profileSet, string folder)
    {
        var d = SlicerCatalog.Find(slicer) ?? throw new ArgumentException($"Unknown slicer: {slicer}");
        var set = d.Layout == ProfileLayout.UserSets ? profileSet : "";
        return new ProfileMapping
        {
            Slicer = d.Kind,
            ProfileSet = set,
            Folder = folder,
            OriginalPath = Path.Combine(ProfileRoot(d), set, folder),
            RepoRelativePath = Path.Combine(d.RepoFolder, set, folder),
        };
    }

    /// <summary>A folder chosen by the user; stored under Custom\&lt;name&gt; in the repository.</summary>
    public ProfileMapping CreateCustomMapping(string name, string path) => new()
    {
        Slicer = SlicerKind.Custom,
        Folder = name,
        OriginalPath = Path.GetFullPath(path),
        RepoRelativePath = Path.Combine(SlicerKind.Custom, name),
    };

    /// <summary>Replaces this PC's user folders with %APPDATA%, %LOCALAPPDATA% or %USERPROFILE%.</summary>
    public string ToPathTemplate(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var (token, dir) in Tokens())
        {
            var prefix = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return token + "\\" + full[prefix.Length..];
        }
        return full;
    }

    public string FromPathTemplate(string template)
    {
        foreach (var (token, dir) in Tokens())
            if (template.StartsWith(token + "\\", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(dir, template[(token.Length + 1)..]);
        return template;
    }

    // Most specific first: AppData folders usually live inside the user profile.
    private IEnumerable<(string Token, string Dir)> Tokens() => new[]
    {
        ("%APPDATA%", _paths.RoamingAppData),
        ("%LOCALAPPDATA%", _paths.LocalAppData),
        ("%USERPROFILE%", _paths.UserProfile),
    };

    /// <summary>
    /// Derives mappings from an existing repository (second PC). Prefers the manifest, otherwise
    /// the folder layout &lt;slicer&gt;\[&lt;set&gt;\]&lt;folder&gt; of the built-in slicers.
    /// </summary>
    public List<ProfileMapping> MappingsFromRepository(string repoPath)
    {
        var manifest = RepoManifest.Read(repoPath);
        if (manifest != null && manifest.Folders.Count > 0)
        {
            return manifest.Folders
                .Where(f => f.Slicer == SlicerKind.Custom ? !string.IsNullOrEmpty(f.Path) : SlicerCatalog.Find(f.Slicer) != null)
                .Select(f => f.Slicer == SlicerKind.Custom
                    ? CreateCustomMapping(f.Folder, FromPathTemplate(f.Path))
                    : CreateMapping(f.Slicer, f.ProfileSet, f.Folder))
                .ToList();
        }

        var result = new List<ProfileMapping>();
        foreach (var d in SlicerCatalog.All)
        {
            var dir = Path.Combine(repoPath, d.RepoFolder);
            if (!Directory.Exists(dir))
                continue;
            var sets = d.Layout == ProfileLayout.UserSets
                ? Directory.EnumerateDirectories(dir).Select(Path.GetFileName).ToList()
                : new List<string?> { "" };
            foreach (var set in sets)
                foreach (var folder in d.Folders.Where(f => Directory.Exists(Path.Combine(dir, set!, f))))
                    result.Add(CreateMapping(d.Kind, set!, folder));
        }
        return result;
    }
}
