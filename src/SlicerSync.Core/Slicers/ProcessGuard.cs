using System.Diagnostics;
using SlicerSync.Core.Config;

namespace SlicerSync.Core.Slicers;

/// <summary>
/// Checks whether a slicer is running (profile folders must not be moved then): all built-in
/// slicers, plus the programs of custom folders for which the user picked the EXE.
/// </summary>
public sealed class ProcessGuard
{
    private readonly Func<IEnumerable<string>> _processNames;

    public ProcessGuard(Func<IEnumerable<string>>? processNames = null)
    {
        _processNames = processNames ?? (() => Process.GetProcesses().Select(p => p.ProcessName));
    }

    public IReadOnlyList<string> RunningSlicers(IEnumerable<ProfileMapping>? mappings = null)
    {
        var running = new HashSet<string>(_processNames(), StringComparer.OrdinalIgnoreCase);
        var builtIn = SlicerCatalog.All
            .Where(d => d.ProcessNames.Any(running.Contains))
            .Select(d => d.DisplayName);
        var custom = (mappings ?? Enumerable.Empty<ProfileMapping>())
            .Where(m => m.IsCustom && m.ProcessName.Length > 0 && running.Contains(m.ProcessName))
            .Select(m => m.Folder);
        return builtIn.Concat(custom).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void EnsureSlicersClosed(IEnumerable<ProfileMapping>? mappings = null)
    {
        var running = RunningSlicers(mappings);
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
