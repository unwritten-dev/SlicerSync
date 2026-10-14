using SlicerSync.Core;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;

namespace SlicerSync.App.Forms;

/// <summary>Copies the profiles of a slicer account into the folder used without sign-in ("default").</summary>
public sealed class CopyProfilesForm : Form
{
    private sealed record SourceItem(SlicerDefinition Slicer, ProfileSetInfo Set)
    {
        public override string ToString() =>
            $"{Slicer.DisplayName} – {Set.Name}  ({Set.FileCount} {Loc.T("files", "Dateien")})";
    }

    private readonly AppServices _services;
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 440 };

    public CopyProfilesForm(AppServices services)
    {
        _services = services;
        Ui.InitDialog(this, Loc.T("Use account profiles without sign-in", "Konto-Profile ohne Anmeldung nutzen"), 610, 360);

        foreach (var slicer in SlicerCatalog.All.Where(s => s.Layout == ProfileLayout.UserSets))
            foreach (var set in ProfileSetCopier.AccountSets(services.Settings.RepoPath, slicer))
                _source.Items.Add(new SourceItem(slicer, set));
        if (_source.Items.Count > 0)
            _source.SelectedIndex = 0;

        var col = Ui.Column();
        col.Controls.Add(Ui.Text(Loc.T(
            "Orca Slicer and Bambu Studio keep separate profiles per signed-in account. Without sign-in they use the folder " +
            $"\"{ProfileSetCopier.NoSignInSet}\". This copies the profiles of an account into \"{ProfileSetCopier.NoSignInSet}\", " +
            "so you can use them on every PC without signing in.",
            "Orca Slicer und Bambu Studio führen pro angemeldetem Konto eigene Profile. Ohne Anmeldung verwenden sie den Ordner " +
            $"„{ProfileSetCopier.NoSignInSet}“. Hiermit werden die Profile eines Kontos nach „{ProfileSetCopier.NoSignInSet}“ kopiert, " +
            "damit du sie auf jedem PC ohne Anmeldung nutzen kannst.")));
        col.Controls.Add(Ui.Row(new Label { Text = Loc.T("Copy from:", "Kopieren aus:"), AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, _source));
        col.Controls.Add(Ui.Text(Loc.T(
            "• Account and cloud IDs are removed from the copies; the account folder and your cloud stay untouched.\n" +
            $"• Profiles with the same name in \"{ProfileSetCopier.NoSignInSet}\" are replaced; the old versions are backed up as ZIP first.\n" +
            "• The copy reaches your other PCs with the next sync.\n" +
            "• Afterwards sign out in the slicer on every PC where you want to use these profiles.",
            "• Konto- und Cloud-IDs werden aus den Kopien entfernt; der Konto-Ordner und deine Cloud bleiben unverändert.\n" +
            $"• Gleichnamige Profile in „{ProfileSetCopier.NoSignInSet}“ werden ersetzt; die alten Versionen werden vorher als ZIP gesichert.\n" +
            "• Die Kopie erreicht deine anderen PCs mit dem nächsten Sync.\n" +
            "• Danach im Slicer auf jedem PC abmelden, auf dem du diese Profile nutzen willst.")));
        if (_source.Items.Count == 0)
            col.Controls.Add(Ui.Warning(Loc.T("No account profiles were found in the repository.", "Im Repository wurden keine Konto-Profile gefunden.")));

        var (bar, ok) = Ui.DialogButtons(this, Loc.T("Copy", "Kopieren"));
        ok.Enabled = _source.Items.Count > 0;
        ok.Click += async (_, _) => await OnCopy();
        Controls.Add(col);
        Controls.Add(bar);
    }

    private async Task OnCopy()
    {
        if (_source.SelectedItem is not SourceItem item)
            return;
        try
        {
            var copier = _services.CreateCopier();
            var settings = _services.Settings;
            var result = await Ui.RunBusy(this, () => _services.Exclusive(() => copier.CopyToNoSignIn(settings, item.Slicer, item.Set.Name)));
            // Back up and distribute right away instead of waiting for the next scheduled sync.
            _ = Task.Run(() => _services.Sync?.Sync());
            Ui.Info(this, Loc.T(
                $"Done: {result.Added} new, {result.Updated} updated, {result.Unchanged} unchanged.\n\n" +
                $"Now sign out in {item.Slicer.DisplayName} (or restart it without sign-in) to see the profiles.",
                $"Fertig: {result.Added} neu, {result.Updated} aktualisiert, {result.Unchanged} unverändert.\n\n" +
                $"Jetzt in {item.Slicer.DisplayName} abmelden (oder ohne Anmeldung neu starten), um die Profile zu sehen.") +
                (result.BackupZip != null ? Loc.T($"\n\nReplaced versions backed up in:\n{result.BackupZip}", $"\n\nErsetzte Versionen gesichert in:\n{result.BackupZip}") : ""));
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _services.Log.Error(Loc.T("Copying profiles failed", "Profile konnten nicht kopiert werden"), ex);
            Ui.Error(this, Ui.Describe(ex));
        }
    }
}
