using System.Text.Json.Serialization;
using SlicerSync.Core.Slicers;

namespace SlicerSync.Core.Config;

public enum SyncMode
{
    /// <summary>Only via "Sync now".</summary>
    Manual,
    /// <summary>Every <see cref="AppSettings.IntervalMinutes"/> minutes.</summary>
    Interval,
    /// <summary>Once a day at <see cref="AppSettings.DailyTime"/>.</summary>
    Daily,
    /// <summary>Shortly after a file change (debounced).</summary>
    OnChange,
}


/// <summary>A profile subfolder that is linked into the repository via a junction.</summary>
public sealed class ProfileMapping
{
    /// <summary>A <see cref="SlicerKind"/> value.</summary>
    public string Slicer { get; set; } = "";

    /// <summary>Orca/Bambu: user subfolder ("default" or user ID). Otherwise empty.</summary>
    public string ProfileSet { get; set; } = "";

    /// <summary>Subfolder, e.g. "filament". For custom folders: the name the user gave it.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Original location (holds the junction once set up).</summary>
    public string OriginalPath { get; set; } = "";

    /// <summary>Path relative to the repository, e.g. "Orca\default\filament".</summary>
    public string RepoRelativePath { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Custom folders only: process name of the program (EXE file name without ".exe"), so SlicerSync
    /// can check whether it is running. Empty = unknown.
    /// </summary>
    public string ProcessName { get; set; } = "";

    [JsonIgnore]
    public bool IsCustom => Slicer == SlicerKind.Custom;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (IsCustom)
                return $"{Loc.T("Custom folder", "Eigener Ordner")} – {Folder} ({OriginalPath})";
            var name = SlicerCatalog.Find(Slicer)?.DisplayName ?? Slicer;
            return string.IsNullOrEmpty(ProfileSet) ? $"{name} – {Folder}" : $"{name} – {ProfileSet}\\{Folder}";
        }
    }

    [JsonIgnore]
    public bool MayContainSecrets =>
        SlicerCatalog.Find(Slicer)?.SecretFolders.Contains(Folder, StringComparer.OrdinalIgnoreCase) == true;

    public string RepoTarget(string repoPath) => Path.Combine(repoPath, RepoRelativePath);

    public ProfileMapping Clone() => (ProfileMapping)MemberwiseClone();

    public bool SameFolderAs(ProfileMapping other) =>
        string.Equals(RepoRelativePath, other.RepoRelativePath, StringComparison.OrdinalIgnoreCase);
}

public sealed class AppSettings
{
    public bool SetupCompleted { get; set; }

    /// <summary>UI language: "en" (default) or "de".</summary>
    public string Language { get; set; } = Loc.English;

    public string RepoPath { get; set; } = "";
    public string RemoteUrl { get; set; } = "";
    public string Branch { get; set; } = "main";

    public List<ProfileMapping> Mappings { get; set; } = new();

    public SyncMode SyncMode { get; set; } = SyncMode.OnChange;
    public int IntervalMinutes { get; set; } = 60;
    /// <summary>Time of day as HH:mm.</summary>
    public string DailyTime { get; set; } = "18:00";
    public int DebounceSeconds { get; set; } = 30;
    public bool SyncOnStartup { get; set; } = true;

    public bool Autostart { get; set; } = true;
    public bool NotifyOnSuccess { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;

    /// <summary>Commit message template. Empty = default of the current language.</summary>
    public string CommitMessageTemplate { get; set; } = "";

    /// <summary>Optional fixed path to git.exe. Empty = locate automatically.</summary>
    public string GitPath { get; set; } = "";

    [JsonIgnore]
    public IEnumerable<ProfileMapping> EnabledMappings => Mappings.Where(m => m.Enabled);

    /// <summary>Deep copy (forms edit a copy; it is applied only on OK).</summary>
    public AppSettings Clone() => SettingsStore.Deserialize<AppSettings>(SettingsStore.Serialize(this))!;

    public TimeSpan GetDailyTime() =>
        TimeSpan.TryParse(DailyTime, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t
            : new TimeSpan(18, 0, 0);
}
