using Microsoft.Win32;

namespace SlicerSync.Core.Slicers;

/// <summary>Reads the Windows "installed apps" list (the uninstall entries in the registry).</summary>
public static class InstalledApps
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IEnumerable<string> DisplayNames()
    {
        var names = new List<string>();
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Default),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                if (uninstall == null)
                    continue;
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(sub);
                    if (app?.GetValue("DisplayName") is string name && name.Length > 0)
                        names.Add(name);
                }
            }
            catch (Exception)
            {
                // Missing permissions etc.: detection is informational only.
            }
        }
        return names;
    }
}
