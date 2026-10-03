using System.Diagnostics;
using System.Text;

namespace SlicerSync.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>Fehlertext für Meldungen: stderr, sonst stdout.</summary>
    public string ErrorText => string.IsNullOrWhiteSpace(StdErr) ? StdOut.Trim() : StdErr.Trim();
}

public sealed class GitException : Exception
{
    public GitException(string message, GitResult? result = null) : base(message) => Result = result;

    public GitResult? Result { get; }
}

/// <summary>Ruft die installierte git.exe auf. Zugangsdaten verwaltet ausschließlich Git selbst (Credential Manager).</summary>
public sealed class GitRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public GitRunner(string gitExe)
    {
        GitExe = gitExe;
    }

    public string GitExe { get; }

    /// <summary>Zusätzliche Umgebungsvariablen (z. B. für Tests: isolierte globale Git-Konfiguration).</summary>
    public Dictionary<string, string?> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

    public GitResult Run(string? workDir, params string[] args) => Run(workDir, DefaultTimeout, args);

    public GitResult Run(string? workDir, TimeSpan timeout, params string[] args)
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

        // Englische, stabile Ausgaben; keine Abfragen im (nicht vorhandenen) Terminal.
        // Der Credential Manager zeigt bei Bedarf sein eigenes Anmeldefenster.
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
            throw new GitException($"Git konnte nicht gestartet werden ({GitExe}): {ex.Message}");
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
            throw new GitException($"Zeitüberschreitung bei „git {string.Join(' ', args)}“.");
        }
        process.WaitForExit();

        return new GitResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    public GitResult RunChecked(string? workDir, params string[] args)
    {
        var result = Run(workDir, args);
        if (!result.Success)
            throw new GitException($"git {args.FirstOrDefault()} fehlgeschlagen: {result.ErrorText}", result);
        return result;
    }
}
