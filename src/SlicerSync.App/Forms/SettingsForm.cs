using SlicerSync.Core;
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
        /// <summary>The folder is currently set up (enabled + junction present).</summary>
        public bool Active { get; }
        public override string ToString() => Mapping.DisplayName;
    }

    private sealed record LanguageItem(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly AppServices _services;
    private readonly AppSettings _work;

    private readonly CheckedListBox _folders = new() { Width = 540, Height = 190, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Label _secretWarning = Ui.Warning(Loc.T(
        "\"physical_printer\" can contain API keys and passwords. The repository must be private.",
        "„physical_printer“ kann API-Schlüssel und Passwörter enthalten. Das Repository muss privat sein."));

    private readonly TextBox _repoPath = new() { ReadOnly = true };
    private readonly TextBox _remoteUrl = new();
    private readonly TextBox _branch = new() { ReadOnly = true, Width = 120 };

    private readonly RadioButton _modeOnChange = new() { Text = Loc.T("Shortly after a file change", "Kurz nach einer Dateiänderung"), AutoSize = true };
    private readonly RadioButton _modeInterval = new() { Text = Loc.T("At a fixed interval", "In festem Intervall"), AutoSize = true };
    private readonly RadioButton _modeDaily = new() { Text = Loc.T("Daily at a fixed time", "Täglich zu fester Uhrzeit"), AutoSize = true };
    private readonly RadioButton _modeManual = new() { Text = Loc.T("Manually only", "Nur manuell"), AutoSize = true };
    private readonly NumericUpDown _debounce = new() { Minimum = 5, Maximum = 3600, Width = 80 };
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 1440, Width = 80 };
    private readonly DateTimePicker _dailyTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    private readonly CheckBox _syncOnStartup = new() { Text = Loc.T("Sync on startup", "Beim Start synchronisieren"), AutoSize = true };

    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _autostart = new() { Text = Loc.T("Start with Windows", "Mit Windows starten"), AutoSize = true };
    private readonly CheckBox _notifySuccess = new() { Text = Loc.T("Notify after a successful backup", "Benachrichtigung bei erfolgreicher Sicherung"), AutoSize = true };
    private readonly CheckBox _notifyError = new() { Text = Loc.T("Notify on errors and conflicts", "Benachrichtigung bei Fehlern und Konflikten"), AutoSize = true };
    private readonly TextBox _template = new();
    private readonly Label _templatePreview = Ui.Text("");
    private readonly TextBox _gitPath = new();

    public SettingsForm(AppServices services)
    {
        _services = services;
        _work = services.Settings.Clone();

        Ui.InitDialog(this, Loc.T("SlicerSync – Settings", "SlicerSync – Einstellungen"), 620, 500);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(ProfilesTab());
        tabs.TabPages.Add(RepositoryTab());
        tabs.TabPages.Add(SyncTab());
        tabs.TabPages.Add(GeneralTab());

        var (bar, ok) = Ui.DialogButtons(this, "OK");
        ok.Click += async (_, _) => await Save();
        Controls.Add(tabs);
        Controls.Add(bar);

        LoadValues();
    }

    private TabPage ProfilesTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text(Loc.T("These profile folders are backed up:", "Diese Profilordner werden gesichert:"), bold: true));
        col.Controls.Add(_folders);
        col.Controls.Add(Ui.Button(Loc.T("Add custom folder…", "Eigenen Ordner hinzufügen…"), (_, _) => AddCustomFolder()));
        col.Controls.Add(_secretWarning);
        col.Controls.Add(Ui.Text(Loc.T(
            "Newly checked folders are moved into the repository and linked (after a ZIP backup). Unchecked folders are " +
            "copied back to their original location; their state in the repository is kept. The slicers must be closed.",
            "Neu angehakte Ordner werden (nach ZIP-Sicherung) ins Repository verschoben und verlinkt. Abgewählte Ordner " +
            "werden an ihren Originalort zurückkopiert; ihr Stand im Repository bleibt erhalten. Dafür müssen die Slicer geschlossen sein.")));
        // ItemCheck fires before the state changes (and also while the list is filled, before the
        // window exists), so compute with the new value directly instead of deferring.
        _folders.ItemCheck += (_, e) => UpdateSecretWarning(e.Index, e.NewValue);
        return new TabPage(Loc.T("Profiles", "Profile")) { Controls = { col } };
    }

    private TabPage RepositoryTab()
    {
        var col = Ui.Column();
        var grid = Ui.Grid();
        var pathPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _repoPath.Dock = DockStyle.Fill;
        pathPanel.Controls.Add(_repoPath, 0, 0);
        pathPanel.Controls.Add(Ui.Button(Loc.T("Open", "Öffnen"), (_, _) => Ui.Open(_work.RepoPath)), 1, 0);
        Ui.AddRow(grid, Loc.T("Repository folder:", "Repository-Ordner:"), pathPanel);
        Ui.AddRow(grid, Loc.T("Remote URL:", "Remote-URL:"), _remoteUrl);
        Ui.AddRow(grid, "Branch:", _branch);
        col.Controls.Add(grid);
        col.Controls.Add(Ui.Text(Loc.T(
            "Folder and branch cannot be changed directly after setup. To change them, run \"Restore\" and set up again.",
            "Ordner und Branch lassen sich nach der Einrichtung nicht direkt ändern. Dafür „Rückbau“ ausführen und neu einrichten.")));
        col.Controls.Add(Ui.Warning(Loc.T("The repository should be private.", "Das Repository sollte privat sein.")));
        col.Controls.Add(Ui.Text(Loc.T(
            "Credentials are handled by the Git Credential Manager; SlicerSync does not store passwords or tokens.",
            "Zugangsdaten verwaltet der Git Credential Manager; SlicerSync speichert keine Passwörter oder Tokens.")));
        return new TabPage("Repository") { Controls = { col } };
    }

    private TabPage SyncTab()
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Text(Loc.T("When should SlicerSync back up?", "Wann soll gesichert werden?"), bold: true));
        col.Controls.Add(_modeOnChange);
        col.Controls.Add(Indented(Loc.T("Wait after the last change (seconds):", "Wartezeit nach der letzten Änderung (Sekunden):"), _debounce));
        col.Controls.Add(_modeInterval);
        col.Controls.Add(Indented(Loc.T("Every … minutes:", "Alle … Minuten:"), _interval));
        col.Controls.Add(_modeDaily);
        col.Controls.Add(Indented(Loc.T("Time:", "Uhrzeit:"), _dailyTime));
        col.Controls.Add(_modeManual);
        col.Controls.Add(new Label { Height = 8 });
        col.Controls.Add(_syncOnStartup);
        col.Controls.Add(Ui.Text(Loc.T(
            "\"Sync now\" in the tray menu works in every mode.",
            "„Jetzt synchronisieren“ im Tray-Menü funktioniert in jedem Modus.")));

        void UpdateEnabled()
        {
            _debounce.Enabled = _modeOnChange.Checked;
            _interval.Enabled = _modeInterval.Checked;
            _dailyTime.Enabled = _modeDaily.Checked;
        }
        foreach (var rb in new[] { _modeOnChange, _modeInterval, _modeDaily, _modeManual })
            rb.CheckedChanged += (_, _) => UpdateEnabled();
        Load += (_, _) => UpdateEnabled();
        return new TabPage(Loc.T("Sync", "Synchronisation")) { Controls = { col } };
    }

    private TabPage GeneralTab()
    {
        var col = Ui.Column();
        _language.Items.Add(new LanguageItem(Loc.English, "English"));
        _language.Items.Add(new LanguageItem(Loc.German, "Deutsch"));
        col.Controls.Add(Ui.Row(new Label { Text = "Language / Sprache:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, _language));
        col.Controls.Add(_autostart);
        col.Controls.Add(_notifySuccess);
        col.Controls.Add(_notifyError);
        col.Controls.Add(new Label { Height = 8 });
        col.Controls.Add(Ui.Text(Loc.T("Commit message template:", "Vorlage für die Commit-Nachricht:"), bold: true));
        _template.Width = 540;
        col.Controls.Add(_template);
        col.Controls.Add(Ui.Text(Loc.T("Placeholders: ", "Platzhalter: ") + string.Join("  ", CommitMessage.Placeholders)));
        col.Controls.Add(_templatePreview);
        _template.TextChanged += (_, _) => _templatePreview.Text = Loc.T("Example: ", "Beispiel: ") +
            CommitMessage.Format(_template.Text, DateTime.Now, Environment.MachineName, Environment.UserName, 3);
        col.Controls.Add(new Label { Height = 8 });
        var grid = Ui.Grid();
        Ui.AddRow(grid, Loc.T("Path to git.exe (optional):", "Pfad zu git.exe (optional):"), _gitPath);
        col.Controls.Add(grid);
        col.Controls.Add(Ui.Text(Loc.T("Leave empty to find Git automatically.", "Leer lassen, um Git automatisch zu finden.")));
        return new TabPage(Loc.T("General", "Allgemein")) { Controls = { col } };
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
        // Profiles: stored mappings + newly detected folders
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

        _language.SelectedIndex = Loc.IsGerman ? 1 : 0;
        _autostart.Checked = _work.Autostart;
        _notifySuccess.Checked = _work.NotifyOnSuccess;
        _notifyError.Checked = _work.NotifyOnError;
        _template.Text = string.IsNullOrWhiteSpace(_work.CommitMessageTemplate) ? CommitMessage.DefaultTemplate : _work.CommitMessageTemplate;
        _gitPath.Text = _work.GitPath;
    }

    /// <param name="changedIndex">Item whose check state is about to change (from ItemCheck), or -1.</param>
    private void UpdateSecretWarning(int changedIndex = -1, CheckState newValue = CheckState.Unchecked) =>
        _secretWarning.Visible = _folders.Items.Cast<FolderItem>()
            .Where((_, i) => i == changedIndex ? newValue == CheckState.Checked : _folders.GetItemChecked(i))
            .Any(item => item.Mapping.MayContainSecrets);

    private void AddCustomFolder()
    {
        var existing = _folders.Items.Cast<FolderItem>().Select(i => i.Mapping).ToList();
        using var dlg = new CustomFolderForm(_services, _work.RepoPath, existing);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
            _folders.Items.Add(new FolderItem(dlg.Result, active: false), true);
    }

    private async Task Save()
    {
        var gitPath = _gitPath.Text.Trim();
        if (gitPath.Length > 0 && !File.Exists(gitPath))
        {
            Ui.Error(this, Loc.T($"git.exe was not found at \"{gitPath}\".", $"git.exe wurde unter „{gitPath}“ nicht gefunden."));
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
        _work.Language = ((LanguageItem)_language.SelectedItem!).Code;
        _work.Autostart = _autostart.Checked;
        _work.NotifyOnSuccess = _notifySuccess.Checked;
        _work.NotifyOnError = _notifyError.Checked;
        // Store the default as empty, so it follows the language.
        var template = _template.Text.Trim();
        _work.CommitMessageTemplate = template.Length == 0 || template == CommitMessage.DefaultTemplate ? "" : template;
        _work.GitPath = gitPath;

        var items = _folders.Items.Cast<FolderItem>().ToList();
        var toAdd = items.Where((it, i) => _folders.GetItemChecked(i) && !it.Active).Select(it => it.Mapping).ToList();
        var toRemove = items.Where((it, i) => !_folders.GetItemChecked(i) && it.Active).Select(it => it.Mapping).ToList();

        if (_work.SetupCompleted && (toAdd.Count > 0 || toRemove.Count > 0))
        {
            var text = Loc.T("The following changes to the profile folders will be made:\n", "Folgende Änderungen an den Profilordnern werden ausgeführt:\n");
            if (toAdd.Count > 0)
                text += Loc.T("\nStart backing up (ZIP backup, move, link):\n  ", "\nNeu sichern (ZIP-Sicherung, verschieben, verlinken):\n  ") +
                        string.Join("\n  ", toAdd.Select(m => m.DisplayName));
            if (toRemove.Count > 0)
                text += Loc.T("\n\nStop backing up (copy back to original location):\n  ", "\n\nNicht mehr sichern (an Originalort zurückkopieren):\n  ") +
                        string.Join("\n  ", toRemove.Select(m => m.DisplayName));
            if (toAdd.Concat(toRemove).Any(m => m.IsCustom))
                text += Loc.T("\n\nPlease close the programs of custom folders first.", "\n\nBitte die Programme eigener Ordner vorher schließen.");
            if (!Ui.Confirm(this, text + Loc.T("\n\nContinue?", "\n\nFortfahren?")))
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
                // Keep track of folders that were already changed successfully.
                _services.ApplySettings(_work.Clone());
                _services.Log.Error(Loc.T("Profile folders could not be changed", "Profilordner konnten nicht geändert werden"), ex);
                Ui.Error(this, Ui.Describe(ex));
                return;
            }
        }

        try
        {
            if (_work.SetupCompleted && _services.Git != null)
                new GitRepository(_services.Git, _work.RepoPath).SetRemote(_work.RemoteUrl);
            _services.ApplySettings(_work);
            Loc.SetLanguage(_work.Language);
            _services.RefreshGit();
        }
        catch (Exception ex)
        {
            _services.Log.Error(Loc.T("Settings could not be saved", "Einstellungen konnten nicht gespeichert werden"), ex);
            Ui.Error(this, Ui.Describe(ex));
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
