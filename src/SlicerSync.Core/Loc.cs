using System.Globalization;

namespace SlicerSync.Core;

/// <summary>
/// Bilingual UI text (English/German). Every visible string is written as a pair right where it is
/// used: <c>Loc.T("Settings", "Einstellungen")</c>. English is the default; the language is set once at startup.
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string German = "de";

    public static bool IsGerman { get; private set; }

    /// <summary>"de" selects German, anything else English.</summary>
    public static void SetLanguage(string? language) =>
        IsGerman = string.Equals(language, German, StringComparison.OrdinalIgnoreCase);

    public static string T(string english, string german) => IsGerman ? german : english;

    public static string DateTime(System.DateTime value) =>
        value.ToString(IsGerman ? "dd.MM.yyyy HH:mm" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
