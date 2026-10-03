using SlicerSync.App.Forms;
using SlicerSync.Core.Sync;

namespace SlicerSync.App;

/// <summary>Tray-Icon mit Menü, Statusanzeige, Benachrichtigungen und Sync-Planung.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly AppServices _services;
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _tray = new();
    private readonly Control _ui = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _syncNow = new("Jetzt synchronisieren");
    private readonly ToolStripMenuItem _history = new("Verlauf…");
    private readonly ToolStripMenuItem _uninstall = new("Rückbau…");
    private readonly SyncScheduler _scheduler;
    private SyncService? _hookedSync;
    private bool _manualRequest;
    private Form? _openForm;
    private DateTime? _lastSync;
    private SyncResult? _lastResult;

    public TrayContext(AppServices services, bool autostarted)
    {
        _services = services;
        _ui.CreateControl();
        _scheduler = new SyncScheduler(() => _services.Sync?.Sync(), services.Log);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_syncNow);
        menu.Items.Add(_history);
        menu.Items.Add("Einstellungen…", null, (_, _) => ShowSingle(() => new SettingsForm(_services), ApplyConfiguration));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Protokoll öffnen", null, (_, _) => Ui.Open(_services.Log.FilePath));
        menu.Items.Add("Repository-Ordner öffnen", null, (_, _) => Ui.Open(_services.Settings.RepoPath));
        menu.Items.Add("Sicherungen öffnen", null, (_, _) =>
        {
            Directory.CreateDirectory(_services.Backups.BackupDir);
            Ui.Open(_services.Backups.BackupDir);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Einrichtungsassistent…", null, (_, _) => ShowWizard());
        menu.Items.Add(_uninstall);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => UpdateMenu();

        _syncNow.Click += (_, _) => TriggerSync(manual: true);
        _history.Click += (_, _) => ShowSingle(() => new HistoryForm(_services));
        _uninstall.Click += async (_, _) => await Uninstall();

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSingle(() => new HistoryForm(_services));
        _tray.Visible = true;

        _services.SyncReplaced += () => _ui.BeginInvoke(HookSync);
        HookSync();
        ApplyConfiguration();

        if (!_services.Settings.SetupCompleted)
            _ui.BeginInvoke(ShowWizard);
        else if (_services.Settings.SyncOnStartup)
            Task.Delay(TimeSpan.FromSeconds(autostarted ? 20 : 3)).ContinueWith(_ => TriggerSync(manual: false));
    }

    // ------------------------------------------------------------ Status

    private void HookSync()
    {
        var sync = _services.Sync;
        if (sync == _hookedSync)
            return;
        if (_hookedSync != null)
        {
            _hookedSync.Started -= OnSyncStarted;
            _hookedSync.Completed -= OnSyncCompleted;
        }
        _hookedSync = sync;
        if (sync != null)
        {
            sync.Started += OnSyncStarted;
            sync.Completed += OnSyncCompleted;
        }
    }

    private void OnSyncStarted() => _ui.BeginInvoke(() => SetState(TrayState.Running, "Synchronisiere…"));

    private void OnSyncCompleted(SyncResult result) => _ui.BeginInvoke(() =>
    {
        var manual = _manualRequest;
        _manualRequest = false;
        var s = _services.Settings;

        if (result.Outcome == SyncOutcome.Busy)
        {
            if (manual) _tray.ShowBalloonTip(3000, Ui.Title, result.Message, ToolTipIcon.Info);
            return;
        }

        _lastSync = DateTime.Now;
        _lastResult = result;
        var text = result.Message;
        if (result.Warnings.Count > 0)
            text = string.Join("\n", result.Warnings.Prepend(text));

        if (result.IsProblem)
        {
            SetState(TrayState.Error, text);
            if (s.NotifyOnError || manual)
                _tray.ShowBalloonTip(10000, result.Outcome == SyncOutcome.Conflict ? "SlicerSync – Konflikt" : "SlicerSync – Fehler",
                    Shorten(text, 250), ToolTipIcon.Error);
        }
        else
        {
            SetState(TrayState.Ok, text);
            if ((result.Outcome == SyncOutcome.Success && s.NotifyOnSuccess) || manual)
                _tray.ShowBalloonTip(3000, Ui.Title, text, ToolTipIcon.Info);
        }
    });

    private void SetState(TrayState state, string detail)
    {
        _tray.Icon = _icons[state];
        var label = state switch
        {
            TrayState.Ok => "OK",
            TrayState.Running => "Synchronisiere…",
            TrayState.Error => "Fehler",
            _ => "Nicht eingerichtet",
        };
        var when = _lastSync != null && state != TrayState.Running ? $" ({_lastSync:HH:mm})" : "";
        // Tooltip ist auf 127 Zeichen begrenzt.
        _tray.Text = Shorten($"SlicerSync – {label}{when}", 127);
        _status.Text = Shorten($"Status: {label}{when} – {detail}", 120);
        _status.ToolTipText = detail;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private void UpdateMenu()
    {
        var ready = _services.Settings.SetupCompleted && _services.GitAvailable;
        _syncNow.Enabled = ready && _services.Sync?.IsRunning != true;
        _history.Enabled = ready;
        _uninstall.Enabled = _services.Settings.SetupCompleted;
    }

    // ------------------------------------------------------------ Aktionen

    private void TriggerSync(bool manual)
    {
        var sync = _services.Sync;
        if (sync == null || !_services.Settings.SetupCompleted)
            return;
        if (manual)
            _manualRequest = true;
        Task.Run(sync.Sync);
    }

    /// <summary>Übernimmt die aktuellen Einstellungen in Planer und Statusanzeige.</summary>
    private void ApplyConfiguration()
    {
        var s = _services.Settings;
        if (!_services.GitAvailable)
        {
            _scheduler.Apply(new Core.Config.AppSettings { SyncMode = Core.Config.SyncMode.Manual });
            SetState(TrayState.Error, "Git wurde nicht gefunden.");
            return;
        }
        if (!s.SetupCompleted)
        {
            _scheduler.Apply(new Core.Config.AppSettings { SyncMode = Core.Config.SyncMode.Manual });
            SetState(TrayState.NotConfigured, "Bitte den Einrichtungsassistenten ausführen.");
            return;
        }
        _scheduler.Apply(s);

        var warnings = SyncService.CheckJunctions(s);
        if (warnings.Count > 0)
            SetState(TrayState.Error, string.Join("\n", warnings));
        else if (_lastResult is not { IsProblem: true })
            SetState(TrayState.Ok, _lastResult?.Message ?? "Bereit.");
    }

    private void ShowWizard()
    {
        var wasConfigured = _services.Settings.SetupCompleted;
        ShowSingle(() => new SetupWizardForm(_services), () =>
        {
            ApplyConfiguration();
            if (!wasConfigured && _services.Settings.SetupCompleted)
                _tray.ShowBalloonTip(5000, Ui.Title,
                    "Einrichtung abgeschlossen. SlicerSync läuft im Infobereich weiter.", ToolTipIcon.Info);
        });
    }

    private void ShowSingle(Func<Form> create, Action? onOk = null)
    {
        if (_openForm is { IsDisposed: false })
        {
            _openForm.Activate();
            return;
        }
        var form = create();
        _openForm = form;
        form.FormClosed += (_, _) =>
        {
            _openForm = null;
            if (form.DialogResult == DialogResult.OK)
                onOk?.Invoke();
            else
                ApplyConfiguration();
        };
        form.Show();
        form.Activate();
    }

    private async Task Uninstall()
    {
        if (_openForm is { IsDisposed: false })
        {
            _openForm.Activate();
            return;
        }
        var s = _services.Settings;
        if (!Ui.Confirm(null,
                "Rückbau: Alle Junctions werden entfernt und die Profilordner wieder an ihren Originalort kopiert.\n\n" +
                $"Das Repository unter {s.RepoPath} (inkl. Verlauf) bleibt erhalten und kann danach von Hand gelöscht werden.\n" +
                "Die Slicer müssen geschlossen sein.\n\nRückbau jetzt durchführen?", MessageBoxIcon.Warning))
            return;

        var work = s.Clone();
        try
        {
            var setup = _services.CreateSetup();
            var count = await Task.Run(() => _services.Exclusive(() => setup.Uninstall(work)));
            work.Autostart = false;
            _services.ApplySettings(work);
            ApplyConfiguration();
            Ui.Info(null, $"Rückbau abgeschlossen: {count} Ordner wurden an ihren Originalort zurückgelegt.\n\n" +
                          "Autostart wurde deaktiviert. SlicerSync kann jetzt beendet oder deinstalliert werden.");
        }
        catch (Exception ex)
        {
            _services.Log.Error("Rückbau fehlgeschlagen", ex);
            Ui.Error(null, "Rückbau fehlgeschlagen:\n" + Ui.Describe(ex));
        }
    }

    protected override void ExitThreadCore()
    {
        _scheduler.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icons.Dispose();
        base.ExitThreadCore();
    }
}
