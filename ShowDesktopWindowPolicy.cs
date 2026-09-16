namespace DesktopTuner;

public readonly record struct ShowDesktopWindowCandidate(nint Handle, bool IsVisible, bool IsMinimized, bool HasOwner, bool IsShellSurface, bool IsCloaked);

public static class ShowDesktopWindowPolicy
{
    public static IReadOnlyList<nint> SelectWindowsToMinimize(IEnumerable<ShowDesktopWindowCandidate> candidates, nint foregroundWindow = 0) =>
        candidates
            .Where(candidate => candidate.Handle != 0 && candidate.IsVisible && !candidate.IsMinimized &&
                candidate.Handle != foregroundWindow && !candidate.HasOwner && !candidate.IsShellSurface && !candidate.IsCloaked)
            .Select(candidate => candidate.Handle)
            .Distinct()
            .ToArray();
}
