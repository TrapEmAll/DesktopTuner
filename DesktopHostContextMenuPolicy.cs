namespace DesktopTuner;

public static class DesktopHostContextMenuPolicy
{
    public static bool CanInvokeNativeCommand(IEnumerable<DesktopHostItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var selection = items.Where(item => item.IsSelected).ToArray();
        return selection.Length > 0 && selection.All(item => item.CanShowNativeContextMenu);
    }
}
