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
        services.Log.Info(Loc.T("SlicerSync started.", "SlicerSync gestartet."));

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            services.Log.Error(Loc.T("Unexpected error", "Unerwarteter Fehler"), e.Exception);
            Ui.Error(null, Loc.T("Unexpected error:\n", "Unerwarteter Fehler:\n") + e.Exception.Message +
                           Loc.T("\n\nDetails are in the log.", "\n\nDetails stehen im Protokoll."));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            services.Log.Error(Loc.T("Unexpected error (background)", "Unerwarteter Fehler (Hintergrund)"), e.ExceptionObject as Exception);

        Application.Run(new TrayContext(services, args.Contains(Autostart.Argument)));
        services.Log.Info(Loc.T("SlicerSync exited.", "SlicerSync beendet."));
    }
}
