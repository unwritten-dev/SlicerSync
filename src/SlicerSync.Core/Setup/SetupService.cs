using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Slicers;

namespace SlicerSync.Core.Setup;

public enum MappingState
{
    /// <summary>The original folder is a regular folder (not set up yet).</summary>
    NotLinked,
    /// <summary>The original location is a junction to the repository folder.</summary>
    Linked,
    /// <summary>The original location is a junction to somewhere else.</summary>
    ForeignJunction,
    /// <summary>Nothing exists at the original location.</summary>
    Missing,
}

public sealed class SetupException : Exception
{
    public SetupException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class SetupResult
{
    public string? BackupZip { get; set; }
    public bool Pushed { get; set; }
    public int LinkedFolders { get; set; }
    public int SkippedFolders { get; set; }
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Setup (new / second PC), adding and removing single folders, and uninstall (restore).
/// Ground rule: before anything is deleted or replaced there is a verified ZIP backup.
/// </summary>
public sealed class SetupService
{
    internal const string AsideSuffix = ".slicersync-old";

    private readonly GitRunner _git;
    private readonly BackupService _backups;
    private readonly ProcessGuard _guard;
    private readonly SlicerDetector _detector;
    private readonly ILogger _log;

    public SetupService(GitRunner git, BackupService backups, ProcessGuard guard, SlicerDetector detector, ILogger log)
    {
        _git = git;
        _backups = backups;
        _guard = guard;
        _detector = detector;
        _log = log;
    }

    public static MappingState GetState(ProfileMapping m, string repoPath)
    {
        if (Junction.IsJunction(m.OriginalPath))
            return Junction.PointsTo(m.OriginalPath, m.RepoTarget(repoPath)) ? MappingState.Linked : MappingState.ForeignJunction;
        if (Directory.Exists(m.OriginalPath))
            return MappingState.NotLinked;
        return MappingState.Missing;
    }

    /// <summary>
    /// Checks a folder the user wants to add as a custom folder. Returns an error message or null.
    /// </summary>
    public static string? ValidateCustomFolder(string name, string path, string repoPath, IEnumerable<ProfileMapping> existing)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60 ||
            !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') || name is "." or "..")
            return Loc.T(
                "Please use a short name made of letters (no umlauts), digits, '-', '_' or '.'.",
                "Bitte einen kurzen Namen aus Buchstaben (ohne Umlaute), Ziffern, „-“, „_“ oder „.“ angeben.");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            return Loc.T("The folder does not exist.", "Der Ordner existiert nicht.");

        var full = Path.GetFullPath(path).TrimEnd('\\');
        if (Path.GetPathRoot(full)!.TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase))
            return Loc.T("A whole drive cannot be backed up.", "Ein ganzes Laufwerk kann nicht gesichert werden.");
        if (Junction.IsJunction(full))
            return Loc.T("This folder is already a link (junction).", "Dieser Ordner ist bereits eine Verknüpfung (Junction).");

        var repo = Path.GetFullPath(repoPath).TrimEnd('\\');
        if (IsSameOrInside(full, repo) || IsSameOrInside(repo, full))
            return Loc.T("The folder must not contain or be inside the repository folder.",
                         "Der Ordner darf den Repository-Ordner weder enthalten noch darin liegen.");

        var special = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        if (special.Any(s => !string.IsNullOrEmpty(s) && Junction.PathsEqual(s, full)))
            return Loc.T("Please choose the program's profile folder, not a system or user folder.",
                         "Bitte den Profilordner des Programms wählen, nicht einen System- oder Benutzerordner.");

        foreach (var m in existing.Where(m => m.Enabled))
        {
            if (m.IsCustom && string.Equals(m.Folder, name, StringComparison.OrdinalIgnoreCase))
                return Loc.T("A custom folder with this name already exists.", "Es gibt bereits einen eigenen Ordner mit diesem Namen.");
            var other = Path.GetFullPath(m.OriginalPath).TrimEnd('\\');
            if (IsSameOrInside(full, other) || IsSameOrInside(other, full))
                return Loc.T($"Overlaps with a folder that is already backed up: {m.DisplayName}",
                             $"Überschneidet sich mit einem bereits gesicherten Ordner: {m.DisplayName}");
        }
        return null;
    }

    private static bool IsSameOrInside(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- New setup

    /// <summary>
    /// First setup on the PC that has the profiles: ZIP backup, move folders into the repository,
    /// create junctions, git init, first commit, push.
    /// </summary>
    /// <param name="createRemoteOnPush">
    /// true if the remote does not exist yet and is created on the first push (GitLab "push to create").
    /// An unreachable remote is then not an error during the pre-check.
    /// </param>
    public SetupResult RunNewSetup(AppSettings settings, IReadOnlyList<ProfileMapping> selected, bool createRemoteOnPush = false)
    {
        ValidateBasics(settings);
        if (selected.Count == 0)
            throw new SetupException(Loc.T("No profile folder selected.", "Es ist kein Profilordner ausgewählt."));
        _guard.EnsureSlicersClosed(selected);
        CheckOverlaps(selected, LinkedMappings(settings));

        var repo = new GitRepository(_git, settings.RepoPath);
        var hasRemote = !string.IsNullOrWhiteSpace(settings.RemoteUrl);

        // Check the remote before touching any file (also triggers the sign-in if needed).
        if (hasRemote)
        {
            var probe = _git.Run(null, "ls-remote", "--heads", settings.RemoteUrl, settings.Branch);
            if (!probe.Success && createRemoteOnPush)
                _log.Info(Loc.T("Remote does not exist yet; it will be created on the first upload.",
                                "Remote existiert noch nicht und wird beim ersten Hochladen angelegt."));
            else if (!probe.Success)
                throw new SetupException(Loc.T(
                    $"The remote repository is not reachable:\n{probe.ErrorText}",
                    $"Das Remote-Repository ist nicht erreichbar:\n{probe.ErrorText}"));
            else if (probe.StdOut.Trim().Length > 0 && !repo.Exists)
                throw new SetupException(Loc.T(
                    "The remote repository already contains profiles. Please choose \"Second PC: clone existing repository\" " +
                    "or use an empty repository.",
                    "Das Remote-Repository enthält bereits Profile. Bitte „Zweiter PC: bestehendes Repository klonen“ wählen " +
                    "oder ein leeres Repository angeben."));
        }

        if (!repo.Exists && Directory.Exists(settings.RepoPath) && !DirectoryCopy.IsEmpty(settings.RepoPath))
        {
            // Only our own top-level folders may already be there (e.g. after an uninstall without .git).
            var known = SlicerCatalog.All.Select(d => d.RepoFolder).Append(SlicerKind.Custom);
            var unexpected = Directory.EnumerateFileSystemEntries(settings.RepoPath)
                .Select(Path.GetFileName)
                .Where(n => !known.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unexpected.Count > 0)
                throw new SetupException(Loc.T(
                    $"The repository folder is not empty and not a Git repository: {settings.RepoPath}",
                    $"Der Repository-Ordner ist nicht leer und kein Git-Repository: {settings.RepoPath}"));
        }

        var result = new SetupResult();
        var states = selected.ToDictionary(m => m, m => GetState(m, settings.RepoPath));
        CheckForeignJunctions(states);

        var toLink = selected.Where(m => states[m] != MappingState.Linked).ToList();
        result.SkippedFolders = selected.Count - toLink.Count;
        foreach (var m in selected.Where(m => states[m] == MappingState.Linked))
            _log.Info(Loc.T($"Already set up, skipping: {m.DisplayName}", $"Bereits eingerichtet, wird übersprungen: {m.DisplayName}"));

        result.BackupZip = BackupBeforeLink(toLink, settings.RepoPath, "setup", states);

        if (!repo.Exists)
        {
            _log.Info(Loc.T($"Creating Git repository in {settings.RepoPath}", $"Erstelle Git-Repository in {settings.RepoPath}"));
            repo.Init(settings.Branch);
        }
        else
        {
            repo.EnsureBranch(settings.Branch);
        }
        WriteGitAttributes(settings.RepoPath);
        repo.EnsureIdentity();

        foreach (var m in toLink)
        {
            LinkCore(m, settings.RepoPath, states[m], targetWins: false);
            result.LinkedFolders++;
        }

        RepoManifest.AddFolders(settings.RepoPath, selected, _detector);
        repo.SetRemote(settings.RemoteUrl);

        repo.StageAll();
        var files = repo.StagedFiles();
        if (files.Count > 0)
            repo.Commit(Loc.T(
                $"Initial setup from {Environment.MachineName} ({files.Count} files)",
                $"Ersteinrichtung von {Environment.MachineName} ({files.Count} Dateien)"));

        if (hasRemote)
        {
            try
            {
                repo.Push(settings.Branch);
                result.Pushed = true;
            }
            catch (GitException ex)
            {
                result.Warnings.Add(Loc.T(
                    $"Upload failed – it will be retried on the next sync: {ex.Message}",
                    $"Hochladen fehlgeschlagen – wird beim nächsten Sync erneut versucht: {ex.Message}"));
                _log.Warn(Loc.T("Push during setup failed", "Push bei der Einrichtung fehlgeschlagen"), ex);
            }
        }

        MergeMappings(settings, selected);
        settings.SetupCompleted = true;
        _log.Info(Loc.T(
            $"Setup complete: {result.LinkedFolders} folders linked, {result.SkippedFolders} skipped.",
            $"Einrichtung abgeschlossen: {result.LinkedFolders} Ordner verlinkt, {result.SkippedFolders} übersprungen."));
        return result;
    }

    // ---------------------------------------------------------------- Second PC

    /// <summary>
    /// Second PC: clone the existing repository, back up local profile folders as ZIP and
    /// replace them with junctions to the repository.
    /// </summary>
    public SetupResult RunCloneSetup(AppSettings settings)
    {
        ValidateBasics(settings);
        if (string.IsNullOrWhiteSpace(settings.RemoteUrl))
            throw new SetupException(Loc.T("The second-PC mode needs the remote URL.", "Für den Zweiter-PC-Modus wird die Remote-URL benötigt."));
        _guard.EnsureSlicersClosed();

        var repo = new GitRepository(_git, settings.RepoPath);
        if (repo.Exists)
        {
            if (!string.Equals(repo.GetRemoteUrl(), settings.RemoteUrl, StringComparison.OrdinalIgnoreCase))
                throw new SetupException(Loc.T(
                    $"The folder {settings.RepoPath} already contains a different repository.",
                    $"Im Ordner {settings.RepoPath} liegt bereits ein anderes Repository."));
            _log.Info(Loc.T("Repository is already cloned, fetching the latest state.", "Repository ist bereits geklont, hole aktuellen Stand."));
            if (repo.PullRebase(settings.Branch) == PullOutcome.Conflict)
                throw new SetupException(Loc.T(
                    "Updating the existing repository caused a conflict.",
                    "Beim Aktualisieren des vorhandenen Repositorys gab es einen Konflikt."));
        }
        else
        {
            if (!DirectoryCopy.IsEmpty(settings.RepoPath))
                throw new SetupException(Loc.T(
                    $"The repository folder is not empty: {settings.RepoPath}",
                    $"Der Repository-Ordner ist nicht leer: {settings.RepoPath}"));
            _log.Info(Loc.T($"Cloning {settings.RemoteUrl} to {settings.RepoPath}", $"Klone {settings.RemoteUrl} nach {settings.RepoPath}"));
            try
            {
                repo = GitRepository.Clone(_git, settings.RemoteUrl, settings.RepoPath, settings.Branch);
            }
            catch (GitException ex)
            {
                throw new SetupException(ex.Message, ex);
            }
        }

        var mappings = _detector.MappingsFromRepository(settings.RepoPath);
        if (mappings.Count == 0)
            throw new SetupException(Loc.T(
                "The repository contains no slicer profiles. Please run \"New setup\" on the first PC first.",
                "Das Repository enthält keine Slicer-Profile. Bitte zuerst auf dem ersten PC „Neu einrichten“ ausführen."));
        // Custom folders are only known now (from the manifest): check their programs too.
        _guard.EnsureSlicersClosed(mappings);
        CheckOverlaps(mappings, Array.Empty<ProfileMapping>());

        var result = new SetupResult();
        var states = mappings.ToDictionary(m => m, m => GetState(m, settings.RepoPath));
        CheckForeignJunctions(states);

        var toLink = mappings.Where(m => states[m] != MappingState.Linked).ToList();
        result.SkippedFolders = mappings.Count - toLink.Count;

        // Back up the local originals only; the repository state wins.
        var backupItems = toLink
            .Where(m => states[m] == MappingState.NotLinked)
            .Select(m => (m.OriginalPath, $"local/{m.RepoRelativePath}"))
            .ToList();
        if (backupItems.Count > 0)
            result.BackupZip = _backups.CreateBackup("second-pc", backupItems);

        foreach (var m in toLink)
        {
            LinkCore(m, settings.RepoPath, states[m], targetWins: true);
            result.LinkedFolders++;
        }

        repo.EnsureIdentity();
        MergeMappings(settings, mappings);
        settings.SetupCompleted = true;
        _log.Info(Loc.T(
            $"Second PC set up: {result.LinkedFolders} folders linked, {result.SkippedFolders} skipped.",
            $"Zweiter PC eingerichtet: {result.LinkedFolders} Ordner verlinkt, {result.SkippedFolders} übersprungen."));
        return result;
    }

    // ---------------------------------------------------------------- Add / remove folders

    /// <summary>Adds more profile folders to the repository later on.</summary>
    public SetupResult AddMappings(AppSettings settings, IReadOnlyList<ProfileMapping> mappings)
    {
        ValidateBasics(settings);
        _guard.EnsureSlicersClosed(mappings);
        CheckOverlaps(mappings, LinkedMappings(settings));

        var result = new SetupResult();
        var states = mappings.ToDictionary(m => m, m => GetState(m, settings.RepoPath));
        CheckForeignJunctions(states);
        var toLink = mappings.Where(m => states[m] != MappingState.Linked).ToList();

        result.BackupZip = BackupBeforeLink(toLink, settings.RepoPath, "add-folders", states);
        foreach (var m in toLink)
        {
            LinkCore(m, settings.RepoPath, states[m], targetWins: false);
            result.LinkedFolders++;
        }
        RepoManifest.AddFolders(settings.RepoPath, mappings, _detector);
        MergeMappings(settings, mappings);
        return result;
    }

    /// <summary>
    /// Stops backing up single folders: removes the junction and copies the content back to the
    /// original location. The state in the repository is kept.
    /// </summary>
    public void RemoveMappings(AppSettings settings, IReadOnlyList<ProfileMapping> mappings)
    {
        _guard.EnsureSlicersClosed(mappings);
        foreach (var m in mappings)
        {
            Unlink(m, settings.RepoPath);
            var stored = settings.Mappings.FirstOrDefault(x => x.SameFolderAs(m));
            if (stored != null)
                stored.Enabled = false;
        }
        RepoManifest.RemoveFolders(settings.RepoPath, mappings);
    }

    /// <summary>
    /// Refuses folders that lie inside each other (or inside an already linked folder): linking them
    /// would create a junction inside another junction, i.e. inside the repository itself.
    /// </summary>
    private static void CheckOverlaps(IReadOnlyList<ProfileMapping> toAdd, IEnumerable<ProfileMapping> existing)
    {
        var others = existing.Where(e => !toAdd.Any(a => a.SameFolderAs(e))).ToList();
        for (var i = 0; i < toAdd.Count; i++)
        {
            var path = Path.GetFullPath(toAdd[i].OriginalPath).TrimEnd('\\');
            var conflict = toAdd.Where((_, j) => j != i).Concat(others).FirstOrDefault(o =>
            {
                var other = Path.GetFullPath(o.OriginalPath).TrimEnd('\\');
                return IsSameOrInside(path, other) || IsSameOrInside(other, path);
            });
            if (conflict != null)
                throw new SetupException(Loc.T(
                    $"{toAdd[i].DisplayName} overlaps with {conflict.DisplayName}. One folder must not be inside the other – " +
                    "please stop backing up one of them first.",
                    $"{toAdd[i].DisplayName} überschneidet sich mit {conflict.DisplayName}. Ein Ordner darf nicht im anderen liegen – " +
                    "bitte zuerst einen der beiden nicht mehr sichern."));
        }
    }

    /// <summary>Folders of this PC that are currently backed up (enabled and linked).</summary>
    private static IEnumerable<ProfileMapping> LinkedMappings(AppSettings settings) =>
        settings.EnabledMappings.Where(m => GetState(m, settings.RepoPath) == MappingState.Linked);

    // ---------------------------------------------------------------- Uninstall

    /// <summary>
    /// Restore: removes all junctions and puts the folders back at their original location.
    /// The repository (and with it the history) is left untouched.
    /// </summary>
    public int Uninstall(AppSettings settings)
    {
        _guard.EnsureSlicersClosed(settings.Mappings);
        var count = 0;
        foreach (var m in settings.Mappings)
        {
            if (GetState(m, settings.RepoPath) != MappingState.Linked)
                continue;
            Unlink(m, settings.RepoPath);
            count++;
        }
        settings.SetupCompleted = false;
        _log.Info(Loc.T(
            $"Restore complete: {count} folders put back. The repository stays at {settings.RepoPath}.",
            $"Rückbau abgeschlossen: {count} Ordner zurückgelegt. Repository bleibt unter {settings.RepoPath} erhalten."));
        return count;
    }

    // ---------------------------------------------------------------- Core operations

    private void Unlink(ProfileMapping m, string repoPath)
    {
        var state = GetState(m, repoPath);
        if (state != MappingState.Linked)
        {
            _log.Warn(Loc.T(
                $"{m.DisplayName}: no junction to the repository, nothing to restore.",
                $"{m.DisplayName}: keine Junction auf das Repository vorhanden, nichts zurückzubauen."));
            return;
        }

        var target = m.RepoTarget(repoPath);
        Junction.Remove(m.OriginalPath);
        try
        {
            DirectoryCopy.Copy(target, m.OriginalPath);
            if (!DirectoryCopy.IsCopyComplete(target, m.OriginalPath))
                throw new IOException(Loc.T("Copy incomplete.", "Kopie unvollständig."));
        }
        catch (Exception ex)
        {
            // Remove the incomplete copy (only our own files) and restore the junction.
            if (Directory.Exists(m.OriginalPath) && !Junction.IsJunction(m.OriginalPath))
                Directory.Delete(m.OriginalPath, recursive: true);
            Junction.Create(m.OriginalPath, target);
            throw new SetupException(Loc.T(
                $"{m.DisplayName} could not be restored: {ex.Message}",
                $"{m.DisplayName} konnte nicht zurückgelegt werden: {ex.Message}"), ex);
        }
        _log.Info(Loc.T($"Restored: {m.DisplayName} → {m.OriginalPath}", $"Zurückgelegt: {m.DisplayName} → {m.OriginalPath}"));
    }

    /// <summary>
    /// Links a folder. <paramref name="targetWins"/> = false: the local folder is moved into the
    /// repository. true: the repository state stays and the local folder is replaced.
    /// In both cases a backup must have been made beforehand.
    /// </summary>
    private void LinkCore(ProfileMapping m, string repoPath, MappingState state, bool targetWins)
    {
        var original = m.OriginalPath;
        var target = m.RepoTarget(repoPath);
        var aside = original + AsideSuffix;

        if (state == MappingState.Linked)
            return;
        if (state == MappingState.ForeignJunction)
            throw new SetupException(Loc.T(
                $"{original} is already linked to a different location.",
                $"{original} ist bereits mit einem anderen Ort verknüpft."));
        if (Directory.Exists(aside))
            throw new SetupException(Loc.T(
                $"A folder from an interrupted operation is still there: {aside}. Please check and remove it.",
                $"Es liegt noch ein Ordner aus einem abgebrochenen Vorgang vor: {aside}. Bitte prüfen und entfernen."));

        if (state == MappingState.Missing)
        {
            Directory.CreateDirectory(target);
            Junction.Create(original, target);
            _log.Info(Loc.T($"Linked (newly created): {m.DisplayName}", $"Verlinkt (neu angelegt): {m.DisplayName}"));
            return;
        }

        if (!targetWins)
        {
            if (Directory.Exists(target) && !DirectoryCopy.IsEmpty(target))
                Directory.Delete(target, recursive: true); // backed up before (BackupBeforeLink)
            DirectoryCopy.Copy(original, target);
            if (!DirectoryCopy.IsCopyComplete(original, target))
                throw new SetupException(Loc.T(
                    $"Copying {original} to {target} was incomplete. Nothing was changed.",
                    $"Kopieren von {original} nach {target} war unvollständig. Es wurde nichts verändert."));
        }
        else
        {
            Directory.CreateDirectory(target);
        }

        // Move the original aside (same drive → atomic), create the junction, only then delete.
        Directory.Move(original, aside);
        try
        {
            Junction.Create(original, target);
        }
        catch (Exception ex)
        {
            if (Junction.IsJunction(original))
                Junction.Remove(original);
            Directory.Move(aside, original);
            throw new SetupException(Loc.T(
                $"Could not create the junction for {m.DisplayName}: {ex.Message}",
                $"Junction für {m.DisplayName} konnte nicht angelegt werden: {ex.Message}"), ex);
        }
        Directory.Delete(aside, recursive: true);
        _log.Info(Loc.T($"Linked: {original} → {target}", $"Verlinkt: {original} → {target}"));
    }

    private string? BackupBeforeLink(
        IReadOnlyList<ProfileMapping> toLink, string repoPath, string label, IReadOnlyDictionary<ProfileMapping, MappingState> states)
    {
        var items = new List<(string, string)>();
        foreach (var m in toLink)
        {
            if (states[m] == MappingState.NotLinked)
                items.Add((m.OriginalPath, $"original/{m.RepoRelativePath}"));
            var target = m.RepoTarget(repoPath);
            if (states[m] == MappingState.NotLinked && Directory.Exists(target) && !DirectoryCopy.IsEmpty(target))
                items.Add((target, $"repository/{m.RepoRelativePath}"));
        }
        return items.Count > 0 ? _backups.CreateBackup(label, items) : null;
    }

    private static void CheckForeignJunctions(IReadOnlyDictionary<ProfileMapping, MappingState> states)
    {
        var foreign = states.Where(s => s.Value == MappingState.ForeignJunction).Select(s => s.Key.OriginalPath).ToList();
        if (foreign.Count > 0)
            throw new SetupException(Loc.T(
                "These folders are already linked to a different location:\n",
                "Diese Ordner sind bereits mit einem anderen Ort verknüpft:\n") + string.Join("\n", foreign));
    }

    private static void ValidateBasics(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RepoPath))
            throw new SetupException(Loc.T("No repository folder specified.", "Kein Repository-Ordner angegeben."));
        if (!Path.IsPathFullyQualified(settings.RepoPath))
            throw new SetupException(Loc.T("The repository folder must be a full path.", "Der Repository-Ordner muss ein vollständiger Pfad sein."));
        if (string.IsNullOrWhiteSpace(settings.Branch))
            throw new SetupException(Loc.T("No branch specified.", "Kein Branch angegeben."));
    }

    private static void WriteGitAttributes(string repoPath)
    {
        // Store profile files byte for byte (no CRLF/LF conversion).
        var file = Path.Combine(repoPath, ".gitattributes");
        if (!File.Exists(file))
            File.WriteAllText(file, "* -text\n");
    }

    private static void MergeMappings(AppSettings settings, IEnumerable<ProfileMapping> mappings)
    {
        foreach (var m in mappings)
        {
            var existing = settings.Mappings.FirstOrDefault(x => x.SameFolderAs(m));
            if (existing != null)
                settings.Mappings.Remove(existing);
            var copy = m.Clone();
            copy.Enabled = true;
            settings.Mappings.Add(copy);
        }
    }
}
