namespace SlicerSync.Core.Git;

public sealed record ChangedFile(string Status, string Path, string? OldPath = null)
{
    public string StatusText => Status switch
    {
        "A" => "neu",
        "M" => "geändert",
        "D" => "gelöscht",
        "R" => "umbenannt",
        "C" => "kopiert",
        _ => Status,
    };
}

public sealed record CommitInfo(string Hash, string Author, DateTimeOffset Date, string Message, IReadOnlyList<ChangedFile> Files)
{
    public string ShortHash => Hash.Length > 8 ? Hash[..8] : Hash;
}

public enum PullOutcome { UpToDate, Updated, NoRemoteBranch, Conflict }

/// <summary>Git-Operationen auf einem Arbeitsverzeichnis.</summary>
public sealed class GitRepository
{
    public const string RemoteName = "origin";

    private readonly GitRunner _git;

    public GitRepository(GitRunner git, string path)
    {
        _git = git;
        Path = path;
    }

    public string Path { get; }

    public bool Exists => Directory.Exists(System.IO.Path.Combine(Path, ".git"));

    public void Init(string branch)
    {
        Directory.CreateDirectory(Path);
        _git.RunChecked(Path, "init", "-b", branch);
    }

    public static GitRepository Clone(GitRunner git, string url, string path, string branch)
    {
        var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        Directory.CreateDirectory(parent);
        var result = git.Run(parent, "clone", "--branch", branch, url, path);
        if (!result.Success)
        {
            // Leere Repositories haben noch keinen Branch: dann ohne --branch klonen.
            if (result.ErrorText.Contains("not found in upstream", StringComparison.OrdinalIgnoreCase))
                git.RunChecked(parent, "clone", url, path);
            else
                throw new GitException($"Klonen fehlgeschlagen: {result.ErrorText}", result);
        }
        return new GitRepository(git, path);
    }

    public string? GetConfig(string key)
    {
        var r = _git.Run(Path, "config", "--get", key);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public void SetConfig(string key, string value) => _git.RunChecked(Path, "config", key, value);

    /// <summary>Setzt Name/E-Mail lokal, falls Git global keine Identität kennt (sonst schlägt commit fehl).</summary>
    public void EnsureIdentity()
    {
        if (string.IsNullOrEmpty(GetConfig("user.name")))
            SetConfig("user.name", $"{Environment.UserName} ({Environment.MachineName})");
        if (string.IsNullOrEmpty(GetConfig("user.email")))
            SetConfig("user.email", $"{Environment.UserName}@{Environment.MachineName}.local".ToLowerInvariant());
    }

    public string? GetRemoteUrl()
    {
        var r = _git.Run(Path, "remote", "get-url", RemoteName);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public void SetRemote(string? url)
    {
        var current = GetRemoteUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            if (current != null)
                _git.RunChecked(Path, "remote", "remove", RemoteName);
            return;
        }
        if (current == null)
            _git.RunChecked(Path, "remote", "add", RemoteName, url);
        else if (current != url)
            _git.RunChecked(Path, "remote", "set-url", RemoteName, url);
    }

    public string CurrentBranch()
    {
        var r = _git.RunChecked(Path, "symbolic-ref", "--short", "HEAD");
        return r.StdOut.Trim();
    }

    /// <summary>Wechselt auf den gewünschten Branch (legt ihn an, wenn es noch keine Commits gibt).</summary>
    public void EnsureBranch(string branch)
    {
        if (CurrentBranch() == branch)
            return;
        if (HeadHash() == null)
            _git.RunChecked(Path, "symbolic-ref", "HEAD", "refs/heads/" + branch);
        else
            throw new GitException($"Das Repository steht auf Branch „{CurrentBranch()}“, eingestellt ist „{branch}“.");
    }

    public string? HeadHash()
    {
        var r = _git.Run(Path, "rev-parse", "--verify", "--quiet", "HEAD");
        return r.Success ? r.StdOut.Trim() : null;
    }

    public void StageAll() => _git.RunChecked(Path, "add", "-A");

    public IReadOnlyList<string> StagedFiles()
    {
        var r = _git.RunChecked(Path, "diff", "--cached", "--name-only", "-z");
        return r.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public void Commit(string message) => _git.RunChecked(Path, "commit", "--quiet", "-m", message);

    public bool RemoteBranchExists(string branch)
    {
        var r = _git.Run(Path, "ls-remote", "--exit-code", "--heads", RemoteName, branch);
        if (r.ExitCode == 2)
            return false;
        if (!r.Success)
            throw new GitException($"Remote nicht erreichbar: {r.ErrorText}", r);
        return true;
    }

    /// <summary>
    /// git pull --rebase. Bei einem Konflikt wird der Rebase abgebrochen, sodass der lokale
    /// Stand unverändert bleibt.
    /// </summary>
    public PullOutcome PullRebase(string branch)
    {
        if (!RemoteBranchExists(branch))
            return PullOutcome.NoRemoteBranch;

        var before = HeadHash();
        var r = _git.Run(Path, "pull", "--rebase", "--autostash", "--no-edit", RemoteName, branch);
        if (!r.Success)
        {
            if (IsRebaseInProgress())
            {
                _git.Run(Path, "rebase", "--abort");
                return PullOutcome.Conflict;
            }
            throw new GitException($"git pull fehlgeschlagen: {r.ErrorText}", r);
        }
        return HeadHash() == before ? PullOutcome.UpToDate : PullOutcome.Updated;
    }

    public bool IsRebaseInProgress() =>
        Directory.Exists(System.IO.Path.Combine(Path, ".git", "rebase-merge")) ||
        Directory.Exists(System.IO.Path.Combine(Path, ".git", "rebase-apply"));

    /// <summary>Anzahl lokaler Commits, die noch nicht auf dem Remote sind (null = Remote-Branch unbekannt).</summary>
    public int? CommitsAhead(string branch)
    {
        var r = _git.Run(Path, "rev-list", "--count", $"{RemoteName}/{branch}..HEAD");
        return r.Success && int.TryParse(r.StdOut.Trim(), out var n) ? n : null;
    }

    public void Push(string branch) => _git.RunChecked(Path, "push", "--set-upstream", RemoteName, branch);

    public IReadOnlyList<CommitInfo> Log(int maxCount, string? path = null)
    {
        if (HeadHash() == null)
            return Array.Empty<CommitInfo>();

        var args = new List<string>
        {
            "log", "-n", maxCount.ToString(), "--name-status", "-z",
            "--pretty=format:%x1e%H%x1f%an%x1f%aI%x1f%s%x1f",
        };
        if (path != null)
        {
            args.Add("--");
            args.Add(path);
        }
        var r = _git.RunChecked(Path, args.ToArray());
        return ParseLog(r.StdOut);
    }

    internal static IReadOnlyList<CommitInfo> ParseLog(string output)
    {
        var commits = new List<CommitInfo>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\x1f');
            if (fields.Length < 5)
                continue;

            // Nach dem letzten Trennzeichen folgen NUL-getrennt: Status, Pfad[, Pfad] …
            var tokens = fields[4].Split('\0').Select(t => t.Trim('\n', '\r')).Where(t => t.Length > 0).ToList();
            var files = new List<ChangedFile>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var status = tokens[i];
                var kind = status[..1];
                if ((kind == "R" || kind == "C") && i + 2 < tokens.Count)
                {
                    files.Add(new ChangedFile(kind, tokens[i + 2], tokens[i + 1]));
                    i += 2;
                }
                else if (i + 1 < tokens.Count)
                {
                    files.Add(new ChangedFile(kind, tokens[i + 1]));
                    i += 1;
                }
            }

            commits.Add(new CommitInfo(
                fields[0].Trim(),
                fields[1],
                DateTimeOffset.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
                fields[3],
                files));
        }
        return commits;
    }

    public bool FileExistsInCommit(string commit, string repoRelativePath)
    {
        var r = _git.Run(Path, "cat-file", "-e", $"{commit}:{ToGitPath(repoRelativePath)}");
        return r.Success;
    }

    /// <summary>Überschreibt eine Datei im Arbeitsverzeichnis mit dem Stand aus einem Commit.</summary>
    public void RestoreFile(string commit, string repoRelativePath) =>
        _git.RunChecked(Path, "restore", "--source", commit, "--worktree", "--", ToGitPath(repoRelativePath));

    public static string ToGitPath(string path) => path.Replace('\\', '/');
}
