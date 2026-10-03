using System.Text.Json.Serialization;

namespace SlicerSync.Core.Config;

public enum SyncMode
{
    /// <summary>Nur über „Jetzt synchronisieren“.</summary>
    Manual,
    /// <summary>Alle <see cref="AppSettings.IntervalMinutes"/> Minuten.</summary>
    Interval,
    /// <summary>Einmal täglich zu <see cref="AppSettings.DailyTime"/>.</summary>
    Daily,
    /// <summary>Kurz nach einer Dateiänderung (entprellt).</summary>
    OnChange,
}

public static class SlicerKind
{
    public const string Orca = "Orca";
    public const string Prusa = "Prusa";
}

/// <summary>Ein Profil-Unterordner, der per Junction ins Repository verlinkt ist.</summary>
public sealed class ProfileMapping
{
    /// <summary><see cref="SlicerKind.Orca"/> oder <see cref="SlicerKind.Prusa"/>.</summary>
    public string Slicer { get; set; } = "";

    /// <summary>Orca: Benutzer-Unterordner („default“ oder Nutzer-ID). Prusa: leer.</summary>
    public string ProfileSet { get; set; } = "";

    /// <summary>Unterordner, z. B. „filament“.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Ursprünglicher Speicherort (hier liegt nach der Einrichtung die Junction).</summary>
    public string OriginalPath { get; set; } = "";

    /// <summary>Pfad relativ zum Repository, z. B. „Orca\default\filament“.</summary>
    public string RepoRelativePath { get; set; } = "";

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string DisplayName => Slicer == SlicerKind.Orca
        ? $"Orca Slicer – {ProfileSet}\\{Folder}"
        : $"PrusaSlicer – {Folder}";

    [JsonIgnore]
    public bool MayContainSecrets =>
        Slicer == SlicerKind.Prusa && Folder.Equals("physical_printer", StringComparison.OrdinalIgnoreCase);

    public string RepoTarget(string repoPath) => Path.Combine(repoPath, RepoRelativePath);

    public ProfileMapping Clone() => (ProfileMapping)MemberwiseClone();

    public bool SameFolderAs(ProfileMapping other) =>
        string.Equals(RepoRelativePath, other.RepoRelativePath, StringComparison.OrdinalIgnoreCase);
}

public sealed class AppSettings
{
    public const string DefaultCommitTemplate = "Profil-Sync {datum} {zeit} von {rechner} ({anzahl} Dateien)";

    public bool SetupCompleted { get; set; }

    public string RepoPath { get; set; } = "";
    public string RemoteUrl { get; set; } = "";
    public string Branch { get; set; } = "main";

    public List<ProfileMapping> Mappings { get; set; } = new();

    public SyncMode SyncMode { get; set; } = SyncMode.OnChange;
    public int IntervalMinutes { get; set; } = 60;
    /// <summary>Uhrzeit im Format HH:mm.</summary>
    public string DailyTime { get; set; } = "18:00";
    public int DebounceSeconds { get; set; } = 30;
    public bool SyncOnStartup { get; set; } = true;

    public bool Autostart { get; set; } = true;
    public bool NotifyOnSuccess { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;

    public string CommitMessageTemplate { get; set; } = DefaultCommitTemplate;

    /// <summary>Optional: fester Pfad zu git.exe. Leer = automatisch suchen.</summary>
    public string GitPath { get; set; } = "";

    [JsonIgnore]
    public IEnumerable<ProfileMapping> EnabledMappings => Mappings.Where(m => m.Enabled);

    /// <summary>Tiefe Kopie (Formulare bearbeiten eine Kopie, übernommen wird erst bei OK).</summary>
    public AppSettings Clone() => SettingsStore.Deserialize<AppSettings>(SettingsStore.Serialize(this))!;

    public TimeSpan GetDailyTime() =>
        TimeSpan.TryParse(DailyTime, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t
            : new TimeSpan(18, 0, 0);
}
