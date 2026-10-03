using System.Diagnostics;
using System.Text;

namespace SlicerSync.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>Text for error messages: stderr, falling back to stdout.</summary>
    public string ErrorText => string.IsNullOrWhiteSpace(StdErr) ? StdOut.Trim() : StdErr.Trim();
}

public sealed class GitException : Exception
{
    public GitException(string message, GitResult? result = null) : base(message) => Result = result;

    public GitResult? Result { get; }
}

/// <summary>Runs the installed git.exe. Credentials are handled by Git itself (Git Credential Manager) only.</summary>
public sealed class GitRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public GitRunner(string gitExe)
    {
        GitExe = gitExe;
    }

    public string GitExe { get; }

    /// <summary>Extra environment variables (e.g. an isolated global Git config for tests).</summary>
    public Dictionary<string, string?> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

    public GitResult Run(string? workDir, params string[] args) => Run(workDir, DefaultTimeout, args);

    public GitResult Run(string? workDir, TimeSpan timeout, params string[] args) => RunWithInput(workDir, timeout, null, args);

    /// <summary>Like <see cref="Run(string?, TimeSpan, string[])"/>, but writes <paramref name="input"/> to stdin.</summary>
    public GitResult RunWithInput(string? workDir, TimeSpan timeout, string? input, params string[] args)
    {
        var psi = new ProcessStartInfo(GitExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workDir != null)
            psi.WorkingDirectory = workDir;

        // Stable English output; no prompts in the (non-existent) terminal.
        // The Credential Manager shows its own sign-in window when needed.
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var (key, value) in Environment)
            psi.Environment[key] = value;

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new GitException(Loc.T(
                $"Could not start Git ({GitExe}): {ex.Message}",
                $"Git konnte nicht gestartet werden ({GitExe}): {ex.Message}"));
        }

        if (input != null)
        {
            // Write without BOM: Git reads stdin line by line as UTF-8.
            var bytes = new UTF8Encoding(false).GetBytes(input);
            process.StandardInput.BaseStream.Write(bytes);
            process.StandardInput.BaseStream.Flush();
        }
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw new GitException(Loc.T(
                $"Timeout while running \"git {string.Join(' ', args)}\".",
                $"Zeitüberschreitung bei „git {string.Join(' ', args)}“."));
        }
        process.WaitForExit();

        return new GitResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    public GitResult RunChecked(string? workDir, params string[] args)
    {
        var result = Run(workDir, args);
        if (!result.Success)
            throw new GitException(Loc.T(
                $"git {args.FirstOrDefault()} failed: {result.ErrorText}",
                $"git {args.FirstOrDefault()} fehlgeschlagen: {result.ErrorText}"), result);
        return result;
    }
}
