using System.Diagnostics;
using SlicerSync.Core.Git;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;

namespace SlicerSync.App;

/// <summary>Kleine Helfer für einheitliche Dialoge und Layouts.</summary>
public static class Ui
{
    public const string Title = "SlicerSync";

    public static void Error(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirm(IWin32Window? owner, string text, MessageBoxIcon icon = MessageBoxIcon.Question) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Verständliche Meldung für Fehler aus der Kernbibliothek.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        SlicerRunningException s => $"Bitte zuerst {string.Join(" und ", s.Slicers)} schließen und dann erneut versuchen.",
        SetupException or GitException => ex.Message,
        UnauthorizedAccessException => "Zugriff verweigert: " + ex.Message,
        IOException => "Dateifehler: " + ex.Message,
        _ => ex.Message,
    };

    /// <summary>Führt eine Aufgabe im Hintergrund aus; das Formular ist währenddessen gesperrt.</summary>
    public static async Task<T> RunBusy<T>(Form form, Func<T> work)
    {
        var previous = form.Cursor;
        form.UseWaitCursor = true;
        form.Enabled = false;
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            form.Enabled = true;
            form.UseWaitCursor = false;
            form.Cursor = previous;
        }
    }

    public static void Open(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Error(null, $"Konnte nicht geöffnet werden: {pathOrUrl}\n{ex.Message}"); }
    }

    public static Label Text(string text, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        Margin = new Padding(0, 4, 0, 6),
        Font = bold ? new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) : SystemFonts.MessageBoxFont,
        ForeColor = color ?? SystemColors.ControlText,
    };

    public static Label Warning(string text) => Text("⚠ " + text, color: Color.FromArgb(180, 83, 9));

    public static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(12),
    };

    /// <summary>Zweispaltiges Raster: Beschriftung | Eingabe.</summary>
    public static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 4),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        return grid;
    }

    public static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        grid.RowCount++;
        grid.Controls.Add(l, 0, grid.RowCount - 1);
        grid.Controls.Add(control, 1, grid.RowCount - 1);
    }

    /// <summary>Textfeld mit „Durchsuchen…“-Knopf für Ordner.</summary>
    public static (Control Panel, TextBox Box) FolderPicker(string initial)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var box = new TextBox { Text = initial, Dock = DockStyle.Fill };
        var button = new Button { Text = "Durchsuchen…", AutoSize = true };
        button.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = box.Text, ShowNewFolderButton = true };
            if (dlg.ShowDialog() == DialogResult.OK)
                box.Text = dlg.SelectedPath;
        };
        panel.Controls.Add(box, 0, 0);
        panel.Controls.Add(button, 1, 0);
        return (panel, box);
    }
}
