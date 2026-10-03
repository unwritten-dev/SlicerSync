using System.Diagnostics;

namespace SlicerSync.Core.Slicers;

/// <summary>Checks whether a known slicer is running (profile folders must not be moved then).</summary>
public sealed class ProcessGuard
{
    private readonly Func<IEnumerable<string>> _processNames;

    public ProcessGuard(Func<IEnumerable<string>>? processNames = null)
    {
        _processNames = processNames ?? (() => Process.GetProcesses().Select(p => p.ProcessName));
    }

    public IReadOnlyList<string> RunningSlicers()
    {
        var running = new HashSet<string>(_processNames(), StringComparer.OrdinalIgnoreCase);
        return SlicerCatalog.All
            .Where(d => d.ProcessNames.Any(running.Contains))
            .Select(d => d.DisplayName)
            .ToList();
    }

    public void EnsureSlicersClosed()
    {
        var running = RunningSlicers();
        if (running.Count > 0)
            throw new SlicerRunningException(running);
    }
}

public sealed class SlicerRunningException : Exception
{
    public SlicerRunningException(IReadOnlyList<string> slicers)
        : base(Loc.T($"Please close first: {string.Join(", ", slicers)}", $"Bitte zuerst schließen: {string.Join(", ", slicers)}"))
    {
        Slicers = slicers;
    }

    public IReadOnlyList<string> Slicers { get; }
}
