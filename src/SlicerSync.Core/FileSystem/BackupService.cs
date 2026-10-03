using System.IO.Compression;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.FileSystem;

/// <summary>Creates ZIP backups before anything is moved, deleted or overwritten.</summary>
public sealed class BackupService
{
    private readonly string _backupDir;
    private readonly ILogger _log;

    public BackupService(string backupDir, ILogger log)
    {
        _backupDir = backupDir;
        _log = log;
    }

    public string BackupDir => _backupDir;

    /// <summary>
    /// Backs up folders and files into a ZIP; each item is stored under its given name.
    /// Verifies afterwards that every file made it into the archive. Returns the ZIP path.
    /// </summary>
    public string CreateBackup(string label, IEnumerable<(string Source, string NameInZip)> items)
    {
        Directory.CreateDirectory(_backupDir);
        var safeLabel = string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var zipPath = Path.Combine(_backupDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeLabel}.zip");
        for (var i = 2; File.Exists(zipPath); i++)
            zipPath = Path.Combine(_backupDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeLabel}-{i}.zip");

        var expected = 0;
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (source, name) in items)
            {
                var prefix = name.Replace('\\', '/').Trim('/');
                if (Directory.Exists(source))
                {
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(source, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"{prefix}/{rel}", CompressionLevel.Optimal);
                        expected++;
                    }
                    // Keep empty folders as directory entries.
                    if (!Directory.EnumerateFileSystemEntries(source).Any())
                        zip.CreateEntry(prefix + "/");
                }
                else if (File.Exists(source))
                {
                    zip.CreateEntryFromFile(source, prefix, CompressionLevel.Optimal);
                    expected++;
                }
            }
        }

        using (var check = ZipFile.OpenRead(zipPath))
        {
            var actual = check.Entries.Count(e => !e.FullName.EndsWith('/'));
            if (actual != expected)
                throw new IOException(Loc.T(
                    $"Backup incomplete ({actual} of {expected} files): {zipPath}",
                    $"Sicherung unvollständig ({actual} von {expected} Dateien): {zipPath}"));
        }

        _log.Info(Loc.T($"Backup created: {zipPath} ({expected} files)", $"Sicherung angelegt: {zipPath} ({expected} Dateien)"));
        return zipPath;
    }
}
