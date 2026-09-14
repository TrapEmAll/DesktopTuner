using System.IO;

namespace DesktopTuner;

public static class DesktopHostRefreshPolicy
{
    private const int ShellItemChanges =
        0x00000001 | // SHCNE_RENAMEITEM
        0x00000002 | // SHCNE_CREATE
        0x00000004 | // SHCNE_DELETE
        0x00000008 | // SHCNE_MKDIR
        0x00000010 | // SHCNE_RMDIR
        0x00000020 | // SHCNE_MEDIAINSERTED
        0x00000040 | // SHCNE_MEDIAREMOVED
        0x00000080 | // SHCNE_DRIVEREMOVED
        0x00000100 | // SHCNE_DRIVEADD
        0x00000200 | // SHCNE_NETSHARE
        0x00000400 | // SHCNE_NETUNSHARE
        0x00000800 | // SHCNE_ATTRIBUTES
        0x00001000 | // SHCNE_UPDATEDIR
        0x00002000 | // SHCNE_UPDATEITEM
        0x00004000 | // SHCNE_SERVERDISCONNECT
        0x00008000 | // SHCNE_UPDATEIMAGE
        0x00020000 | // SHCNE_RENAMEFOLDER
        0x00040000 | // SHCNE_FREESPACE
        0x08000000;  // SHCNE_ASSOCCHANGED

    public static bool ShouldRefresh(WatcherChangeTypes changeType) => changeType is
        WatcherChangeTypes.Created or WatcherChangeTypes.Deleted or WatcherChangeTypes.Changed or WatcherChangeTypes.Renamed;

    public static bool ShouldRefreshShellEvent(int eventId) => (eventId & ShellItemChanges) != 0;
}
