using SlicerSync.Core;
using SlicerSync.Core.Git;

namespace SlicerSync.App.Forms;

/// <summary>Recent commits with changed files; restore a single file to an older state.</summary>
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
    private readonly Button _restore = new() { Text = Loc.T("Restore file to this state…", "Datei auf diesen Stand zurücksetzen…"), AutoSize = true, Enabled = false };

    public HistoryForm(AppServices services)
    {
        _services = services;
        Text = Loc.T("SlicerSync – History", "SlicerSync – Verlauf");
        Icon = TrayIcons.LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(600, 400);
        Font = SystemFonts.MessageBoxFont;

        _commits.Columns.Add(Loc.T("Date", "Datum"), 130);
        _commits.Columns.Add(Loc.T("Author", "Autor"), 160);
        _commits.Columns.Add(Loc.T("Message", "Nachricht"), 420);
        _commits.Columns.Add("Commit", 80);
        _files.Columns.Add(Loc.T("Change", "Änderung"), 90);
        _files.Columns.Add(Loc.T("File", "Datei"), 680);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        Load += (_, _) => split.SplitterDistance = split.Height / 2;
        split.Panel1.Controls.Add(_commits);
        split.Panel2.Controls.Add(_files);

        var refresh = new Button { Text = Loc.T("Refresh", "Aktualisieren"), AutoSize = true };
        var close = new Button { Text = Loc.T("Close", "Schließen"), AutoSize = true, DialogResult = DialogResult.Cancel };
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
                var item = new ListViewItem(Loc.DateTime(c.Date.LocalDateTime)) { Tag = c };
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
            _services.Log.Error(Loc.T("History could not be loaded", "Verlauf konnte nicht geladen werden"), ex);
            Ui.Error(this, Loc.T("The history could not be loaded:\n", "Der Verlauf konnte nicht geladen werden:\n") + Ui.Describe(ex));
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
            ? Loc.T("the state before it was deleted", "den Stand vor dem Löschen")
            : Loc.T($"the state of {Loc.DateTime(commit.Date.LocalDateTime)}", $"den Stand vom {Loc.DateTime(commit.Date.LocalDateTime)}");
        var running = _services.Guard.RunningSlicers();
        var warning = running.Count > 0
            ? Loc.T(
                $"\n\nCaution: {string.Join(", ", running)} is open. Please restart the slicer after restoring, " +
                "otherwise it may overwrite the file the next time it saves.",
                $"\n\nAchtung: {string.Join(", ", running)} ist geöffnet. Bitte den Slicer nach dem Zurücksetzen neu starten, " +
                "sonst überschreibt er die Datei eventuell beim nächsten Speichern.")
            : "";
        if (!Ui.Confirm(this, Loc.T(
                $"Restore \"{file.Path}\" to {stateText}?\n\nThe current version is backed up as ZIP first.{warning}",
                $"„{file.Path}“ auf {stateText} zurücksetzen?\n\nDie aktuelle Version wird vorher als ZIP gesichert.{warning}")))
            return;

        try
        {
            var history = _services.CreateHistory();
            var backup = await Ui.RunBusy(this, () => _services.Exclusive(() => history.RestoreFile(commit.Hash, file.Path)));
            Ui.Info(this, Loc.T(
                "The file was restored. The change is backed up on the next sync.",
                "Die Datei wurde zurückgesetzt. Die Änderung wird beim nächsten Sync gesichert.") +
                (backup != null ? Loc.T($"\n\nPrevious version saved in:\n{backup}", $"\n\nVorherige Version gesichert in:\n{backup}") : ""));
        }
        catch (Exception ex)
        {
            _services.Log.Error(Loc.T("File could not be restored", "Datei konnte nicht zurückgesetzt werden"), ex);
            Ui.Error(this, Ui.Describe(ex));
        }
    }
}
