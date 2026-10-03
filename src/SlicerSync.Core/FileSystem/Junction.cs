using System.Diagnostics;

namespace SlicerSync.Core.FileSystem;

/// <summary>Create, detect and remove directory junctions (mklink /J, no admin rights needed).</summary>
public static class Junction
{
    public static bool IsJunction(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    /// <summary>Full path of a junction's target, or null.</summary>
    public static string? GetTarget(string path)
    {
        if (!IsJunction(path))
            return null;
        var target = new DirectoryInfo(path).LinkTarget;
        if (target == null)
            return null;
        if (target.StartsWith(@"\??\", StringComparison.Ordinal))
            target = target[4..];
        return Path.GetFullPath(target);
    }

    public static bool PointsTo(string path, string expectedTarget)
    {
        var target = GetTarget(path);
        return target != null && PathsEqual(target, expectedTarget);
    }

    public static void Create(string linkPath, string targetPath)
    {
        if (Directory.Exists(linkPath) || File.Exists(linkPath))
            throw new IOException(Loc.T(
                $"Something already exists where the junction should go: {linkPath}",
                $"Am Ort der Junction existiert bereits etwas: {linkPath}"));
        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException(Loc.T(
                $"Junction target is missing: {targetPath}",
                $"Ziel der Junction fehlt: {targetPath}"));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(linkPath))!);

        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("/d");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(Path.GetFullPath(linkPath));
        psi.ArgumentList.Add(Path.GetFullPath(targetPath));

        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 || !PointsTo(linkPath, targetPath))
            throw new IOException(Loc.T(
                $"Could not create junction ({linkPath} → {targetPath}): {err.Trim()}",
                $"Junction konnte nicht angelegt werden ({linkPath} → {targetPath}): {err.Trim()}"));
    }

    /// <summary>Removes only the junction itself – the target's content is left untouched.</summary>
    public static void Remove(string linkPath)
    {
        if (!IsJunction(linkPath))
            throw new IOException(Loc.T($"Not a junction: {linkPath}", $"Keine Junction: {linkPath}"));
        // Not recursive: deletes the link only.
        Directory.Delete(linkPath, recursive: false);
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
