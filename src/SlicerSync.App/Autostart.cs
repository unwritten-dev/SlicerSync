using Microsoft.Win32;

namespace SlicerSync.App;

/// <summary>Autostart via HKCU\...\Run (no admin rights needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "SlicerSync";
    public const string Argument = "--autostart";

    private static string? Command => Environment.ProcessPath is { } exe ? $"\"{exe}\" {Argument}" : null;

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Command is { } command)
            key.SetValue(ValueName, command);
        else if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Re-creates the entry if it is missing or points to another EXE (e.g. after moving the
    /// portable version). Returns true if something was repaired.
    /// </summary>
    public static bool Repair()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (Command is { } command && !string.Equals(key?.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
        {
            Apply(true);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the user switched SlicerSync off under Task Manager → Startup apps (or in the Windows
    /// settings). Windows then ignores the Run entry; only the user can switch it back on there.
    /// </summary>
    public static bool IsDisabledByWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        // First byte: 0x02/0x06 = enabled, 0x03/0x07 (odd) = disabled.
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
    }
}
