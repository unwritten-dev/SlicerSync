using System.Globalization;

namespace SlicerSync.Core.Sync;

/// <summary>
/// Placeholders for the commit message. English and German names both work:
/// {date}/{datum} {time}/{zeit} {computer}/{rechner} {user}/{benutzer} {count}/{anzahl}.
/// </summary>
public static class CommitMessage
{
    public static string[] Placeholders => Loc.IsGerman
        ? new[] { "{datum}", "{zeit}", "{rechner}", "{benutzer}", "{anzahl}" }
        : new[] { "{date}", "{time}", "{computer}", "{user}", "{count}" };

    public static string DefaultTemplate => Loc.T(
        "Profile sync {date} {time} from {computer} ({count} files)",
        "Profil-Sync {datum} {zeit} von {rechner} ({anzahl} Dateien)");

    public static string Format(string? template, DateTime now, string machine, string user, int fileCount)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = DefaultTemplate;

        var values = new (string[] Names, string Value)[]
        {
            (new[] { "{date}", "{datum}" }, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            (new[] { "{time}", "{zeit}" }, now.ToString("HH:mm", CultureInfo.InvariantCulture)),
            (new[] { "{computer}", "{rechner}" }, machine),
            (new[] { "{user}", "{benutzer}" }, user),
            (new[] { "{count}", "{anzahl}" }, fileCount.ToString(CultureInfo.InvariantCulture)),
        };

        var text = template;
        foreach (var (names, value) in values)
            foreach (var name in names)
                text = text.Replace(name, value, StringComparison.OrdinalIgnoreCase);
        text = text.Trim();
        return text.Length > 0 ? text : "Profile sync";
    }
}
