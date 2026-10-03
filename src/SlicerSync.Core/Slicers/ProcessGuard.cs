using System.Diagnostics;

namespace SlicerSync.Core.Slicers;

/// <summary>Prüft, ob ein Slicer läuft (dann dürfen Profilordner nicht verschoben werden).</summary>
public sealed class ProcessGuard
{
    private static readonly (string Process, string Name)[] Known =
    {
        ("orca-slicer", "Orca Slicer"),
        ("OrcaSlicer", "Orca Slicer"),
        ("prusa-slicer", "PrusaSlicer"),
        ("PrusaSlicer", "PrusaSlicer"),
    };

    private readonly Func<IEnumerable<string>> _processNames;

    public ProcessGuard(Func<IEnumerable<string>>? processNames = null)
    {
        _processNames = processNames ?? (() => Process.GetProcesses().Select(p => p.ProcessName));
    }

    public IReadOnlyList<string> RunningSlicers()
    {
        var running = new HashSet<string>(_processNames(), StringComparer.OrdinalIgnoreCase);
        return Known.Where(k => running.Contains(k.Process)).Select(k => k.Name).Distinct().ToList();
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
        : base($"Bitte zuerst schließen: {string.Join(", ", slicers)}")
    {
        Slicers = slicers;
    }

    public IReadOnlyList<string> Slicers { get; }
}
