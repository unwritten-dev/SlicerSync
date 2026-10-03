using Microsoft.Win32;

namespace SlicerSync.Core.Git;

/// <summary>
/// Locates git.exe. Besides PATH it checks the usual install locations and the registry, because
/// a Git that the installer has just installed is not yet on the PATH of the running process.
/// </summary>
public static class GitLocator
{
    public const string DownloadUrl = "https://git-scm.com/download/win";

    public static string? Find(string? configuredPath = null)
    {
        foreach (var candidate in Candidates(configuredPath))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static IEnumerable<string?> Candidates(string? configuredPath)
    {
        yield return configuredPath;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string? file = null;
            try { file = Path.Combine(dir.Trim('"'), "git.exe"); } catch (ArgumentException) { }
            yield return file;
        }

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            string? install = null;
            try
            {
                using var key = root.OpenSubKey(@"SOFTWARE\GitForWindows");
                install = key?.GetValue("InstallPath") as string;
            }
            catch (Exception) { /* no permission etc. */ }
            if (install != null)
                yield return Path.Combine(install, "cmd", "git.exe");
        }

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe");
    }
}
