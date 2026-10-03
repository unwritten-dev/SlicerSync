using System.Globalization;

namespace SlicerSync.Core.Sync;

/// <summary>Platzhalter für die Commit-Nachricht: {datum} {zeit} {rechner} {benutzer} {anzahl}.</summary>
public static class CommitMessage
{
    public static readonly string[] Placeholders = { "{datum}", "{zeit}", "{rechner}", "{benutzer}", "{anzahl}" };

    public static string Format(string? template, DateTime now, string machine, string user, int fileCount)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = Config.AppSettings.DefaultCommitTemplate;

        var de = CultureInfo.GetCultureInfo("de-DE");
        var text = template
            .Replace("{datum}", now.ToString("yyyy-MM-dd", de), StringComparison.OrdinalIgnoreCase)
            .Replace("{zeit}", now.ToString("HH:mm", de), StringComparison.OrdinalIgnoreCase)
            .Replace("{rechner}", machine, StringComparison.OrdinalIgnoreCase)
            .Replace("{benutzer}", user, StringComparison.OrdinalIgnoreCase)
            .Replace("{anzahl}", fileCount.ToString(de), StringComparison.OrdinalIgnoreCase)
            .Trim();
        return text.Length > 0 ? text : "Profil-Sync";
    }
}
