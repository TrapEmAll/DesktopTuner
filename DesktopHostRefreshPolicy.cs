using System.IO;

namespace DesktopTuner;

public static class DesktopHostRefreshPolicy
{
    public static bool ShouldRefresh(WatcherChangeTypes changeType) => changeType is
        WatcherChangeTypes.Created or WatcherChangeTypes.Deleted or WatcherChangeTypes.Changed or WatcherChangeTypes.Renamed;
}
