using Microsoft.Win32;

namespace SlicerSync.Core.Git;

/// <summary>
/// Sucht git.exe. Neben PATH werden die üblichen Installationsorte und die Registry geprüft,
/// weil eine gerade erst (vom Installer) installierte Git-Version noch nicht im PATH des
/// laufenden Prozesses steht.
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
            catch (Exception) { /* keine Berechtigung o. Ä. */ }
            if (install != null)
                yield return Path.Combine(install, "cmd", "git.exe");
        }

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe");
    }
}
