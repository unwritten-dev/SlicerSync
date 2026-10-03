using System.Text.RegularExpressions;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.Hosting;

/// <summary>Creates the remote repository for the profiles (GitHub via API, GitLab on the first push).</summary>
public sealed class RemoteCreator
{
    public const string DefaultName = "slicer-profiles";
    public const string DefaultGitLabHost = "gitlab.com";

    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9._-]+$");
    private static readonly Regex HostPattern = new(@"^[A-Za-z0-9.-]+(:\d+)?$");

    private readonly GitRunner _git;
    private readonly GitHubClient _github;
    private readonly ILogger _log;

    public RemoteCreator(GitRunner git, ILogger log, GitHubClient? github = null)
    {
        _git = git;
        _log = log;
        _github = github ?? new GitHubClient();
    }

    /// <summary>Validates a repository name; returns an error message or null.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Loc.T("Please enter a name.", "Bitte einen Namen angeben.");
        if (name.Length > 100)
            return Loc.T("The name must not be longer than 100 characters.", "Der Name darf höchstens 100 Zeichen lang sein.");
        if (!NamePattern.IsMatch(name) || name is "." or "..")
            return Loc.T(
                "Only letters (no umlauts), digits, dot, hyphen and underscore are allowed.",
                "Erlaubt sind nur Buchstaben (ohne Umlaute), Ziffern, Punkt, Bindestrich und Unterstrich.");
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            return Loc.T("Please enter the name without \".git\".", "Bitte den Namen ohne „.git“ angeben.");
        return null;
    }

    /// <summary>Validates a GitLab user name/group (subgroups with "/" allowed).</summary>
    public static string? ValidateNamespace(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns))
            return Loc.T("Please enter your GitLab user name or group.", "Bitte den GitLab-Benutzernamen oder die Gruppe angeben.");
        return ns.Split('/').All(p => p.Length > 0 && NamePattern.IsMatch(p))
            ? null
            : Loc.T("The user name/group contains invalid characters.", "Benutzername/Gruppe enthält ungültige Zeichen.");
    }

    /// <summary>
    /// Creates a private repository on GitHub. The sign-in comes from the Git Credential Manager
    /// (shows a sign-in window if needed) and is held in memory for this call only.
    /// </summary>
    public async Task<CreatedRepository> CreateOnGitHubAsync(string name, CancellationToken ct = default)
    {
        if (ValidateName(name) is { } error)
            throw new HostingException(error);

        _log.Info(Loc.T($"Creating private GitHub repository \"{name}\" …", $"Lege privates GitHub-Repository „{name}“ an …"));
        var credential = GitCredential.Fill(_git, GitHubClient.Host);
        try
        {
            var repo = await _github.CreatePrivateRepositoryAsync(credential.Password, name, ct);
            credential.Approve(_git);
            _log.Info(Loc.T($"GitHub repository created: {repo.FullName}", $"GitHub-Repository angelegt: {repo.FullName}"));
            return repo;
        }
        catch (GitHubAuthException)
        {
            credential.Reject(_git);
            throw;
        }
    }

    /// <summary>
    /// URL for a GitLab project that does not exist yet. GitLab creates it automatically as a
    /// private project on the first push ("push to create").
    /// </summary>
    public static string GitLabUrl(string host, string ns, string name)
    {
        host = string.IsNullOrWhiteSpace(host) ? DefaultGitLabHost : host.Trim();
        ns = ns.Trim().Trim('/');
        name = name.Trim();
        if (!HostPattern.IsMatch(host))
            throw new HostingException(Loc.T("Invalid GitLab server.", "Ungültiger GitLab-Server."));
        if (ValidateNamespace(ns) is { } nsError)
            throw new HostingException(nsError);
        if (ValidateName(name) is { } nameError)
            throw new HostingException(nameError);
        return $"https://{host}/{ns}/{name}.git";
    }
}
