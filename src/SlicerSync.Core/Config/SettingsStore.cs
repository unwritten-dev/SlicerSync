using System.Text.Json;
using System.Text.Json.Serialization;
using SlicerSync.Core.Logging;

namespace SlicerSync.Core.Config;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _file;
    private readonly ILogger _log;

    public SettingsStore(string file, ILogger log)
    {
        _file = file;
        _log = log;
    }

    public string FilePath => _file;

    public AppSettings Load()
    {
        if (!File.Exists(_file))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file), Options) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            // Never overwrite a broken file; move a copy aside instead.
            var broken = _file + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_file, broken, overwrite: true);
            _log.Error(Loc.T(
                $"Settings could not be read; a copy was saved as {broken}. Using defaults.",
                $"Einstellungen konnten nicht gelesen werden, Kopie unter {broken}. Standardwerte werden verwendet."), ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, _file, overwrite: true);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
