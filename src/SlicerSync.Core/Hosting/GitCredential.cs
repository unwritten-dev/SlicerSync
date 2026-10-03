using SlicerSync.Core.Git;

namespace SlicerSync.Core.Hosting;

/// <summary>
/// Gets credentials via "git credential fill" from the configured credential helper
/// (Git Credential Manager). SlicerSync keeps them in memory only and never stores them itself.
/// </summary>
public sealed class GitCredential
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, string> _fields;

    private GitCredential(Dictionary<string, string> fields) => _fields = fields;

    public string Username => _fields.GetValueOrDefault("username", "");
    public string Password => _fields.GetValueOrDefault("password", "");

    /// <summary>Asks the Credential Manager, which shows its sign-in window if needed.</summary>
    public static GitCredential Fill(GitRunner git, string host)
    {
        var r = git.RunWithInput(null, LoginTimeout, $"protocol=https\nhost={host}\n\n", "credential", "fill");
        if (!r.Success)
            throw new HostingException(Loc.T(
                $"Cannot sign in to {host}. Is the Git Credential Manager installed?\n{r.ErrorText}",
                $"Anmeldung bei {host} nicht möglich. Ist der Git Credential Manager installiert?\n{r.ErrorText}"));

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in r.StdOut.Split('\n'))
        {
            var i = line.IndexOf('=');
            if (i > 0)
                fields[line[..i]] = line[(i + 1)..].TrimEnd('\r');
        }
        if (!fields.ContainsKey("password") || fields["password"].Length == 0)
            throw new HostingException(Loc.T(
                $"The Credential Manager returned no sign-in for {host}.",
                $"Der Credential Manager hat keine Anmeldung für {host} geliefert."));
        return new GitCredential(fields);
    }

    /// <summary>Tells the Credential Manager that the sign-in worked (it stores it then).</summary>
    public void Approve(GitRunner git) => git.RunWithInput(null, GitRunner.DefaultTimeout, Serialize(), "credential", "approve");

    /// <summary>Tells the Credential Manager that the sign-in is invalid (it discards it).</summary>
    public void Reject(GitRunner git) => git.RunWithInput(null, GitRunner.DefaultTimeout, Serialize(), "credential", "reject");

    private string Serialize() => string.Concat(_fields.Select(f => $"{f.Key}={f.Value}\n")) + "\n";
}

public sealed class HostingException : Exception
{
    public HostingException(string message, Exception? inner = null) : base(message, inner) { }
}
