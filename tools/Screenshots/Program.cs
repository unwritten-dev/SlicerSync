using System.Diagnostics;
using System.Reflection;
using SlicerSync.App;
using SlicerSync.App.Forms;
using SlicerSync.Core;
using SlicerSync.Core.Git;

// Renders the screenshots in docs/images with made-up demo data in a temporary folder, so no real
// paths, computer or user names end up in the README. Usage (from the repository root):
//   dotnet run --project tools/Screenshots
static class Program
{
    private const string Machine = "WORKSHOP-PC";

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var output = Path.GetFullPath(Path.Combine("docs", "images"));
        Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), "SlicerSyncScreenshots", Guid.NewGuid().ToString("N")[..8]);

        // Neutral identity for the demo commits.
        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Maker");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "maker@example.com");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Maker");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "maker@example.com");

        try
        {
            var (wizardServices, settingsServices) = CreateDemo(root);
            foreach (var language in new[] { Loc.English, Loc.German })
            {
                Loc.SetLanguage(language);
                Shoot(output, language, wizardServices, settingsServices);
            }
            Console.WriteLine($"Screenshots written to {output}");
        }
        finally
        {
            Loc.SetLanguage(Loc.English);
            Cleanup(root);
        }
    }

    private static (AppServices Wizard, AppServices Settings) CreateDemo(string root)
    {
        var paths = new AppPaths(Path.Combine(root, "Roaming"), Path.Combine(root, "Local"), Path.Combine(root, "Home"), new[] { root });
        var installed = () => (IEnumerable<string>)new[] { "OrcaSlicer", "Bambu Studio", "PrusaSlicer 2.9.2" };
        var running = () => (IEnumerable<string>)Array.Empty<string>();

        void Profiles(string roaming)
        {
            void Profile(string dir, string name) => Write(Path.Combine(roaming, dir), name, "{}");
            Profile(@"OrcaSlicer\user\default\filament", "Generic PETG @Voron Trident 350.json");
            Profile(@"OrcaSlicer\user\default\machine", "Voron Trident 350 0.6 nozzle.json");
            Profile(@"OrcaSlicer\user\default\process", "0.30mm Standard @Voron.json");
            Profile(@"BambuStudio\user\default\filament", "PLA Matte @BBL A1 mini.json");
            Profile(@"BambuStudio\user\default\machine", "Bambu Lab A1 mini 0.4 nozzle.json");
            Profile(@"BambuStudio\user\default\process", "0.20mm Strength @BBL A1M.json");
            Profile(@"PrusaSlicer\print", "0.15mm QUALITY MK4.ini");
            Profile(@"PrusaSlicer\filament", "Prusament PETG.ini");
            Profile(@"PrusaSlicer\printer", "Original Prusa MK4.ini");
            Profile(@"PrusaSlicer\physical_printer", "MK4 workshop.ini");
        }
        Profiles(paths.RoamingAppData);

        // Wizard: a fresh PC with its own profile folders; the user folder only appears as text, so it
        // may be a neutral example path.
        var wizardPaths = paths with { RoamingAppData = Path.Combine(root, "FreshPc"), UserProfile = @"C:\Users\Maker" };
        Profiles(wizardPaths.RoamingAppData);
        var wizard = new AppServices(wizardPaths, installed, running);

        // Settings and history: a real setup (junctions, Git repository, a few commits) in the temp folder.
        var services = new AppServices(paths, installed, running);
        var settings = services.Settings.Clone();
        settings.RepoPath = paths.DefaultRepoPath;
        settings.RemoteUrl = "";
        settings.Autostart = false;
        settings.BackupLocations = new List<string>();
        services.CreateSetup().RunNewSetup(settings, services.Detector.Detect().Candidates);
        settings.BackupLocations = new List<string> { @"\\nas\backup\slicer", @"G:\My Drive\SlicerSync" };
        services.Store.Save(settings);

        var git = new GitRunner(GitLocator.Find()!);
        void SetDate(string date)
        {
            git.Environment["GIT_AUTHOR_DATE"] = date;
            git.Environment["GIT_COMMITTER_DATE"] = date;
        }
        // The setup commit contains the real computer name: replace it with the demo name.
        SetDate("2026-10-11T20:15:00");
        git.RunChecked(paths.DefaultRepoPath, "commit", "--amend", "-q", "--date", "2026-10-11T20:15:00", "-m", $"Initial setup from {Machine} (10 files)");

        void Commit(string date, string relativeFile, string content, string message)
        {
            Write(Path.Combine(paths.DefaultRepoPath, Path.GetDirectoryName(relativeFile)!), Path.GetFileName(relativeFile), content);
            SetDate(date);
            git.RunChecked(paths.DefaultRepoPath, "add", "-A");
            git.RunChecked(paths.DefaultRepoPath, "commit", "-q", "-m", message);
        }
        Commit("2026-10-12T19:41:00", @"Orca\default\filament\Generic PETG @Voron Trident 350.json", "{\"temp\":245}", $"Profile sync 2026-10-12 19:41 from {Machine} (1 files)");
        Commit("2026-10-13T08:12:00", @"Prusa\print\0.15mm QUALITY MK4.ini", "perimeters = 3", "Profile sync 2026-10-13 08:12 from LAPTOP (1 files)");
        Commit("2026-10-14T17:05:00", @"Bambu\default\process\0.20mm Strength @BBL A1M.json", "{\"walls\":4}", $"Profile sync 2026-10-14 17:05 from {Machine} (1 files)");

        return (wizard, new AppServices(paths, installed, running));
    }

    private static void Shoot(string output, string language, AppServices wizardServices, AppServices services)
    {
        var showPage = typeof(SetupWizardForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var wizard = new SetupWizardForm(wizardServices);
        foreach (var (page, name) in new[] { (0, "wizard-welcome"), (2, "wizard-folders"), (3, "wizard-repository") })
        {
            showPage.Invoke(wizard, new object[] { page });
            Save(wizard, output, language, name);
        }
        wizard.Dispose();

        var settings = new SettingsForm(services);
        var tabs = settings.Controls.OfType<TabControl>().Single();
        foreach (var (tab, name) in new[] { (0, "settings-profiles"), (2, "settings-backup"), (3, "settings-sync") })
        {
            tabs.SelectedIndex = tab;
            Save(settings, output, language, name);
        }
        settings.Dispose();

        var history = new HistoryForm(services) { ClientSize = new Size(820, 420) };
        Save(history, output, language, "history", waitForLoad: true);
        history.Dispose();
    }

    private static void Save(Form form, string output, string language, string name, bool waitForLoad = false)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Show();
        var until = DateTime.Now.AddSeconds(waitForLoad ? 3 : 0.3);
        while (DateTime.Now < until)
            Application.DoEvents();
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        bmp.Save(Path.Combine(output, $"{name}.{language}.png"));
        form.Hide();
    }

    private static void Write(string dir, string file, string content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), content);
    }

    private static void Cleanup(string root)
    {
        try
        {
            var all = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
            foreach (var dir in Directory.EnumerateDirectories(root, "*", all)
                         .Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
                Directory.Delete(dir, false);
            foreach (var file in Directory.EnumerateFiles(root, "*", all))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
