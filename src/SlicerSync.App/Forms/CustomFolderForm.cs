using SlicerSync.Core;
using SlicerSync.Core.Config;
using SlicerSync.Core.Setup;

namespace SlicerSync.App.Forms;

/// <summary>Adds any folder (e.g. profiles of another slicer) as a custom folder.</summary>
public sealed class CustomFolderForm : Form
{
    private readonly AppServices _services;
    private readonly string _repoPath;
    private readonly IReadOnlyList<ProfileMapping> _existing;
    private readonly TextBox _name = new();
    private readonly TextBox _path;

    public CustomFolderForm(AppServices services, string repoPath, IReadOnlyList<ProfileMapping> existing)
    {
        _services = services;
        _repoPath = repoPath;
        _existing = existing;
        Ui.InitDialog(this, Loc.T("Add custom folder", "Eigenen Ordner hinzufügen"), 560, 290);

        Control picker;
        (picker, _path) = Ui.FolderPicker(services.Paths.RoamingAppData);

        var col = Ui.Column();
        col.Controls.Add(Ui.Text(Loc.T(
            "Back up the profile folder of any other slicer or program, e.g. Cura, Creality Print or ElegooSlicer.",
            "Sichert den Profilordner eines beliebigen anderen Slicers oder Programms, z. B. Cura, Creality Print oder ElegooSlicer.")));
        var grid = Ui.Grid(380);
        Ui.AddRow(grid, Loc.T("Name:", "Name:"), _name);
        Ui.AddRow(grid, Loc.T("Folder:", "Ordner:"), picker);
        col.Controls.Add(grid);
        col.Controls.Add(Ui.Text(Loc.T(
            "Choose the folder that contains the profiles, not the whole program folder. The name is used as folder name " +
            "in the repository (letters, digits, '-', '_').",
            "Wähle den Ordner, der die Profile enthält, nicht den ganzen Programmordner. Der Name wird als Ordnername im " +
            "Repository verwendet (Buchstaben, Ziffern, „-“, „_“).")));
        col.Controls.Add(Ui.Warning(Loc.T(
            "SlicerSync cannot tell whether this program is running. Close it yourself before you continue.",
            "SlicerSync kann nicht erkennen, ob dieses Programm läuft. Bitte schließe es selbst, bevor du fortfährst.")));

        var (bar, ok) = Ui.DialogButtons(this, Loc.T("Add", "Hinzufügen"));
        ok.Click += (_, _) => OnOk();
        Controls.Add(col);
        Controls.Add(bar);
    }

    public ProfileMapping? Result { get; private set; }

    private void OnOk()
    {
        var name = _name.Text.Trim();
        var path = _path.Text.Trim();
        if (SetupService.ValidateCustomFolder(name, path, _repoPath, _existing) is { } error)
        {
            Ui.Error(this, error);
            return;
        }
        Result = _services.Detector.CreateCustomMapping(name, path);
        DialogResult = DialogResult.OK;
    }
}
