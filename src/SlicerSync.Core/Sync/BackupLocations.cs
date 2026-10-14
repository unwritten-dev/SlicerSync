using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.Sync;

/// <summary>
/// Additional backup locations: a complete copy of the repository (with history) in a folder on a NAS,
/// a USB drive or a cloud folder. Every PC writes to its own repository there
/// (SlicerProfiles-&lt;computer&gt;.git), so several PCs can share one location without interfering.
/// </summary>
public static class BackupLocations
{
    /// <summary>The repository of this PC inside a backup location.</summary>
    public static string RepositoryPath(string location, string machine) =>
        Path.Combine(location, $"SlicerProfiles-{machine}.git");

    /// <summary>Checks a location chosen by the user; returns an error message or null.</summary>
    public static string? Validate(string location, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
            return Loc.T("Please choose a complete folder path.", "Bitte einen vollständigen Ordnerpfad wählen.");
        var full = Path.GetFullPath(location).TrimEnd('\\');
        var repo = Path.GetFullPath(repoPath).TrimEnd('\\');
        if (full.Equals(repo, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(repo + "\\", StringComparison.OrdinalIgnoreCase) ||
            repo.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
            return Loc.T("The backup location must not contain or be inside the repository folder.",
                         "Der Sicherungsort darf den Repository-Ordner weder enthalten noch darin liegen.");
        if (!Directory.Exists(location))
            return Loc.T("The folder is not reachable.", "Der Ordner ist nicht erreichbar.");
        return null;
    }

    /// <summary>
    /// Pushes the current state with its complete history to every location. Unreachable locations
    /// (USB drive unplugged, NAS switched off) are reported as notes, not as errors.
    /// </summary>
    public static List<string> PushAll(GitRunner git, GitRepository repo, IEnumerable<string> locations, string branch, ILogger log)
    {
        var notes = new List<string>();
        if (repo.HeadHash() == null)
            return notes;

        foreach (var location in locations.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            if (!Directory.Exists(location))
            {
                var msg = Loc.T(
                    $"Backup location not reachable, will be updated on the next sync: {location}",
                    $"Sicherungsort nicht erreichbar, wird beim nächsten Sync nachgeholt: {location}");
                log.Warn(msg);
                notes.Add(msg);
                continue;
            }
            try
            {
                var target = RepositoryPath(location, Environment.MachineName);
                if (!Directory.Exists(target))
                {
                    git.RunChecked(null, "init", "--bare", "--quiet", "-b", branch, GitRepository.ToGitPath(target));
                    log.Info(Loc.T($"Backup repository created: {target}", $"Sicherungs-Repository angelegt: {target}"));
                }
                // Only this PC writes to its repository in the location, so forcing is safe; it keeps
                // the copy identical to this PC even after a rebase of local commits.
                repo.PushTo(GitRepository.ToGitPath(target), branch, force: true);
            }
            catch (Exception ex)
            {
                var msg = Loc.T($"Backup to {location} failed: {ex.Message}", $"Sicherung nach {location} fehlgeschlagen: {ex.Message}");
                log.Warn(msg);
                notes.Add(msg);
            }
        }
        return notes;
    }
}
