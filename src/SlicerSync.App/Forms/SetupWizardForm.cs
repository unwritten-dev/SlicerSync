using SlicerSync.Core.Config;
using SlicerSync.Core.Git;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;

namespace SlicerSync.App.Forms;

/// <summary>Einrichtungsassistent: neu einrichten oder als zweiten PC an ein bestehendes Repository anbinden.</summary>
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
            MappingState.Linked => "   (bereits eingerichtet)",
            MappingState.ForeignJunction => "   (verweist auf anderen Ort!)",
            _ => "",
        };
    }

    private readonly AppServices _services;
    private readonly List<Page> _pages = new();
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 12, 12, 0) };
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private readonly Button _back = new() { Text = "< Zurück", AutoSize = true };
    private readonly Button _next = new() { Text = "Weiter >", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Abbrechen", AutoSize = true, DialogResult = DialogResult.Cancel };
    private int _index;
    private bool _done;

    // Seite Git
    private readonly Label _gitStatus = Ui.Text("");
    private readonly LinkLabel _gitLink = new() { Text = "Git for Windows herunterladen", AutoSize = true, Margin = new Padding(0, 4, 0, 8) };

    // Seite Modus
    private readonly RadioButton _modeNew = new() { Text = "Neu einrichten – dieser PC hat die Profile", AutoSize = true, Checked = true };
    private readonly RadioButton _modeClone = new() { Text = "Zweiter PC – bestehendes Repository klonen", AutoSize = true };

    // Seite Profile
    private readonly Label _detected = Ui.Text("");
    private readonly CheckedListBox _folders = new() { Width = 560, Height = 190, CheckOnClick = true, IntegralHeight = false };
    private readonly Label _secretWarning = Ui.Warning(
        "„physical_printer“ kann API-Schlüssel und Passwörter für PrusaLink/OctoPrint enthalten. " +
        "Das Repository muss unbedingt privat sein.");

    // Seite Repository
    private readonly TextBox _repoPath;
    private readonly Control _repoPicker;
    private readonly TextBox _remoteUrl = new() { Width = 400 };
    private readonly TextBox _branch = new() { Width = 120 };

    // Seite Ausführen
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

        Text = "SlicerSync – Einrichtung";
        Icon = TrayIcons.LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 470);
        Font = SystemFonts.MessageBoxFont;
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

    private bool IsNewMode => _modeNew.Checked;

    private void BuildPages()
    {
        // 1. Git
        var git = Ui.Column();
        git.Controls.Add(Ui.Text(
            "SlicerSync sichert deine selbst erstellten Profile aus Orca Slicer und PrusaSlicer mit Versionsverlauf " +
            "in ein privates Git-Repository (z. B. auf GitHub oder GitLab).\n\n" +
            "Die Profilordner werden dazu in einen Repository-Ordner verschoben und über Verzeichnis-Verknüpfungen " +
            "(Junctions) an den ursprünglichen Ort zurückverlinkt. Die Slicer merken davon nichts.\n\n" +
            "Vor jeder Änderung wird eine ZIP-Sicherung angelegt."));
        git.Controls.Add(_gitStatus);
        git.Controls.Add(_gitLink);
        var recheck = new Button { Text = "Erneut prüfen", AutoSize = true };
        recheck.Click += (_, _) => UpdateGitStatus();
        git.Controls.Add(recheck);
        _pages.Add(new Page("Willkommen", git, () => true, () => _services.GitAvailable, UpdateGitStatus));

        // 2. Modus
        var mode = Ui.Column();
        mode.Controls.Add(_modeNew);
        mode.Controls.Add(Ui.Text("Die vorhandenen Profile dieses PCs werden ins Repository übernommen und hochgeladen. " +
                                  "Das Remote-Repository sollte leer sein."));
        mode.Controls.Add(_modeClone);
        mode.Controls.Add(Ui.Text("Das Repository eines anderen PCs wird heruntergeladen. Lokale Profilordner werden vorher " +
                                  "als ZIP gesichert und dann durch die Profile aus dem Repository ersetzt."));
        _pages.Add(new Page("Einrichtungsart", mode, () => true, () => true));

        // 3. Profilordner
        var folders = Ui.Column();
        folders.Controls.Add(_detected);
        folders.Controls.Add(Ui.Text("Diese Profilordner sichern:", bold: true));
        folders.Controls.Add(_folders);
        folders.Controls.Add(_secretWarning);
        _pages.Add(new Page("Profilordner", folders, () => IsNewMode, ValidateFolders, LoadFolders));

        // 4. Repository
        var repo = Ui.Column();
        var grid = Ui.Grid();
        Ui.AddRow(grid, "Repository-Ordner:", _repoPicker);
        Ui.AddRow(grid, "Remote-URL:", _remoteUrl);
        Ui.AddRow(grid, "Branch:", _branch);
        repo.Controls.Add(grid);
        repo.Controls.Add(Ui.Text("Beispiel: https://github.com/name/slicer-profile.git"));
        repo.Controls.Add(Ui.Warning("Das Repository sollte privat sein – Profile können Zugangsdaten enthalten."));
        repo.Controls.Add(Ui.Text(
            "Zugangsdaten speichert SlicerSync nicht. Beim ersten Zugriff öffnet der Git Credential Manager ein " +
            "Anmeldefenster (z. B. Browser-Anmeldung bei GitHub) und merkt sich die Anmeldung sicher in Windows."));
        _pages.Add(new Page("Repository", repo, () => true, ValidateRepository));

        // 5. Ausführen
        var run = Ui.Column();
        run.Controls.Add(_summary);
        run.Controls.Add(_slicerStatus);
        run.Controls.Add(_output);
        _pages.Add(new Page("Zusammenfassung", run, () => true, () => true, PrepareSummary));
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
        _next.Text = IsLastPage ? (_done ? "Fertig" : "Einrichten") : "Weiter >";
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

    // ------------------------------------------------------------ Seiten-Logik

    private void UpdateGitStatus()
    {
        var ok = _services.RefreshGit();
        _gitStatus.Text = ok
            ? $"✔ Git gefunden: {_services.Git!.GitExe}"
            : "✖ Git wurde nicht gefunden. SlicerSync benötigt Git for Windows (inkl. Git Credential Manager). " +
              "Normalerweise installiert das Setup Git mit. Bitte Git installieren und dann „Erneut prüfen“ klicken.";
        _gitStatus.ForeColor = ok ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28);
        _gitLink.Visible = !ok;
    }

    private void LoadFolders()
    {
        if (_folders.Items.Count > 0)
            return;

        var detection = _services.Detector.Detect();
        var lines = detection.Slicers.Select(s =>
            $"{s.DisplayName}: {(s.Installed ? "installiert" : "nicht gefunden")}, " +
            $"Profilordner {(s.ProfileRootExists ? "vorhanden" : "nicht vorhanden")}");
        _detected.Text = string.Join("\n", lines);

        var repoPath = string.IsNullOrEmpty(_services.Settings.RepoPath) ? _repoPath.Text : _services.Settings.RepoPath;
        var known = _services.Settings.Mappings;
        foreach (var m in detection.Candidates)
        {
            var state = SetupService.GetState(m, repoPath);
            var stored = known.FirstOrDefault(k => k.SameFolderAs(m));
            var check = stored?.Enabled ?? true;
            _folders.Items.Add(new MappingItem(m, state), check);
        }
        if (detection.Candidates.Count == 0)
            _detected.Text += "\n\nEs wurden keine Profilordner gefunden. Bitte zuerst im Slicer ein eigenes Profil speichern.";
        UpdateSecretWarning();
    }

    private IReadOnlyList<ProfileMapping> SelectedMappings() =>
        _folders.CheckedItems.Cast<MappingItem>().Select(i => i.Mapping).ToList();

    private void UpdateSecretWarning() =>
        _secretWarning.Visible = SelectedMappings().Any(m => m.MayContainSecrets);

    private bool ValidateFolders()
    {
        if (_folders.CheckedItems.Count == 0)
        {
            Ui.Error(this, "Bitte mindestens einen Profilordner auswählen.");
            return false;
        }
        if (_folders.CheckedItems.Cast<MappingItem>().Any(i => i.State == MappingState.ForeignJunction))
        {
            Ui.Error(this, "Mindestens ein ausgewählter Ordner ist bereits mit einem anderen Ort verknüpft. Bitte abwählen oder die Verknüpfung prüfen.");
            return false;
        }
        return true;
    }

    private bool ValidateRepository()
    {
        var path = _repoPath.Text.Trim();
        if (path.Length == 0 || !Path.IsPathFullyQualified(path))
        {
            Ui.Error(this, "Bitte einen vollständigen Pfad für den Repository-Ordner angeben.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_branch.Text))
        {
            Ui.Error(this, "Bitte einen Branch angeben (üblich: main).");
            return false;
        }
        var url = _remoteUrl.Text.Trim();
        if (!IsNewMode && url.Length == 0)
        {
            Ui.Error(this, "Für den Zweiter-PC-Modus wird die Remote-URL benötigt.");
            return false;
        }
        if (IsNewMode && url.Length == 0 &&
            !Ui.Confirm(this, "Ohne Remote-URL werden die Profile nur lokal versioniert und nicht hochgeladen. Trotzdem fortfahren?"))
            return false;
        return true;
    }

    private void PrepareSummary()
    {
        var lines = new List<string>();
        if (IsNewMode)
        {
            lines.Add("Folgendes wird ausgeführt:");
            lines.Add("1. ZIP-Sicherung der ausgewählten Profilordner");
            lines.Add($"2. Ordner nach {_repoPath.Text.Trim()} verschieben und Junctions anlegen");
            lines.Add("3. Git-Repository anlegen, ersten Commit erstellen" + (_remoteUrl.Text.Trim().Length > 0 ? " und hochladen" : ""));
            lines.Add("");
            lines.Add($"Ausgewählt: {SelectedMappings().Count} Ordner");
        }
        else
        {
            lines.Add("Folgendes wird ausgeführt:");
            lines.Add($"1. Repository nach {_repoPath.Text.Trim()} klonen");
            lines.Add("2. Vorhandene lokale Profilordner als ZIP sichern");
            lines.Add("3. Lokale Profilordner durch Junctions auf das Repository ersetzen");
        }
        _summary.Text = string.Join("\n", lines);
        UpdateSlicerStatus();
    }

    private bool UpdateSlicerStatus()
    {
        var running = _services.Guard.RunningSlicers();
        _slicerStatus.Text = running.Count == 0
            ? "✔ Kein Slicer geöffnet."
            : $"✖ Bitte zuerst schließen: {string.Join(", ", running)}";
        _slicerStatus.ForeColor = running.Count == 0 ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28);
        return running.Count == 0;
    }

    // ------------------------------------------------------------ Ausführen

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

        _output.Clear();
        try
        {
            var setup = _services.CreateSetup();
            var result = await Ui.RunBusy(this, () => _services.Exclusive(() =>
                newMode ? setup.RunNewSetup(settings, selected) : setup.RunCloneSetup(settings)));

            _services.ApplySettings(settings);
            if (result.BackupZip != null)
                Append($"Sicherung: {result.BackupZip}");
            foreach (var w in result.Warnings)
                Append("Hinweis: " + w);
            Append("");
            Append("✔ Einrichtung abgeschlossen. SlicerSync läuft jetzt im Infobereich der Taskleiste.");
            _done = true;
            _cancel.Visible = false;
            ShowPage(_index);
        }
        catch (Exception ex)
        {
            _services.Log.Error("Einrichtung fehlgeschlagen", ex);
            Ui.Error(this, "Die Einrichtung ist fehlgeschlagen:\n\n" + Ui.Describe(ex) +
                           "\n\nEs wurde nichts gelöscht. Sicherungen liegen unter:\n" + _services.Backups.BackupDir);
        }
    }

    private void OnLogMessage(LogLevel level, string message)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(() => Append(level == LogLevel.Info ? message : $"{level}: {message}"));
    }

    private void Append(string line) => _output.AppendText(line + Environment.NewLine);
}
