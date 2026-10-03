namespace SlicerSync.Core;

/// <summary>
/// All base directories used by the app. Tests pass temporary folders here so real profile
/// or settings folders are never touched.
/// </summary>
public sealed record AppPaths(
    string RoamingAppData,
    string LocalAppData,
    string UserProfile,
    IReadOnlyList<string> ProgramFilesDirs)
{
    public string SettingsDir => Path.Combine(RoamingAppData, "SlicerSync");
    public string SettingsFile => Path.Combine(SettingsDir, "settings.json");
    public string DataDir => Path.Combine(LocalAppData, "SlicerSync");
    public string LogFile => Path.Combine(DataDir, "slicersync.log");
    public string BackupDir => Path.Combine(DataDir, "backups");
    public string DefaultRepoPath => Path.Combine(UserProfile, "SlicerProfiles");

    public static AppPaths FromEnvironment()
    {
        var programFiles = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AppPaths(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            programFiles);
    }
}
