using SlicerSync.Core.Git;

namespace SlicerSync.App.Forms;

/// <summary>Letzte Commits mit geänderten Dateien; einzelne Datei auf einen älteren Stand zurücksetzen.</summary>
public sealed class HistoryForm : Form
{
    private readonly AppServices _services;
    private readonly ListView _commits = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
    };
    private readonly ListView _files = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
    };
    private readonly Button _restore = new() { Text = "Datei auf diesen Stand zurücksetzen…", AutoSize = true, Enabled = false };

    public HistoryForm(AppServices services)
    {
        _services = services;
        Text = "SlicerSync – Verlauf";
        Icon = TrayIcons.LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(600, 400);
        Font = SystemFonts.MessageBoxFont;

        _commits.Columns.Add("Datum", 130);
        _commits.Columns.Add("Autor", 160);
        _commits.Columns.Add("Nachricht", 420);
        _commits.Columns.Add("Commit", 80);
        _files.Columns.Add("Änderung", 90);
        _files.Columns.Add("Datei", 680);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        Load += (_, _) => split.SplitterDistance = split.Height / 2;
        split.Panel1.Controls.Add(_commits);
        split.Panel2.Controls.Add(_files);

        var refresh = new Button { Text = "Aktualisieren", AutoSize = true };
        var close = new Button { Text = "Schließen", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { close, refresh, _restore });

        Controls.Add(split);
        Controls.Add(buttons);
        CancelButton = close;

        _commits.SelectedIndexChanged += (_, _) => ShowFiles();
        _files.SelectedIndexChanged += (_, _) => UpdateRestoreButton();
        _files.DoubleClick += async (_, _) => await Restore();
        _restore.Click += async (_, _) => await Restore();
        refresh.Click += async (_, _) => await LoadCommits();
        Shown += async (_, _) => await LoadCommits();
    }

    private async Task LoadCommits()
    {
        try
        {
            var history = _services.CreateHistory();
            var commits = await Ui.RunBusy(this, () => history.GetCommits(200));
            _commits.BeginUpdate();
            _commits.Items.Clear();
            foreach (var c in commits)
            {
                var item = new ListViewItem(c.Date.LocalDateTime.ToString("dd.MM.yyyy HH:mm")) { Tag = c };
                item.SubItems.Add(c.Author);
                item.SubItems.Add(c.Message);
                item.SubItems.Add(c.ShortHash);
                _commits.Items.Add(item);
            }
            _commits.EndUpdate();
            if (_commits.Items.Count > 0)
                _commits.Items[0].Selected = true;
            else
                _files.Items.Clear();
        }
        catch (Exception ex)
        {
            _services.Log.Error("Verlauf konnte nicht geladen werden", ex);
            Ui.Error(this, "Der Verlauf konnte nicht geladen werden:\n" + Ui.Describe(ex));
        }
    }

    private CommitInfo? SelectedCommit => _commits.SelectedItems.Count > 0 ? (CommitInfo)_commits.SelectedItems[0].Tag! : null;
    private ChangedFile? SelectedFile => _files.SelectedItems.Count > 0 ? (ChangedFile)_files.SelectedItems[0].Tag! : null;

    private void ShowFiles()
    {
        _files.BeginUpdate();
        _files.Items.Clear();
        if (SelectedCommit is { } commit)
        {
            foreach (var f in commit.Files)
            {
                var item = new ListViewItem(f.StatusText) { Tag = f };
                item.SubItems.Add(f.OldPath != null ? $"{f.OldPath} → {f.Path}" : f.Path);
                _files.Items.Add(item);
            }
        }
        _files.EndUpdate();
        UpdateRestoreButton();
    }

    private void UpdateRestoreButton() => _restore.Enabled = SelectedFile != null;

    private async Task Restore()
    {
        if (SelectedCommit is not { } commit || SelectedFile is not { } file)
            return;

        var stateText = file.Status == "D"
            ? "den Stand vor dem Löschen"
            : $"den Stand vom {commit.Date.LocalDateTime:dd.MM.yyyy HH:mm}";
        var running = _services.Guard.RunningSlicers();
        var warning = running.Count > 0
            ? $"\n\nAchtung: {string.Join(", ", running)} ist geöffnet. Bitte den Slicer nach dem Zurücksetzen neu starten, " +
              "sonst überschreibt er die Datei eventuell beim nächsten Speichern."
            : "";
        if (!Ui.Confirm(this, $"„{file.Path}“ auf {stateText} zurücksetzen?\n\nDie aktuelle Version wird vorher als ZIP gesichert.{warning}"))
            return;

        try
        {
            var history = _services.CreateHistory();
            var backup = await Ui.RunBusy(this, () => _services.Exclusive(() => history.RestoreFile(commit.Hash, file.Path)));
            Ui.Info(this, "Die Datei wurde zurückgesetzt. Die Änderung wird beim nächsten Sync gesichert." +
                          (backup != null ? $"\n\nVorherige Version gesichert in:\n{backup}" : ""));
        }
        catch (Exception ex)
        {
            _services.Log.Error("Datei konnte nicht zurückgesetzt werden", ex);
            Ui.Error(this, Ui.Describe(ex));
        }
    }
}
