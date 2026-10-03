using SlicerSync.Core.Config;
using SlicerSync.Core.Git;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Sync;

namespace SlicerSync.App.Forms;

public sealed class SettingsForm : Form
{
    private sealed class FolderItem
    {
        public FolderItem(ProfileMapping mapping, bool active) { Mapping = mapping; Active = active; }
        public ProfileMapping Mapping { get; }
        /// <summary>Ordner ist aktuell eingerichtet (aktiv + Junction vorhanden).</summary>
        public bool Active { get; }
        public override string ToString() => Mapping.DisplayName;
    }

    private readonly AppServices _services;
    private readonly AppSettings _work;

    private readonly CheckedListBox _folders = new() { Width = 520, Height = 200, CheckOnClick = true, IntegralHeight = false };
    private readonly Label _secretWarning = Ui.Warning(
        "„physical_printer“ kann API-Schlüssel und Passwörter enthalten. Das Repository muss privat sein.");

    private readonly TextBox _repoPath = new() { ReadOnly = true };
    private readonly TextBox _remoteUrl = new();
    private readonly TextBox _branch = new() { ReadOnly = true, Width = 120 };

    private readonly RadioButton _modeOnChange = new() { Text = "Kurz nach einer Dateiänderung", AutoSize = true };
    private readonly RadioButton _modeInterval = new() { Text = "In festem Intervall", AutoSize = true };
    private readonly RadioButton _modeDaily = new() { Text = "Täglich zu fester Uhrzeit", AutoSize = true };
    private readonly RadioButton _modeManual = new() { Text = "Nur manuell", AutoSize = true };
    private readonly NumericUpDown _debounce = new() { Minimum = 5, Maximum = 3600, Width = 80 };
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 1440, Width = 80 };
    private readonly DateTimePicker _dailyTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    private readonly CheckBox _syncOnStartup = new() { Text = "Beim Start synchronisieren", AutoSize = true };

    private readonly CheckBox _autostart = new() { Text = "Mit Windows starten", AutoSize = true };
    private readonly CheckBox _notifySuccess = new() { Text = "Benachrichtigung bei erfolgreicher Sicherung", AutoSize = true };
    private readonly CheckBox _notifyError = new() { Text = "Benachrichtigung bei Fehlern und Konflikten", AutoSize = true };
    private readonly TextBox _template = new();
    private readonly Label _templatePreview = Ui.Text("");
    private readonly TextBox _gitPath = new();

    public SettingsForm(AppServices services)
    {
        _services = services;
        _work = services.Settings.Clone();

        Text = "SlicerSync – Einstellungen";
        Icon = TrayIcons.LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 470);
        Font = SystemFonts.MessageBoxFont;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(ProfilesTab());
        tabs.TabPages.Add(RepositoryTab());
        tabs.TabPages.Add(SyncTab());
        tabs.TabPages.Add(GeneralTab());

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += async (_, _) => await Save();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        Controls.Add(tabs);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        LoadValues();
    }

    private TabPage ProfilesTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Diese Profilordner werden gesichert:", bold: true));
        col.Controls.Add(_folders);
        col.Controls.Add(_secretWarning);
        col.Controls.Add(Ui.Text(
            "Neu angehakte Ordner werden (nach ZIP-Sicherung) ins Repository verschoben und verlinkt. " +
            "Abgewählte Ordner werden an ihren Originalort zurückkopiert; ihr Stand im Repository bleibt erhalten. " +
            "Dafür müssen die Slicer geschlossen sein."));
        _folders.ItemCheck += (_, _) => BeginInvoke(UpdateSecretWarning);
        return new TabPage("Profile") { Controls = { col } };
    }

    private TabPage RepositoryTab()
    {
        var col = Ui.Column();
        var grid = Ui.Grid();
        var pathPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var open = new Button { Text = "Öffnen", AutoSize = true };
        open.Click += (_, _) => Ui.Open(_work.RepoPath);
        _repoPath.Dock = DockStyle.Fill;
        pathPanel.Controls.Add(_repoPath, 0, 0);
        pathPanel.Controls.Add(open, 1, 0);
        Ui.AddRow(grid, "Repository-Ordner:", pathPanel);
        Ui.AddRow(grid, "Remote-URL:", _remoteUrl);
        Ui.AddRow(grid, "Branch:", _branch);
        col.Controls.Add(grid);
        col.Controls.Add(Ui.Text(
            "Ordner und Branch lassen sich nach der Einrichtung nicht direkt ändern. Dafür „Rückbau“ ausführen und neu einrichten."));
        col.Controls.Add(Ui.Warning("Das Repository sollte privat sein."));
        col.Controls.Add(Ui.Text("Zugangsdaten verwaltet der Git Credential Manager; SlicerSync speichert keine Passwörter oder Tokens."));
        return new TabPage("Repository") { Controls = { col } };
    }

    private TabPage SyncTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text("Wann soll gesichert werden?", bold: true));
        col.Controls.Add(_modeOnChange);
        col.Controls.Add(Indented("Wartezeit nach der letzten Änderung (Sekunden):", _debounce));
        col.Controls.Add(_modeInterval);
        col.Controls.Add(Indented("Alle … Minuten:", _interval));
        col.Controls.Add(_modeDaily);
        col.Controls.Add(Indented("Uhrzeit:", _dailyTime));
        col.Controls.Add(_modeManual);
        col.Controls.Add(new Label { Height = 8 });
        col.Controls.Add(_syncOnStartup);
        col.Controls.Add(Ui.Text("„Jetzt synchronisieren“ im Tray-Menü funktioniert in jedem Modus."));

        void UpdateEnabled()
        {
            _debounce.Enabled = _modeOnChange.Checked;
            _interval.Enabled = _modeInterval.Checked;
            _dailyTime.Enabled = _modeDaily.Checked;
        }
        foreach (var rb in new[] { _modeOnChange, _modeInterval, _modeDaily, _modeManual })
            rb.CheckedChanged += (_, _) => UpdateEnabled();
        Load += (_, _) => UpdateEnabled();
        return new TabPage("Synchronisation") { Controls = { col } };
    }

    private TabPage GeneralTab()
    {
        var col = Ui.Column();
        col.Controls.Add(_autostart);
        col.Controls.Add(_notifySuccess);
        col.Controls.Add(_notifyError);
        col.Controls.Add(new Label { Height = 8 });
        col.Controls.Add(Ui.Text("Vorlage für die Commit-Nachricht:", bold: true));
        _template.Width = 520;
        col.Controls.Add(_template);
        col.Controls.Add(Ui.Text("Platzhalter: " + string.Join("  ", CommitMessage.Placeholders)));
        col.Controls.Add(_templatePreview);
        _template.TextChanged += (_, _) => _templatePreview.Text = "Beispiel: " +
            CommitMessage.Format(_template.Text, DateTime.Now, Environment.MachineName, Environment.UserName, 3);
        col.Controls.Add(new Label { Height = 8 });
        var grid = Ui.Grid();
        Ui.AddRow(grid, "Pfad zu git.exe (optional):", _gitPath);
        col.Controls.Add(grid);
        col.Controls.Add(Ui.Text("Leer lassen, um Git automatisch zu finden."));
        return new TabPage("Allgemein") { Controls = { col } };
    }

    private static Control Indented(string label, Control control)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(20, 0, 0, 6), WrapContents = false };
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        panel.Controls.Add(control);
        return panel;
    }

    private void LoadValues()
    {
        // Profile: gespeicherte Zuordnungen + neu erkannte Ordner
        var all = _work.Mappings.Select(m => m.Clone()).ToList();
        foreach (var c in _services.Detector.Detect().Candidates)
            if (!all.Any(m => m.SameFolderAs(c)))
                all.Add(c);
        foreach (var m in all.OrderBy(m => m.RepoRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var active = m.Enabled && _work.Mappings.Any(x => x.SameFolderAs(m)) &&
                         SetupService.GetState(m, _work.RepoPath) == MappingState.Linked;
            _folders.Items.Add(new FolderItem(m, active), active);
        }
        UpdateSecretWarning();

        _repoPath.Text = _work.RepoPath;
        _remoteUrl.Text = _work.RemoteUrl;
        _branch.Text = _work.Branch;

        _modeOnChange.Checked = _work.SyncMode == SyncMode.OnChange;
        _modeInterval.Checked = _work.SyncMode == SyncMode.Interval;
        _modeDaily.Checked = _work.SyncMode == SyncMode.Daily;
        _modeManual.Checked = _work.SyncMode == SyncMode.Manual;
        _debounce.Value = Math.Clamp(_work.DebounceSeconds, (int)_debounce.Minimum, (int)_debounce.Maximum);
        _interval.Value = Math.Clamp(_work.IntervalMinutes, (int)_interval.Minimum, (int)_interval.Maximum);
        _dailyTime.Value = DateTime.Today + _work.GetDailyTime();
        _syncOnStartup.Checked = _work.SyncOnStartup;

        _autostart.Checked = _work.Autostart;
        _notifySuccess.Checked = _work.NotifyOnSuccess;
        _notifyError.Checked = _work.NotifyOnError;
        _template.Text = _work.CommitMessageTemplate;
        _gitPath.Text = _work.GitPath;
    }

    private void UpdateSecretWarning() =>
        _secretWarning.Visible = _folders.CheckedItems.Cast<FolderItem>().Any(i => i.Mapping.MayContainSecrets);

    private async Task Save()
    {
        var gitPath = _gitPath.Text.Trim();
        if (gitPath.Length > 0 && !File.Exists(gitPath))
        {
            Ui.Error(this, $"git.exe wurde unter „{gitPath}“ nicht gefunden.");
            return;
        }

        _work.RemoteUrl = _remoteUrl.Text.Trim();
        _work.SyncMode = _modeOnChange.Checked ? SyncMode.OnChange
            : _modeInterval.Checked ? SyncMode.Interval
            : _modeDaily.Checked ? SyncMode.Daily
            : SyncMode.Manual;
        _work.DebounceSeconds = (int)_debounce.Value;
        _work.IntervalMinutes = (int)_interval.Value;
        _work.DailyTime = _dailyTime.Value.ToString("HH:mm");
        _work.SyncOnStartup = _syncOnStartup.Checked;
        _work.Autostart = _autostart.Checked;
        _work.NotifyOnSuccess = _notifySuccess.Checked;
        _work.NotifyOnError = _notifyError.Checked;
        _work.CommitMessageTemplate = string.IsNullOrWhiteSpace(_template.Text) ? AppSettings.DefaultCommitTemplate : _template.Text.Trim();
        _work.GitPath = gitPath;

        var items = _folders.Items.Cast<FolderItem>().ToList();
        var toAdd = items.Where((it, i) => _folders.GetItemChecked(i) && !it.Active).Select(it => it.Mapping).ToList();
        var toRemove = items.Where((it, i) => !_folders.GetItemChecked(i) && it.Active).Select(it => it.Mapping).ToList();

        if (_work.SetupCompleted && (toAdd.Count > 0 || toRemove.Count > 0))
        {
            var text = "Folgende Änderungen an den Profilordnern werden ausgeführt:\n";
            if (toAdd.Count > 0)
                text += "\nNeu sichern (ZIP-Sicherung, verschieben, verlinken):\n  " + string.Join("\n  ", toAdd.Select(m => m.DisplayName));
            if (toRemove.Count > 0)
                text += "\n\nNicht mehr sichern (an Originalort zurückkopieren):\n  " + string.Join("\n  ", toRemove.Select(m => m.DisplayName));
            if (!Ui.Confirm(this, text + "\n\nFortfahren?"))
                return;

            try
            {
                var setup = _services.CreateSetup();
                await Ui.RunBusy(this, () => _services.Exclusive(() =>
                {
                    if (toRemove.Count > 0)
                        setup.RemoveMappings(_work, toRemove);
                    if (toAdd.Count > 0)
                        setup.AddMappings(_work, toAdd);
                    return true;
                }));
            }
            catch (Exception ex)
            {
                // Bereits erfolgreich geänderte Ordner trotzdem festhalten.
                _services.ApplySettings(_work.Clone());
                _services.Log.Error("Profilordner konnten nicht geändert werden", ex);
                Ui.Error(this, Ui.Describe(ex));
                return;
            }
        }

        try
        {
            if (_work.SetupCompleted && _services.Git != null)
                new GitRepository(_services.Git, _work.RepoPath).SetRemote(_work.RemoteUrl);
            _services.ApplySettings(_work);
            _services.RefreshGit();
        }
        catch (Exception ex)
        {
            _services.Log.Error("Einstellungen konnten nicht gespeichert werden", ex);
            Ui.Error(this, Ui.Describe(ex));
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
