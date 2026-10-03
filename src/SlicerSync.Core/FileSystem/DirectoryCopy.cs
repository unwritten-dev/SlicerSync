namespace SlicerSync.Core.FileSystem;

public static class DirectoryCopy
{
    /// <summary>Copies a folder recursively. Existing files in the target are never overwritten.</summary>
    public static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            File.Copy(file, dest, overwrite: false);
            File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(file));
        }
    }

    /// <summary>Checks that every file from <paramref name="source"/> exists with identical content in <paramref name="target"/>.</summary>
    public static bool IsCopyComplete(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            if (!File.Exists(dest) || !FilesEqual(file, dest))
                return false;
        }
        return true;
    }

    public static bool FilesEqual(string a, string b)
    {
        var ia = new FileInfo(a);
        var ib = new FileInfo(b);
        if (ia.Length != ib.Length)
            return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    public static bool IsEmpty(string dir) =>
        !Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any();

    public static int CountFiles(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;
}
