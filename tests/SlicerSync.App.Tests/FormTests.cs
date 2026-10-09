using System.Reflection;
using SlicerSync.App;
using SlicerSync.App.Forms;
using SlicerSync.Core;
using SlicerSync.Core.Config;

namespace SlicerSync.App.Tests;

/// <summary>
/// Opens every window with realistic profile data (in temporary folders). Catches UI errors that only
/// show up when lists are filled, e.g. events firing before a window handle exists.
/// Switches the global language, so these tests must not run in parallel.
/// </summary>
[CollectionDefinition(nameof(UiCollection), DisableParallelization = true)]
public sealed class UiCollection { }

[Collection(nameof(UiCollection))]
public sealed class FormTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SlicerSyncUiTests", Guid.NewGuid().ToString("N")[..12]);
    private readonly AppServices _services;

    public FormTests()
    {
        var paths = new AppPaths(
            Path.Combine(_root, "Roaming"),
            Path.Combine(_root, "Local"),
            Path.Combine(_root, "Home"),
            new[] { Path.Combine(_root, "ProgramFiles") });

        // Profiles like on a real PC: Orca with several users, Prusa including physical_printer.
        foreach (var set in new[] { "4288834837", "default" })
            foreach (var folder in new[] { "filament", "machine", "process" })
                Write(Path.Combine(paths.RoamingAppData, "OrcaSlicer", "user", set, folder), "x.json");
        foreach (var folder in new[] { "print", "filament", "printer", "physical_printer" })
            Write(Path.Combine(paths.RoamingAppData, "PrusaSlicer", folder), "x.ini");

        _services = new AppServices(paths, () => new[] { "OrcaSlicer", "PrusaSlicer 2.9.2" });

        // A real setup (junctions + local Git repository, no remote) in the temporary folders, so the
        // settings window fills its folder list with checked items like on a real PC.
        var settings = _services.Settings.Clone();
        settings.RepoPath = paths.DefaultRepoPath;
        settings.RemoteUrl = "";
        settings.Autostart = false;
        _services.CreateSetup().RunNewSetup(settings, _services.Detector.Detect().Candidates);
        _services.Store.Save(settings);
        _services = new AppServices(paths, () => new[] { "OrcaSlicer", "PrusaSlicer 2.9.2" });
    }

    private static void Write(string dir, string file)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), "x");
    }

    public void Dispose()
    {
        Loc.SetLanguage(Loc.English);
        try
        {
            // Remove the junctions first (never follow them), then the rest; .git files are read-only.
            var all = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
            foreach (var dir in Directory.EnumerateDirectories(_root, "*", all)
                         .Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
                Directory.Delete(dir, false);
            foreach (var file in Directory.EnumerateFiles(_root, "*", all))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (Exception)
        {
            // Cleanup is best effort.
        }
    }

    /// <summary>WinForms needs an STA thread; exceptions are passed on to the test.</summary>
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw new Exception("UI error: " + error.Message, error);
    }

    private static void ShowAndClose(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-5000, -5000);
        form.Show();
        Application.DoEvents();
        form.Close();
        form.Dispose();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void SettingsForm_OpensWithCheckedProfileFolders(string language) => OnSta(() =>
    {
        Loc.SetLanguage(language);
        var form = new SettingsForm(_services);
        var folders = FindAll<CheckedListBox>(form).Single();
        Assert.Equal(10, folders.Items.Count);
        Assert.Equal(10, folders.CheckedItems.Count);
        ShowAndClose(form);
    });

    [Fact]
    public void SettingsForm_ShowsSecretWarning_OnlyWhilePhysicalPrinterIsChecked() => OnSta(() =>
    {
        var form = new SettingsForm(_services);
        form.Show();
        var folders = FindAll<CheckedListBox>(form).Single();
        var warning = FindAll<Label>(form).Single(l => l.Text.Contains("physical_printer"));
        var index = Enumerable.Range(0, folders.Items.Count).Single(i => folders.Items[i]!.ToString()!.Contains("physical_printer"));

        Assert.True(warning.Visible);
        folders.SetItemChecked(index, false);
        Assert.False(warning.Visible);
        folders.SetItemChecked(index, true);
        Assert.True(warning.Visible);
        form.Close();
    });

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void SetupWizard_ShowsAllPages_WithDetectedProfiles(string language) => OnSta(() =>
    {
        Loc.SetLanguage(language);
        var wizard = new SetupWizardForm(_services);
        var showPage = typeof(SetupWizardForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // The profile page also before the window exists (fills the list with checked items).
        showPage.Invoke(wizard, new object[] { 2 });
        Assert.Equal(10, FindAll<CheckedListBox>(wizard).Single().CheckedItems.Count);
        wizard.Show();
        for (var i = 0; i < 5; i++)
        {
            showPage.Invoke(wizard, new object[] { i });
            Application.DoEvents();
        }
        wizard.Close();
    });

    [Fact]
    public void Dialogs_Open() => OnSta(() =>
    {
        ShowAndClose(new CustomFolderForm(_services, _services.Settings.RepoPath, _services.Settings.Mappings));
        ShowAndClose(new CreateRemoteForm(_services, RemoteProvider.GitHub));
        ShowAndClose(new CreateRemoteForm(_services, RemoteProvider.GitLab));
        new HistoryForm(_services).Dispose(); // not shown: it would load the (non-existent) repository
    });

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            if (c is T match)
                yield return match;
            foreach (var nested in FindAll<T>(c))
                yield return nested;
        }
    }
}
