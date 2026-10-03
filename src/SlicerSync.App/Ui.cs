using System.Diagnostics;
using SlicerSync.Core;
using SlicerSync.Core.Git;
using SlicerSync.Core.Hosting;
using SlicerSync.Core.Setup;
using SlicerSync.Core.Slicers;

namespace SlicerSync.App;

/// <summary>Small helpers for consistent dialogs and layouts.</summary>
public static class Ui
{
    public const string Title = "SlicerSync";

    public static readonly Color Good = Color.FromArgb(21, 128, 61);
    public static readonly Color Bad = Color.FromArgb(185, 28, 28);

    public static void Error(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirm(IWin32Window? owner, string text, MessageBoxIcon icon = MessageBoxIcon.Question) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Readable message for errors from the core library.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        SlicerRunningException s => Loc.T(
            $"Please close {string.Join(" and ", s.Slicers)} first, then try again.",
            $"Bitte zuerst {string.Join(" und ", s.Slicers)} schließen und dann erneut versuchen."),
        SetupException or GitException or HostingException or GitHubAuthException => ex.Message,
        UnauthorizedAccessException => Loc.T("Access denied: ", "Zugriff verweigert: ") + ex.Message,
        IOException => Loc.T("File error: ", "Dateifehler: ") + ex.Message,
        _ => ex.Message,
    };

    /// <summary>Runs work in the background; the form is disabled meanwhile.</summary>
    public static async Task<T> RunBusy<T>(Form form, Func<T> work)
    {
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
        }
    }

    public static void Open(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Error(null, Loc.T($"Could not open: {pathOrUrl}\n{ex.Message}", $"Konnte nicht geöffnet werden: {pathOrUrl}\n{ex.Message}")); }
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

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.AddRange(controls);
        return row;
    }

    public static Button Button(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    /// <summary>Two-column grid: label | input.</summary>
    public static TableLayoutPanel Grid(int inputWidth = 400)
    {
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 4),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, inputWidth));
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

    /// <summary>Text box with a "Browse…" button for folders.</summary>
    public static (Control Panel, TextBox Box) FolderPicker(string initial)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var box = new TextBox { Text = initial, Dock = DockStyle.Fill };
        var button = new Button { Text = Loc.T("Browse…", "Durchsuchen…"), AutoSize = true };
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

    /// <summary>Standard setup for a fixed-size dialog window.</summary>
    public static void InitDialog(Form form, string title, int width, int height)
    {
        form.Text = title;
        form.Icon = TrayIcons.LoadAppIcon();
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = new Size(width, height);
        form.Font = SystemFonts.MessageBoxFont;
    }

    /// <summary>OK/Cancel bar docked at the bottom of a dialog.</summary>
    public static (FlowLayoutPanel Bar, Button Ok) DialogButtons(Form form, string okText)
    {
        var ok = new Button { Text = okText, AutoSize = true };
        var cancel = new Button { Text = Loc.T("Cancel", "Abbrechen"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        bar.Controls.AddRange(new Control[] { cancel, ok });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return (bar, ok);
    }
}
