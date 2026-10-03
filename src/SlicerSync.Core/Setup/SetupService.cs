using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Slicers;

namespace SlicerSync.Core.Setup;

public enum MappingState
{
    /// <summary>Originalordner ist ein normaler Ordner (noch nicht eingerichtet).</summary>
    NotLinked,
    /// <summary>Originalort ist eine Junction auf den Repository-Ordner.</summary>
    Linked,
    /// <summary>Originalort ist eine Junction auf einen anderen Ort.</summary>
    ForeignJunction,
    /// <summary>Am Originalort gibt es nichts.</summary>
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
/// Einrichtung (neu / zweiter PC), Hinzufügen und Entfernen einzelner Ordner sowie Rückbau.
/// Grundregel: Bevor etwas gelöscht oder ersetzt wird, gibt es ein geprüftes ZIP-Backup.
/// </summary>
public sealed class SetupService
{
    internal const string AsideSuffix = ".slicersync-alt";

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

    // ---------------------------------------------------------------- Neu einrichten

    /// <summary>
    /// Erst-Einrichtung auf dem PC, der die Profile hat: ZIP-Backup, Ordner ins Repository
    /// verschieben, Junctions anlegen, git init, erster Commit, Push.
    /// </summary>
    public SetupResult RunNewSetup(AppSettings settings, IReadOnlyList<ProfileMapping> selected)
    {
        ValidateBasics(settings);
        if (selected.Count == 0)
            throw new SetupException("Es ist kein Profilordner ausgewählt.");
        _guard.EnsureSlicersClosed();

        var repo = new GitRepository(_git, settings.RepoPath);
        var hasRemote = !string.IsNullOrWhiteSpace(settings.RemoteUrl);

        // Remote vor jeder Dateiänderung prüfen (löst ggf. auch die Anmeldung aus).
        if (hasRemote)
        {
            var probe = _git.Run(null, "ls-remote", "--heads", settings.RemoteUrl, settings.Branch);
            if (!probe.Success)
                throw new SetupException($"Das Remote-Repository ist nicht erreichbar:\n{probe.ErrorText}");
            if (probe.StdOut.Trim().Length > 0 && !repo.Exists)
                throw new SetupException(
                    "Das Remote-Repository enthält bereits Profile. Bitte „Zweiter PC: bestehendes Repository klonen“ wählen " +
                    "oder ein leeres Repository angeben.");
        }

        if (!repo.Exists && Directory.Exists(settings.RepoPath) && !DirectoryCopy.IsEmpty(settings.RepoPath))
        {
            var unexpected = Directory.EnumerateFileSystemEntries(settings.RepoPath)
                .Select(Path.GetFileName)
                .Where(n => !string.Equals(n, "Orca", StringComparison.OrdinalIgnoreCase) && !string.Equals(n, "Prusa", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (unexpected.Count > 0)
                throw new SetupException($"Der Repository-Ordner ist nicht leer und kein Git-Repository: {settings.RepoPath}");
        }

        var result = new SetupResult();
        var states = selected.ToDictionary(m => m, m => GetState(m, settings.RepoPath));
        CheckForeignJunctions(states);

        var toLink = selected.Where(m => states[m] != MappingState.Linked).ToList();
        result.SkippedFolders = selected.Count - toLink.Count;
        foreach (var m in selected.Where(m => states[m] == MappingState.Linked))
            _log.Info($"Bereits eingerichtet, wird übersprungen: {m.DisplayName}");

        result.BackupZip = BackupBeforeLink(toLink, settings.RepoPath, "einrichtung", states);

        if (!repo.Exists)
        {
            _log.Info($"Erstelle Git-Repository in {settings.RepoPath}");
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

        RepoManifest.AddFolders(settings.RepoPath, selected);
        repo.SetRemote(settings.RemoteUrl);

        repo.StageAll();
        var files = repo.StagedFiles();
        if (files.Count > 0)
            repo.Commit($"Ersteinrichtung von {Environment.MachineName} ({files.Count} Dateien)");

        if (hasRemote)
        {
            try
            {
                repo.Push(settings.Branch);
                result.Pushed = true;
            }
            catch (GitException ex)
            {
                result.Warnings.Add($"Push fehlgeschlagen – wird beim nächsten Sync erneut versucht: {ex.Message}");
                _log.Warn("Push bei der Einrichtung fehlgeschlagen", ex);
            }
        }

        MergeMappings(settings, selected);
        settings.SetupCompleted = true;
        _log.Info($"Einrichtung abgeschlossen: {result.LinkedFolders} Ordner verlinkt, {result.SkippedFolders} übersprungen.");
        return result;
    }

    // ---------------------------------------------------------------- Zweiter PC

    /// <summary>
    /// Zweiter PC: bestehendes Repository klonen, lokale Profilordner per ZIP sichern und
    /// durch Junctions auf das Repository ersetzen.
    /// </summary>
    public SetupResult RunCloneSetup(AppSettings settings)
    {
        ValidateBasics(settings);
        if (string.IsNullOrWhiteSpace(settings.RemoteUrl))
            throw new SetupException("Für den Zweiter-PC-Modus wird die Remote-URL benötigt.");
        _guard.EnsureSlicersClosed();

        var repo = new GitRepository(_git, settings.RepoPath);
        if (repo.Exists)
        {
            if (!string.Equals(repo.GetRemoteUrl(), settings.RemoteUrl, StringComparison.OrdinalIgnoreCase))
                throw new SetupException($"Im Ordner {settings.RepoPath} liegt bereits ein anderes Repository.");
            _log.Info("Repository ist bereits geklont, hole aktuellen Stand.");
            if (repo.PullRebase(settings.Branch) == PullOutcome.Conflict)
                throw new SetupException("Beim Aktualisieren des vorhandenen Repositorys gab es einen Konflikt.");
        }
        else
        {
            if (!DirectoryCopy.IsEmpty(settings.RepoPath))
                throw new SetupException($"Der Repository-Ordner ist nicht leer: {settings.RepoPath}");
            _log.Info($"Klone {settings.RemoteUrl} nach {settings.RepoPath}");
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
            throw new SetupException(
                "Das Repository enthält keine Slicer-Profile. Bitte zuerst auf dem ersten PC „Neu einrichten“ ausführen.");

        var result = new SetupResult();
        var states = mappings.ToDictionary(m => m, m => GetState(m, settings.RepoPath));
        CheckForeignJunctions(states);

        var toLink = mappings.Where(m => states[m] != MappingState.Linked).ToList();
        result.SkippedFolders = mappings.Count - toLink.Count;

        // Nur die lokalen Originale sichern; der Repository-Stand gewinnt.
        var backupItems = toLink
            .Where(m => states[m] == MappingState.NotLinked)
            .Select(m => (m.OriginalPath, $"lokal/{m.RepoRelativePath}"))
            .ToList();
        if (backupItems.Count > 0)
            result.BackupZip = _backups.CreateBackup("zweiter-pc", backupItems);

        foreach (var m in toLink)
        {
            LinkCore(m, settings.RepoPath, states[m], targetWins: true);
            result.LinkedFolders++;
        }

        repo.EnsureIdentity();
        MergeMappings(settings, mappings);
        settings.SetupCompleted = true;
        _log.Info($"Zweiter PC eingerichtet: {result.LinkedFolders} Ordner verlinkt, {result.SkippedFolders} übersprungen.");
        return result;
    }

    // ---------------------------------------------------------------- Ordner hinzufügen / entfernen

    /// <summary>Weitere Profilordner nachträglich ins Repository aufnehmen.</summary>
    public SetupResult AddMappings(AppSettings settings, IReadOnlyList<ProfileMapping> mappings)
    {
        ValidateBasics(settings);
        _guard.EnsureSlicersClosed();

        var result = new SetupResult();
        var states = mappings.ToDictionary(m => m, m => GetState(m, settings.RepoPath));
        CheckForeignJunctions(states);
        var toLink = mappings.Where(m => states[m] != MappingState.Linked).ToList();

        result.BackupZip = BackupBeforeLink(toLink, settings.RepoPath, "ordner-hinzufuegen", states);
        foreach (var m in toLink)
        {
            LinkCore(m, settings.RepoPath, states[m], targetWins: false);
            result.LinkedFolders++;
        }
        RepoManifest.AddFolders(settings.RepoPath, mappings);
        MergeMappings(settings, mappings);
        return result;
    }

    /// <summary>
    /// Einzelne Ordner nicht mehr sichern: Junction entfernen und Inhalt an den Originalort
    /// zurückkopieren. Der Stand im Repository bleibt erhalten.
    /// </summary>
    public void RemoveMappings(AppSettings settings, IReadOnlyList<ProfileMapping> mappings)
    {
        _guard.EnsureSlicersClosed();
        foreach (var m in mappings)
        {
            Unlink(m, settings.RepoPath);
            var stored = settings.Mappings.FirstOrDefault(x => x.SameFolderAs(m));
            if (stored != null)
                stored.Enabled = false;
        }
    }

    // ---------------------------------------------------------------- Rückbau

    /// <summary>
    /// Rückbau: alle Junctions entfernen und die Ordner wieder an den Originalort legen.
    /// Das Repository (und damit der Verlauf) bleibt unangetastet.
    /// </summary>
    public int Uninstall(AppSettings settings)
    {
        _guard.EnsureSlicersClosed();
        var count = 0;
        foreach (var m in settings.Mappings)
        {
            if (GetState(m, settings.RepoPath) != MappingState.Linked)
                continue;
            Unlink(m, settings.RepoPath);
            count++;
        }
        settings.SetupCompleted = false;
        _log.Info($"Rückbau abgeschlossen: {count} Ordner zurückgelegt. Repository bleibt unter {settings.RepoPath} erhalten.");
        return count;
    }

    // ---------------------------------------------------------------- Kernoperationen

    private void Unlink(ProfileMapping m, string repoPath)
    {
        var state = GetState(m, repoPath);
        if (state != MappingState.Linked)
        {
            _log.Warn($"{m.DisplayName}: keine Junction auf das Repository vorhanden, nichts zurückzubauen.");
            return;
        }

        var target = m.RepoTarget(repoPath);
        Junction.Remove(m.OriginalPath);
        try
        {
            DirectoryCopy.Copy(target, m.OriginalPath);
            if (!DirectoryCopy.IsCopyComplete(target, m.OriginalPath))
                throw new IOException("Kopie unvollständig.");
        }
        catch (Exception ex)
        {
            // Unvollständige Kopie (nur unsere eigenen Dateien) entfernen und Junction wiederherstellen.
            if (Directory.Exists(m.OriginalPath) && !Junction.IsJunction(m.OriginalPath))
                Directory.Delete(m.OriginalPath, recursive: true);
            Junction.Create(m.OriginalPath, target);
            throw new SetupException($"{m.DisplayName} konnte nicht zurückgelegt werden: {ex.Message}", ex);
        }
        _log.Info($"Zurückgelegt: {m.DisplayName} → {m.OriginalPath}");
    }

    /// <summary>
    /// Verlinkt einen Ordner. <paramref name="targetWins"/> = false: der lokale Ordner wird ins
    /// Repository verschoben. true: der Repository-Stand bleibt, der lokale Ordner wird ersetzt.
    /// In beiden Fällen muss vorher ein Backup angelegt worden sein.
    /// </summary>
    private void LinkCore(ProfileMapping m, string repoPath, MappingState state, bool targetWins)
    {
        var original = m.OriginalPath;
        var target = m.RepoTarget(repoPath);
        var aside = original + AsideSuffix;

        if (state == MappingState.Linked)
            return;
        if (state == MappingState.ForeignJunction)
            throw new SetupException($"{original} ist bereits mit einem anderen Ort verknüpft.");
        if (Directory.Exists(aside))
            throw new SetupException($"Es liegt noch ein Ordner aus einem abgebrochenen Vorgang vor: {aside}. Bitte prüfen und entfernen.");

        if (state == MappingState.Missing)
        {
            Directory.CreateDirectory(target);
            Junction.Create(original, target);
            _log.Info($"Verlinkt (neu angelegt): {m.DisplayName}");
            return;
        }

        if (!targetWins)
        {
            if (Directory.Exists(target) && !DirectoryCopy.IsEmpty(target))
                Directory.Delete(target, recursive: true); // vorher gesichert (BackupBeforeLink)
            DirectoryCopy.Copy(original, target);
            if (!DirectoryCopy.IsCopyComplete(original, target))
                throw new SetupException($"Kopieren von {original} nach {target} war unvollständig. Es wurde nichts verändert.");
        }
        else
        {
            Directory.CreateDirectory(target);
        }

        // Original zur Seite legen (gleiches Laufwerk → atomar), Junction anlegen, erst dann löschen.
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
            throw new SetupException($"Junction für {m.DisplayName} konnte nicht angelegt werden: {ex.Message}", ex);
        }
        Directory.Delete(aside, recursive: true);
        _log.Info($"Verlinkt: {original} → {target}");
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
            throw new SetupException("Diese Ordner sind bereits mit einem anderen Ort verknüpft:\n" + string.Join("\n", foreign));
    }

    private static void ValidateBasics(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RepoPath))
            throw new SetupException("Kein Repository-Ordner angegeben.");
        if (!Path.IsPathFullyQualified(settings.RepoPath))
            throw new SetupException("Der Repository-Ordner muss ein vollständiger Pfad sein.");
        if (string.IsNullOrWhiteSpace(settings.Branch))
            throw new SetupException("Kein Branch angegeben.");
    }

    private static void WriteGitAttributes(string repoPath)
    {
        // Profildateien byte-genau speichern (keine CRLF/LF-Umwandlung).
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
