using System.Text;
using SlicerSync.Core.Config;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Slicers;

namespace SlicerSync.Core.Setup;

public sealed record ProfileSetInfo(string Name, int FileCount);

public sealed record CopyProfilesResult(int Added, int Updated, int Unchanged, string? BackupZip);

/// <summary>
/// Copies the profiles of a signed-in account (Orca Slicer, Bambu Studio) into the folder the slicer
/// uses without sign-in ("default"). The copy works inside the repository, so it reaches every PC
/// with the next sync. Account and cloud IDs are removed from the .info files, exactly like the slicer
/// writes them for "default"; the account folder itself is not touched.
/// </summary>
public sealed class ProfileSetCopier
{
    public const string NoSignInSet = "default";

    private readonly BackupService _backups;
    private readonly ProcessGuard _guard;
    private readonly ILogger _log;

    public ProfileSetCopier(BackupService backups, ProcessGuard guard, ILogger log)
    {
        _backups = backups;
        _guard = guard;
        _log = log;
    }

    /// <summary>Profile sets in the repository that contain files (except the no-sign-in set).</summary>
    public static IReadOnlyList<ProfileSetInfo> AccountSets(string repoPath, SlicerDefinition slicer)
    {
        var dir = Path.Combine(repoPath, slicer.RepoFolder);
        if (slicer.Layout != ProfileLayout.UserSets || !Directory.Exists(dir))
            return Array.Empty<ProfileSetInfo>();
        return Directory.EnumerateDirectories(dir)
            .Select(d => new ProfileSetInfo(Path.GetFileName(d), DirectoryCopy.CountFiles(d)))
            .Where(s => s.FileCount > 0 && !s.Name.Equals(NoSignInSet, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public CopyProfilesResult CopyToNoSignIn(AppSettings settings, SlicerDefinition slicer, string sourceSet)
    {
        if (slicer.Layout != ProfileLayout.UserSets)
            throw new SetupException(Loc.T($"{slicer.DisplayName} has no account folders.", $"{slicer.DisplayName} hat keine Konto-Ordner."));
        if (sourceSet.Equals(NoSignInSet, StringComparison.OrdinalIgnoreCase))
            throw new SetupException(Loc.T("Please choose an account folder.", "Bitte einen Konto-Ordner wählen."));
        _guard.EnsureSlicersClosed(settings.Mappings);

        // The target folders must be backed up on this PC; otherwise the copy would land in the
        // repository only and the slicer here would not see it.
        var notLinked = slicer.Folders
            .Where(folder => !settings.EnabledMappings.Any(m =>
                m.Slicer == slicer.Kind &&
                m.ProfileSet.Equals(NoSignInSet, StringComparison.OrdinalIgnoreCase) &&
                m.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase) &&
                SetupService.GetState(m, settings.RepoPath) == MappingState.Linked))
            .ToList();
        if (notLinked.Count > 0)
            throw new SetupException(Loc.T(
                $"The folder \"{NoSignInSet}\" of {slicer.DisplayName} is not backed up on this PC ({string.Join(", ", notLinked)}). " +
                "Please start the slicer once without signing in, then add these folders in Settings → Profiles.",
                $"Der Ordner „{NoSignInSet}“ von {slicer.DisplayName} wird auf diesem PC nicht gesichert ({string.Join(", ", notLinked)}). " +
                "Bitte den Slicer einmal ohne Anmeldung starten und die Ordner dann unter Einstellungen → Profile hinzufügen."));

        var plan = new List<(string Target, byte[] Content, bool Exists)>();
        var unchanged = 0;
        foreach (var folder in slicer.Folders)
        {
            var source = Path.Combine(settings.RepoPath, slicer.RepoFolder, sourceSet, folder);
            var target = Path.Combine(settings.RepoPath, slicer.RepoFolder, NoSignInSet, folder);
            if (!Directory.Exists(source))
                continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(target, Path.GetRelativePath(source, file));
                var content = file.EndsWith(".info", StringComparison.OrdinalIgnoreCase)
                    ? WithoutAccount(File.ReadAllText(file))
                    : File.ReadAllBytes(file);
                var exists = File.Exists(dest);
                if (exists && File.ReadAllBytes(dest).AsSpan().SequenceEqual(content))
                    unchanged++;
                else
                    plan.Add((dest, content, exists));
            }
        }

        var overwritten = plan.Where(p => p.Exists).ToList();
        string? backup = null;
        if (overwritten.Count > 0)
            backup = _backups.CreateBackup("copy-profiles", overwritten.Select(p =>
                (p.Target, Path.GetRelativePath(settings.RepoPath, p.Target))));

        foreach (var (target, content, _) in plan)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
        }

        var result = new CopyProfilesResult(plan.Count - overwritten.Count, overwritten.Count, unchanged, backup);
        _log.Info(Loc.T(
            $"Profiles of {slicer.DisplayName} account {sourceSet} copied to \"{NoSignInSet}\": {result.Added} new, {result.Updated} updated, {result.Unchanged} unchanged.",
            $"Profile von {slicer.DisplayName}-Konto {sourceSet} nach „{NoSignInSet}“ übernommen: {result.Added} neu, {result.Updated} aktualisiert, {result.Unchanged} unverändert."));
        return result;
    }

    /// <summary>
    /// Rewrites an .info file the way the slicer writes it for presets without sign-in: no user and
    /// cloud ID, marked for update. All other lines are kept in their order.
    /// </summary>
    internal static byte[] WithoutAccount(string info)
    {
        var lines = info.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).Select(line =>
        {
            var key = line.Split('=', 2)[0].Trim();
            return key switch
            {
                "sync_info" => "sync_info = update",
                "user_id" => "user_id = ",
                "setting_id" => "setting_id = ",
                _ => line,
            };
        });
        return Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\r\n")));
    }
}
