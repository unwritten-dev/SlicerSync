using System.Text;

namespace SlicerSync.Core.Logging;

public enum LogLevel { Info, Warning, Error }

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class LoggerExtensions
{
    public static void Info(this ILogger log, string message) => log.Log(LogLevel.Info, message);
    public static void Warn(this ILogger log, string message, Exception? ex = null) => log.Log(LogLevel.Warning, message, ex);
    public static void Error(this ILogger log, string message, Exception? ex = null) => log.Log(LogLevel.Error, message, ex);
}

/// <summary>Schreibt in eine Logdatei (rotiert bei 1 MB) und meldet jede Zeile per Ereignis.</summary>
public sealed class FileLogger : ILogger
{
    private const long MaxSize = 1024 * 1024;
    private readonly object _lock = new();
    private readonly string _file;

    public FileLogger(string file)
    {
        _file = file;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    }

    public event Action<LogLevel, string>? MessageLogged;

    public string FilePath => _file;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var tag = level switch { LogLevel.Warning => "WARNUNG", LogLevel.Error => "FEHLER", _ => "INFO" };
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{tag}] {message}";
        if (exception != null)
            line += Environment.NewLine + "    " + exception.ToString().Replace("\n", "\n    ");

        lock (_lock)
        {
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > MaxSize)
                    File.Move(_file, _file + ".1", overwrite: true);
                File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging darf die App nie zum Absturz bringen.
            }
        }

        MessageLogged?.Invoke(level, message);
    }
}

/// <summary>Sammelt Meldungen im Speicher (für Tests).</summary>
public sealed class MemoryLogger : ILogger
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToList(); } }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        lock (_lines) _lines.Add($"[{level}] {message}{(exception != null ? " " + exception.Message : "")}");
    }
}
