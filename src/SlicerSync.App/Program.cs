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
                MessageBox.Show("SlicerSync läuft bereits. Das Symbol befindet sich im Infobereich der Taskleiste.",
                    Ui.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var services = new AppServices();
        services.Log.Info("SlicerSync gestartet.");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            services.Log.Error("Unerwarteter Fehler", e.Exception);
            Ui.Error(null, "Unerwarteter Fehler:\n" + e.Exception.Message + "\n\nDetails stehen im Protokoll.");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            services.Log.Error("Unerwarteter Fehler (Hintergrund)", e.ExceptionObject as Exception);

        Application.Run(new TrayContext(services, args.Contains(Autostart.Argument)));
        services.Log.Info("SlicerSync beendet.");
    }
}
