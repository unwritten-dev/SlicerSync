using SlicerSync.Core;
using SlicerSync.Core.Config;
using SlicerSync.Core.Git;
using SlicerSync.Core.Setup;

namespace SlicerSync.App.Forms;

/// <summary>Setup wizard: set up from scratch, or connect a second PC to an existing repository.</summary>
public sealed class SetupWizardForm : Form
{
    private sealed record Page(string Title, Control Content, Func<bool> IsVisible, Func<bool> CanLeave, Action? OnEnter = null);

    private sealed class MappingItem
    {
        public MappingItem(ProfileMapping mapping, MappingState state) { Mapping = mapping; State = state; }
        public ProfileMapping Mapping { get; }
        public MappingState State { get; }
        public override string ToString() => Mapping.DisplayName + State switch
        {
            MappingState.Linked => Loc.T("   (already set up)", "   (bereits eingerichtet)"),
            MappingState.ForeignJunction => Loc.T("   (points to another location!)", "   (verweist auf anderen Ort!)"),
            _ => "",
        };
    }

    private sealed record LanguageItem(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly AppServices _services;
    private readonly List<Page> _pages = new();
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 12, 12, 0) };
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private readonly Button _back = new() { Text = Loc.T("< Back", "< Zurück"), AutoSize = true };
    private readonly Button _next = new() { AutoSize = true };
    private readonly Button _cancel = new() { Text = Loc.T("Cancel", "Abbrechen"), AutoSize = true, DialogResult = DialogResult.Cancel };
    private int _index;
    private bool _done;
    private bool _foldersLoaded;

    // Welcome page
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _gitStatus = Ui.Text("");
    private readonly LinkLabel _gitLink = new() { Text = Loc.T("Download Git for Windows", "Git for Windows herunterladen"), AutoSize = true, Margin = new Padding(0, 4, 0, 8) };

    // Mode page
    private readonly RadioButton _modeNew = new() { Text = Loc.T("New setup – this PC has the profiles", "Neu einrichten – dieser PC hat die Profile"), AutoSize = true, Checked = true };
    private readonly RadioButton _modeClone = new() { Text = Loc.T("Second PC – clone an existing repository", "Zweiter PC – bestehendes Repository klonen"), AutoSize = true };

    // Folder page
    private readonly Label _detected = Ui.Text("");
    private readonly CheckedListBox _folders = new() { Width = 560, Height = 170, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Label _secretWarning = Ui.Warning(Loc.T(
        "\"physical_printer\" can contain API keys and passwords for PrusaLink/OctoPrint. The repository must be private.",
        "„physical_printer“ kann API-Schlüssel und Passwörter für PrusaLink/OctoPrint enthalten. Das Repository muss unbedingt privat sein."));

    // Repository page
    private readonly TextBox _repoPath;
    private readonly Control _repoPicker;
    private readonly TextBox _remoteUrl = new() { Width = 400 };
    private readonly TextBox _branch = new() { Width = 120 };
    private readonly FlowLayoutPanel _createButtons;
    private string? _createOnPushUrl;

    // Summary page
    private readonly Label _summary = Ui.Text("");
    private readonly Label _slicerStatus = Ui.Text("");
    private readonly TextBox _output = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 560, Height = 150,
        Font = new Font(FontFamily.GenericMonospace, 8.5f),
    };

    public SetupWizardForm(AppServices services)
    {
        _services = services;
        var current = services.Settings;
        (_repoPicker, _repoPath) = Ui.FolderPicker(string.IsNullOrEmpty(current.RepoPath) ? services.Paths.DefaultRepoPath : current.RepoPath);
        _remoteUrl.Text = current.RemoteUrl;
        _branch.Text = string.IsNullOrEmpty(current.Branch) ? "main" : current.Branch;
        _createButtons = Ui.Row(
            Ui.Button(Loc.T("Create on GitHub…", "Auf GitHub anlegen…"), (_, _) => CreateRemote(RemoteProvider.GitHub)),
            Ui.Button(Loc.T("Create on GitLab…", "Auf GitLab anlegen…"), (_, _) => CreateRemote(RemoteProvider.GitLab)));

        Ui.InitDialog(this, Loc.T("SlicerSync – Setup", "SlicerSync – Einrichtung"), 620, 480);
        CancelButton = _cancel;
        _header.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { _cancel, _next, _back });
        Controls.Add(_host);
        Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 1, BorderStyle = BorderStyle.Fixed3D });
        Controls.Add(buttons);
        Controls.Add(_header);

        _back.Click += (_, _) => Navigate(-1);
        _next.Click += async (_, _) => await OnNext();
        _gitLink.LinkClicked += (_, _) => Ui.Open(GitLocator.DownloadUrl);
        _folders.ItemCheck += (_, _) => BeginInvoke(UpdateSecretWarning);

        BuildPages();
        _services.Log.MessageLogged += OnLogMessage;
        FormClosed += (_, _) => _services.Log.MessageLogged -= OnLogMessage;
        ShowPage(0);
    }

    /// <summary>The user picked another language; the caller reopens the wizard.</summary>
    public bool LanguageChanged { get; private set; }

    private bool IsNewMode => _modeNew.Checked;

    private void BuildPages()
    {
        // 1. Welcome + Git check
        var welcome = Ui.Column();
        _language.Items.Add(new LanguageItem(Loc.English, "English"));
        _language.Items.Add(new LanguageItem(Loc.German, "Deutsch"));
        _language.SelectedIndex = Loc.IsGerman ? 1 : 0;
        _language.SelectedIndexChanged += (_, _) => ChangeLanguage();
        welcome.Controls.Add(Ui.Row(new Label { Text = "Language / Sprache:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, _language));
        welcome.Controls.Add(Ui.Text(Loc.T(
            "SlicerSync backs up your own profiles from Orca Slicer, Bambu Studio, PrusaSlicer, SuperSlicer or any other " +
            "slicer with full version history in a private Git repository (e.g. on GitHub or GitLab).\n\n" +
            "To do so, the profile folders are moved into a repository folder and linked back to their original location " +
            "with directory junctions. The slicers do not notice anything.\n\n" +
            "A ZIP backup is created before every change.",
            "SlicerSync sichert deine selbst erstellten Profile aus Orca Slicer, Bambu Studio, PrusaSlicer, SuperSlicer oder " +
            "jedem anderen Slicer mit Versionsverlauf in ein privates Git-Repository (z. B. auf GitHub oder GitLab).\n\n" +
            "Die Profilordner werden dazu in einen Repository-Ordner verschoben und über Verzeichnis-Verknüpfungen " +
            "(Junctions) an den ursprünglichen Ort zurückverlinkt. Die Slicer merken davon nichts.\n\n" +
            "Vor jeder Änderung wird eine ZIP-Sicherung angelegt.")));
        welcome.Controls.Add(_gitStatus);
        welcome.Controls.Add(_gitLink);
        welcome.Controls.Add(Ui.Button(Loc.T("Check again", "Erneut prüfen"), (_, _) => UpdateGitStatus()));
        _pages.Add(new Page(Loc.T("Welcome", "Willkommen"), welcome, () => true, () => _services.GitAvailable, UpdateGitStatus));

        // 2. Mode
        var mode = Ui.Column();
        mode.Controls.Add(_modeNew);
        mode.Controls.Add(Ui.Text(Loc.T(
            "The existing profiles of this PC are moved into the repository and uploaded. The remote repository should be empty.",
            "Die vorhandenen Profile dieses PCs werden ins Repository übernommen und hochgeladen. Das Remote-Repository sollte leer sein.")));
        mode.Controls.Add(_modeClone);
        mode.Controls.Add(Ui.Text(Loc.T(
            "The repository of another PC is downloaded. Local profile folders are backed up as ZIP first and then " +
            "replaced by the profiles from the repository.",
            "Das Repository eines anderen PCs wird heruntergeladen. Lokale Profilordner werden vorher als ZIP gesichert " +
            "und dann durch die Profile aus dem Repository ersetzt.")));
        _pages.Add(new Page(Loc.T("Setup type", "Einrichtungsart"), mode, () => true, () => true));

        // 3. Profile folders
        var folders = Ui.Column();
        folders.Controls.Add(_detected);
        folders.Controls.Add(Ui.Text(Loc.T("Back up these profile folders:", "Diese Profilordner sichern:"), bold: true));
        folders.Controls.Add(_folders);
        folders.Controls.Add(Ui.Button(Loc.T("Add custom folder…", "Eigenen Ordner hinzufügen…"), (_, _) => AddCustomFolder()));
        folders.Controls.Add(_secretWarning);
        _pages.Add(new Page(Loc.T("Profile folders", "Profilordner"), folders, () => IsNewMode, ValidateFolders, LoadFolders));

        // 4. Repository
        var repo = Ui.Column();
        var grid = Ui.Grid();
        Ui.AddRow(grid, Loc.T("Repository folder:", "Repository-Ordner:"), _repoPicker);
        Ui.AddRow(grid, Loc.T("Remote URL:", "Remote-URL:"), _remoteUrl);
        Ui.AddRow(grid, "Branch:", _branch);
        repo.Controls.Add(grid);
        repo.Controls.Add(_createButtons);
        repo.Controls.Add(Ui.Text(Loc.T(
            "Example: https://github.com/name/slicer-profiles.git – or let SlicerSync create the repository.",
            "Beispiel: https://github.com/name/slicer-profiles.git – oder das Repository von SlicerSync anlegen lassen.")));
        repo.Controls.Add(Ui.Warning(Loc.T(
            "The repository should be private – profiles can contain credentials.",
            "Das Repository sollte privat sein – Profile können Zugangsdaten enthalten.")));
        repo.Controls.Add(Ui.Text(Loc.T(
            "SlicerSync does not store credentials. On first access the Git Credential Manager opens a sign-in window " +
            "(e.g. browser sign-in to GitHub) and remembers the sign-in securely in Windows.",
            "Zugangsdaten speichert SlicerSync nicht. Beim ersten Zugriff öffnet der Git Credential Manager ein " +
            "Anmeldefenster (z. B. Browser-Anmeldung bei GitHub) und merkt sich die Anmeldung sicher in Windows.")));
        _pages.Add(new Page("Repository", repo, () => true, ValidateRepository, () => _createButtons.Visible = IsNewMode));

        // 5. Summary + run
        var run = Ui.Column();
        run.Controls.Add(_summary);
        run.Controls.Add(_slicerStatus);
        run.Controls.Add(_output);
        _pages.Add(new Page(Loc.T("Summary", "Zusammenfassung"), run, () => true, () => true, PrepareSummary));
    }

    // ------------------------------------------------------------ Navigation

    private void Navigate(int direction)
    {
        var i = _index + direction;
        while (i >= 0 && i < _pages.Count && !_pages[i].IsVisible())
            i += direction;
        if (i >= 0 && i < _pages.Count)
            ShowPage(i);
    }

    private void ShowPage(int index)
    {
        _index = index;
        var page = _pages[index];
        page.OnEnter?.Invoke();
        _header.Text = page.Title;
        _host.Controls.Clear();
        _host.Controls.Add(page.Content);
        _back.Enabled = index > 0 && !_done;
        _next.Text = IsLastPage
            ? (_done ? Loc.T("Finish", "Fertig") : Loc.T("Set up", "Einrichten"))
            : Loc.T("Next >", "Weiter >");
        AcceptButton = _next;
    }

    private bool IsLastPage => _index == _pages.Count - 1;

    private async Task OnNext()
    {
        if (_done)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        if (!_pages[_index].CanLeave())
            return;
        if (IsLastPage)
            await Execute();
        else
            Navigate(+1);
    }

    // ------------------------------------------------------------ Page logic

    private void ChangeLanguage()
    {
        var code = ((LanguageItem)_language.SelectedItem!).Code;
        if (code == (Loc.IsGerman ? Loc.German : Loc.English))
            return;
        var settings = _services.Settings.Clone();
        settings.Language = code;
        _services.ApplySettings(settings);
        Loc.SetLanguage(code);
        LanguageChanged = true;
        Close();
    }

    private void UpdateGitStatus()
    {
        var ok = _services.RefreshGit();
        _gitStatus.Text = ok
            ? Loc.T($"✔ Git found: {_services.Git!.GitExe}", $"✔ Git gefunden: {_services.Git!.GitExe}")
            : Loc.T(
                "✖ Git was not found. SlicerSync needs Git for Windows (including the Git Credential Manager). " +
                "The installer normally installs it. Please install Git and click \"Check again\".",
                "✖ Git wurde nicht gefunden. SlicerSync benötigt Git for Windows (inkl. Git Credential Manager). " +
                "Normalerweise installiert das Setup Git mit. Bitte Git installieren und dann „Erneut prüfen“ klicken.");
        _gitStatus.ForeColor = ok ? Ui.Good : Ui.Bad;
        _gitLink.Visible = !ok;
    }

    private void LoadFolders()
    {
        if (_foldersLoaded)
            return;
        _foldersLoaded = true;

        var detection = _services.Detector.Detect();
        var lines = detection.Slicers.Select(s => $"{s.Definition.DisplayName}: " + (s.Installed, s.ProfileRootExists) switch
        {
            (true, true) => Loc.T("installed, profiles found", "installiert, Profile gefunden"),
            (false, true) => Loc.T("profiles found", "Profile gefunden"),
            (true, false) => Loc.T($"installed, but no profile folder at {s.ProfileRoot}",
                                   $"installiert, aber kein Profilordner unter {s.ProfileRoot}"),
            _ => Loc.T("not found", "nicht gefunden"),
        }).ToList();
        if (detection.Slicers.Any(s => s.Installed && !s.ProfileRootExists))
            lines.Add(Loc.T(
                "Tip: in the slicer, \"Help → Show Configuration Folder\" shows where it keeps its profiles. " +
                "A different folder can be added with \"Add custom folder…\".",
                "Tipp: Im Slicer zeigt „Hilfe → Konfigurationsordner anzeigen“, wo er seine Profile ablegt. " +
                "Ein anderer Ordner lässt sich mit „Eigenen Ordner hinzufügen…“ aufnehmen."));
        _detected.Text = string.Join("\n", lines);

        var repoPath = string.IsNullOrEmpty(_services.Settings.RepoPath) ? _repoPath.Text : _services.Settings.RepoPath;
        var known = _services.Settings.Mappings;
        var all = detection.Candidates.Concat(known.Where(k => k.IsCustom)).ToList();
        foreach (var m in all)
        {
            var stored = known.FirstOrDefault(k => k.SameFolderAs(m));
            _folders.Items.Add(new MappingItem(m, SetupService.GetState(m, repoPath)), stored?.Enabled ?? true);
        }
        UpdateSecretWarning();
    }

    private void AddCustomFolder()
    {
        var existing = _folders.Items.Cast<MappingItem>().Select(i => i.Mapping).ToList();
        using var dlg = new CustomFolderForm(_services, _repoPath.Text.Trim(), existing);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
            _folders.Items.Add(new MappingItem(dlg.Result, SetupService.GetState(dlg.Result, _repoPath.Text.Trim())), true);
    }

    private IReadOnlyList<ProfileMapping> SelectedMappings() =>
        _folders.CheckedItems.Cast<MappingItem>().Select(i => i.Mapping).ToList();

    private void UpdateSecretWarning() =>
        _secretWarning.Visible = SelectedMappings().Any(m => m.MayContainSecrets);

    private bool ValidateFolders()
    {
        if (_folders.CheckedItems.Count == 0)
        {
            Ui.Error(this, Loc.T("Please select at least one profile folder.", "Bitte mindestens einen Profilordner auswählen."));
            return false;
        }
        if (_folders.CheckedItems.Cast<MappingItem>().Any(i => i.State == MappingState.ForeignJunction))
        {
            Ui.Error(this, Loc.T(
                "At least one selected folder is already linked to another location. Please deselect it or check the link.",
                "Mindestens ein ausgewählter Ordner ist bereits mit einem anderen Ort verknüpft. Bitte abwählen oder die Verknüpfung prüfen."));
            return false;
        }
        return true;
    }

    private void CreateRemote(RemoteProvider provider)
    {
        using var dlg = new CreateRemoteForm(_services, provider);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        _remoteUrl.Text = dlg.ResultUrl;
        _createOnPushUrl = dlg.CreateOnPush ? dlg.ResultUrl : null;
    }

    private bool ValidateRepository()
    {
        var path = _repoPath.Text.Trim();
        if (path.Length == 0 || !Path.IsPathFullyQualified(path))
        {
            Ui.Error(this, Loc.T("Please enter a full path for the repository folder.", "Bitte einen vollständigen Pfad für den Repository-Ordner angeben."));
            return false;
        }
        if (string.IsNullOrWhiteSpace(_branch.Text))
        {
            Ui.Error(this, Loc.T("Please enter a branch (usually: main).", "Bitte einen Branch angeben (üblich: main)."));
            return false;
        }
        var url = _remoteUrl.Text.Trim();
        if (!IsNewMode && url.Length == 0)
        {
            Ui.Error(this, Loc.T("The second-PC mode needs the remote URL.", "Für den Zweiter-PC-Modus wird die Remote-URL benötigt."));
            return false;
        }
        if (IsNewMode && url.Length == 0 && !Ui.Confirm(this, Loc.T(
                "Without a remote URL the profiles are only versioned locally and not uploaded. Continue anyway?",
                "Ohne Remote-URL werden die Profile nur lokal versioniert und nicht hochgeladen. Trotzdem fortfahren?")))
            return false;
        return true;
    }

    private void PrepareSummary()
    {
        var lines = new List<string> { Loc.T("The following will happen:", "Folgendes wird ausgeführt:") };
        var url = _remoteUrl.Text.Trim();
        if (IsNewMode)
        {
            lines.Add(Loc.T("1. ZIP backup of the selected profile folders", "1. ZIP-Sicherung der ausgewählten Profilordner"));
            lines.Add(Loc.T($"2. Move the folders to {_repoPath.Text.Trim()} and create junctions",
                            $"2. Ordner nach {_repoPath.Text.Trim()} verschieben und Junctions anlegen"));
            lines.Add(url.Length > 0
                ? Loc.T("3. Create the Git repository, make the first commit and upload it", "3. Git-Repository anlegen, ersten Commit erstellen und hochladen")
                : Loc.T("3. Create the Git repository and make the first commit", "3. Git-Repository anlegen und ersten Commit erstellen"));
            lines.Add("");
            lines.Add(Loc.T($"Selected: {SelectedMappings().Count} folders", $"Ausgewählt: {SelectedMappings().Count} Ordner"));
            if (SelectedMappings().Any(m => m.IsCustom))
                lines.Add(Loc.T("Please close the programs of your custom folders now.", "Bitte jetzt auch die Programme deiner eigenen Ordner schließen."));
        }
        else
        {
            lines.Add(Loc.T($"1. Clone the repository to {_repoPath.Text.Trim()}", $"1. Repository nach {_repoPath.Text.Trim()} klonen"));
            lines.Add(Loc.T("2. Back up existing local profile folders as ZIP", "2. Vorhandene lokale Profilordner als ZIP sichern"));
            lines.Add(Loc.T("3. Replace the local profile folders with junctions to the repository", "3. Lokale Profilordner durch Junctions auf das Repository ersetzen"));
        }
        _summary.Text = string.Join("\n", lines);
        UpdateSlicerStatus();
    }

    private bool UpdateSlicerStatus()
    {
        var running = _services.Guard.RunningSlicers(IsNewMode ? SelectedMappings() : null);
        _slicerStatus.Text = running.Count == 0
            ? Loc.T("✔ No slicer is open.", "✔ Kein Slicer geöffnet.")
            : Loc.T($"✖ Please close first: {string.Join(", ", running)}", $"✖ Bitte zuerst schließen: {string.Join(", ", running)}");
        _slicerStatus.ForeColor = running.Count == 0 ? Ui.Good : Ui.Bad;
        return running.Count == 0;
    }

    // ------------------------------------------------------------ Run

    private async Task Execute()
    {
        if (!UpdateSlicerStatus())
        {
            Ui.Error(this, _slicerStatus.Text.TrimStart('✖', ' '));
            return;
        }

        var settings = _services.Settings.Clone();
        settings.RepoPath = Path.GetFullPath(_repoPath.Text.Trim());
        settings.RemoteUrl = _remoteUrl.Text.Trim();
        settings.Branch = _branch.Text.Trim();
        var selected = IsNewMode ? SelectedMappings() : Array.Empty<ProfileMapping>();
        var newMode = IsNewMode;
        var createOnPush = settings.RemoteUrl == _createOnPushUrl;

        _output.Clear();
        try
        {
            var setup = _services.CreateSetup();
            var result = await Ui.RunBusy(this, () => _services.Exclusive(() =>
                newMode ? setup.RunNewSetup(settings, selected, createOnPush) : setup.RunCloneSetup(settings)));

            _services.ApplySettings(settings);
            if (result.BackupZip != null)
                Append(Loc.T($"Backup: {result.BackupZip}", $"Sicherung: {result.BackupZip}"));
            foreach (var w in result.Warnings)
                Append(Loc.T("Note: ", "Hinweis: ") + w);
            Append("");
            Append(Loc.T(
                "✔ Setup complete. SlicerSync now runs in the notification area of the taskbar.",
                "✔ Einrichtung abgeschlossen. SlicerSync läuft jetzt im Infobereich der Taskleiste."));
            _done = true;
            _cancel.Visible = false;
            ShowPage(_index);
        }
        catch (Exception ex)
        {
            _services.Log.Error(Loc.T("Setup failed", "Einrichtung fehlgeschlagen"), ex);
            Ui.Error(this, Loc.T("Setup failed:\n\n", "Die Einrichtung ist fehlgeschlagen:\n\n") + Ui.Describe(ex) +
                           Loc.T("\n\nNothing was deleted. Backups are in:\n", "\n\nEs wurde nichts gelöscht. Sicherungen liegen unter:\n") +
                           _services.Backups.BackupDir);
        }
    }

    private void OnLogMessage(Core.Logging.LogLevel level, string message)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(() => Append(level == Core.Logging.LogLevel.Info ? message : $"{level}: {message}"));
    }

    private void Append(string line) => _output.AppendText(line + Environment.NewLine);
}
