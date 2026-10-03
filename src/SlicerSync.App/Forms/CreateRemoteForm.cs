using SlicerSync.Core;
using SlicerSync.Core.Hosting;

namespace SlicerSync.App.Forms;

public enum RemoteProvider { GitHub, GitLab }

/// <summary>
/// Creates the remote repository: GitHub via API (sign-in through the Git Credential Manager),
/// GitLab by building the URL of a project that GitLab creates on the first push.
/// </summary>
public sealed class CreateRemoteForm : Form
{
    private readonly AppServices _services;
    private readonly RemoteProvider _provider;
    private readonly TextBox _name = new() { Text = RemoteCreator.DefaultName };
    private readonly TextBox _host = new() { Text = RemoteCreator.DefaultGitLabHost };
    private readonly TextBox _namespace = new();

    public CreateRemoteForm(AppServices services, RemoteProvider provider)
    {
        _services = services;
        _provider = provider;
        var github = provider == RemoteProvider.GitHub;
        Ui.InitDialog(this,
            github ? Loc.T("Create repository on GitHub", "Repository auf GitHub anlegen")
                   : Loc.T("Create project on GitLab", "Projekt auf GitLab anlegen"),
            520, github ? 230 : 280);

        var col = Ui.Column();
        var grid = Ui.Grid(300);
        if (github)
        {
            Ui.AddRow(grid, Loc.T("Repository name:", "Repository-Name:"), _name);
            col.Controls.Add(grid);
            col.Controls.Add(Ui.Text(Loc.T(
                "A new private repository is created in your GitHub account. If you are not signed in yet, " +
                "the Git Credential Manager opens a sign-in window (browser). SlicerSync does not store any credentials.",
                "In deinem GitHub-Konto wird ein neues privates Repository angelegt. Falls du noch nicht angemeldet bist, " +
                "öffnet der Git Credential Manager ein Anmeldefenster (Browser). SlicerSync speichert keine Zugangsdaten.")));
        }
        else
        {
            Ui.AddRow(grid, Loc.T("GitLab server:", "GitLab-Server:"), _host);
            Ui.AddRow(grid, Loc.T("User name or group:", "Benutzername oder Gruppe:"), _namespace);
            Ui.AddRow(grid, Loc.T("Project name:", "Projektname:"), _name);
            col.Controls.Add(grid);
            col.Controls.Add(Ui.Text(Loc.T(
                "GitLab creates the project automatically as a private project on the first upload at the end of the setup. " +
                "On the first access the Git Credential Manager asks you to sign in.",
                "GitLab legt das Projekt beim ersten Hochladen am Ende der Einrichtung automatisch als privates Projekt an. " +
                "Beim ersten Zugriff fragt der Git Credential Manager nach der Anmeldung.")));
            col.Controls.Add(Ui.Text(Loc.T(
                "If your GitLab group does not allow this, create the project on the website and enter its URL instead.",
                "Falls deine GitLab-Gruppe das nicht erlaubt, lege das Projekt auf der Webseite an und trage stattdessen die URL ein.")));
        }

        var (bar, ok) = Ui.DialogButtons(this, github ? Loc.T("Create", "Anlegen") : Loc.T("Use", "Übernehmen"));
        ok.Click += async (_, _) => await OnOk();
        Controls.Add(col);
        Controls.Add(bar);
    }

    /// <summary>The URL to enter as remote.</summary>
    public string ResultUrl { get; private set; } = "";

    /// <summary>true = the project does not exist yet and is created on the first push (GitLab).</summary>
    public bool CreateOnPush { get; private set; }

    private async Task OnOk()
    {
        var name = _name.Text.Trim();
        if (RemoteCreator.ValidateName(name) is { } nameError)
        {
            Ui.Error(this, nameError);
            return;
        }

        if (_provider == RemoteProvider.GitLab)
        {
            try
            {
                ResultUrl = RemoteCreator.GitLabUrl(_host.Text, _namespace.Text, name);
                CreateOnPush = true;
                DialogResult = DialogResult.OK;
            }
            catch (HostingException ex)
            {
                Ui.Error(this, ex.Message);
            }
            return;
        }

        try
        {
            var creator = _services.CreateRemoteCreator();
            var repo = await Ui.RunBusy(this, () => creator.CreateOnGitHubAsync(name).GetAwaiter().GetResult());
            ResultUrl = repo.CloneUrl;
            Ui.Info(this, Loc.T(
                $"Private repository created:\n{repo.WebUrl}",
                $"Privates Repository angelegt:\n{repo.WebUrl}"));
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _services.Log.Error(Loc.T("Creating the GitHub repository failed", "GitHub-Repository konnte nicht angelegt werden"), ex);
            Ui.Error(this, Ui.Describe(ex));
        }
    }
}
