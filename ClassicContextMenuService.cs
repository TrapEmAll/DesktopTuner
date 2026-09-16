using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class ClassicContextMenuService
{
    private const string ContextMenuKeyPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string OwnershipKeyPath = @"Software\DesktopTuner\ClassicContextMenu";
    private const string OwnedValue = "Owned";
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ContextMenuKeyPath, writable: false);
        return key is not null && string.IsNullOrEmpty(key.GetValue(null) as string);
    }

    public static bool IsOwned()
    {
        using var key = Registry.CurrentUser.OpenSubKey(OwnershipKeyPath, writable: false);
        return key?.GetValue(OwnedValue) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(ContextMenuKeyPath, writable: false);
            if (existing is not null)
            {
                if (!string.IsNullOrEmpty(existing.GetValue(null) as string))
                    throw new InvalidOperationException("Windows already has a conflicting classic context-menu registration.");
            }
            else
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(ContextMenuKeyPath, writable: true)
                        ?? throw new IOException("Could not register the classic context-menu preference.");
                    key.SetValue(null, string.Empty, RegistryValueKind.String);
                    using var ownership = Registry.CurrentUser.CreateSubKey(OwnershipKeyPath, writable: true)
                        ?? throw new IOException("Could not save classic context-menu ownership state.");
                    ownership.SetValue(OwnedValue, 1, RegistryValueKind.DWord);
                }
                catch
                {
                    Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);
                    Registry.CurrentUser.DeleteSubKeyTree(OwnershipKeyPath, throwOnMissingSubKey: false);
                    throw;
                }
            }
        }
        else if (IsOwned())
        {
            Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(OwnershipKeyPath, throwOnMissingSubKey: false);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
