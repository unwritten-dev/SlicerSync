using System.Diagnostics;
using SlicerSync.Core;

namespace SlicerSync.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SlicerSync-SingleInstance", out var isFirst);
        if (!isFirst)
        {
            if (!args.Contains(Autostart.Argument))
                MessageBox.Show(
                    "SlicerSync is already running. Its icon is in the notification area of the taskbar.\n\n" +
                    "SlicerSync läuft bereits. Das Symbol befindet sich im Infobereich der Taskleiste.",
                    Ui.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var services = new AppServices();
        var autostarted = args.Contains(Autostart.Argument);
        services.Log.Info(autostarted
            ? Loc.T("SlicerSync started (Windows startup).", "SlicerSync gestartet (Windows-Start).")
            : Loc.T("SlicerSync started.", "SlicerSync gestartet."));

        if (services.Settings.SetupCompleted && services.Settings.Autostart)
        {
            try
            {
                if (Autostart.Repair())
                    services.Log.Info(Loc.T("Autostart entry was missing or outdated and has been renewed.",
                                            "Autostart-Eintrag fehlte oder war veraltet und wurde erneuert."));
                if (Autostart.IsDisabledByWindows())
                    services.Log.Warn(Loc.T("Autostart is switched off in Windows (Task Manager → Startup apps).",
                                            "Autostart ist in Windows ausgeschaltet (Task-Manager → Autostart)."));
            }
            catch (Exception ex)
            {
                services.Log.Warn(Loc.T("Autostart entry could not be checked", "Autostart-Eintrag konnte nicht geprüft werden"), ex);
            }
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            services.Log.Error(Loc.T("Unexpected error", "Unerwarteter Fehler"), e.Exception);
            Ui.Error(null, Loc.T("Unexpected error:\n", "Unerwarteter Fehler:\n") + e.Exception.Message +
                           Loc.T("\n\nDetails are in the log.", "\n\nDetails stehen im Protokoll."));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            services.Log.Error(Loc.T("Unexpected error (background)", "Unerwarteter Fehler (Hintergrund)"), e.ExceptionObject as Exception);

        var tray = new TrayContext(services, autostarted);
        Application.Run(tray);
        services.Log.Info(Loc.T("SlicerSync exited.", "SlicerSync beendet."));

        // Portable update: start the new EXE only after releasing the single-instance lock.
        if (tray.RestartExe is { } exe)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
    }
}
