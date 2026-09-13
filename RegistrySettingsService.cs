using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;

namespace DesktopTuner;

public sealed class RegistrySettingsService
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001a;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private readonly ProfileStore _profiles;

    public RegistrySettingsService(ProfileStore profiles) => _profiles = profiles;

    public int Read(SettingDefinition setting)
    {
        using var key = Registry.CurrentUser.OpenSubKey(setting.RegistryPath, writable: false);
        var value = key?.GetValue(setting.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return setting.DefaultValue;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return setting.DefaultValue;
        }
    }

    public IReadOnlyList<RegistrySnapshot> Apply(IReadOnlyDictionary<string, int> desired)
    {
        var settings = Validate(desired);
        var snapshots = settings.Select(Capture).ToList();
        var applied = new List<SettingDefinition>();
        try
        {
            foreach (var setting in settings)
            {
                using var key = Registry.CurrentUser.CreateSubKey(setting.RegistryPath, writable: true)
                    ?? throw new IOException($"Could not open the registry location for {setting.Name}.");
                key.SetValue(setting.ValueName, desired[setting.Id], RegistryValueKind.DWord);
                applied.Add(setting);
            }
            _profiles.SaveUndo(snapshots);
            NotifyShell();
            return snapshots;
        }
        catch (Exception applyError)
        {
            try
            {
                RestoreValues(snapshots.Where(snapshot => applied.Any(setting => setting.Id == snapshot.SettingId)));
                NotifyShell();
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Applying the settings failed, and rollback was incomplete.", applyError, rollbackError);
            }
            throw;
        }
    }

    public void UndoLastApply()
    {
        var snapshots = _profiles.LoadUndo() ?? throw new InvalidOperationException("There is no saved change to undo.");
        RestoreValues(snapshots);
        _profiles.ClearUndo();
        NotifyShell();
    }

    public bool HasUndo => _profiles.LoadUndo() is { Count: > 0 };

    public static Dictionary<string, int> CurrentDefaults() => SettingsCatalog.All.ToDictionary(s => s.Id, s => s.DefaultValue);

    private static IReadOnlyList<SettingDefinition> Validate(IReadOnlyDictionary<string, int> desired)
    {
        if (desired.Count == 0) throw new InvalidOperationException("There are no settings to apply.");
        return desired.Select(pair =>
        {
            var setting = SettingsCatalog.ById(pair.Key);
            if (setting.Choices.All(choice => choice.Value != pair.Value))
                throw new InvalidDataException($"The selected value for {setting.Name} is not supported.");
            return setting;
        }).ToList();
    }

    private static RegistrySnapshot Capture(SettingDefinition setting)
    {
        using var key = Registry.CurrentUser.OpenSubKey(setting.RegistryPath, writable: false);
        if (key is null || !key.GetValueNames().Contains(setting.ValueName, StringComparer.OrdinalIgnoreCase))
            return new RegistrySnapshot(setting.Id, false, null, null);

        var kind = key.GetValueKind(setting.ValueName);
        var value = key.GetValue(setting.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var data = kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value, CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => JsonSerializer.Serialize((string[]?)value ?? []),
            RegistryValueKind.Binary => Convert.ToBase64String((byte[]?)value ?? []),
            _ => throw new InvalidDataException($"Cannot safely back up registry value {setting.ValueName} of type {kind}.")
        };
        return new RegistrySnapshot(setting.Id, true, kind.ToString(), data);
    }

    private static void RestoreValues(IEnumerable<RegistrySnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var setting = SettingsCatalog.ById(snapshot.SettingId);
            using var key = Registry.CurrentUser.CreateSubKey(setting.RegistryPath, writable: true)
                ?? throw new IOException($"Could not open the registry location for {setting.Name}.");
            if (!snapshot.Existed)
            {
                key.DeleteValue(setting.ValueName, throwOnMissingValue: false);
                continue;
            }

            if (!Enum.TryParse<RegistryValueKind>(snapshot.Kind, out var kind) || snapshot.Data is null)
                throw new InvalidDataException($"The saved recovery data for {setting.Name} is invalid.");
            object value = kind switch
            {
                RegistryValueKind.DWord => int.Parse(snapshot.Data, CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => long.Parse(snapshot.Data, CultureInfo.InvariantCulture),
                RegistryValueKind.String or RegistryValueKind.ExpandString => snapshot.Data,
                RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(snapshot.Data) ?? [],
                RegistryValueKind.Binary => Convert.FromBase64String(snapshot.Data),
                _ => throw new InvalidDataException($"The saved registry value type {kind} cannot be restored.")
            };
            key.SetValue(setting.ValueName, value, kind);
        }
    }

    private static void NotifyShell()
    {
        SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_SETTINGCHANGE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out _);
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
