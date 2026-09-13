using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed record TaskbarBatteryStatus(int? Percent, bool IsCharging, bool IsOnAcPower, uint? RemainingSeconds);

public static class TaskbarBatteryService
{
    private const byte NoSystemBattery = 0x80;
    private const byte Charging = 0x08;
    private const byte UnknownPercent = 0xFF;
    private const uint UnknownRemainingTime = uint.MaxValue;

    public static TaskbarBatteryStatus? TryRead()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        return Parse(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent, status.BatteryLifeTime);
    }

    public static TaskbarBatteryStatus? Parse(byte acLineStatus, byte batteryFlag, byte percent, uint remainingSeconds)
    {
        if ((batteryFlag & NoSystemBattery) != 0) return null;
        return new TaskbarBatteryStatus(
            percent == UnknownPercent || percent > 100 ? null : percent,
            (batteryFlag & Charging) != 0,
            acLineStatus == 1,
            remainingSeconds == UnknownRemainingTime ? null : remainingSeconds);
    }

    public static double GetFillWidth(int? percent, double fullWidth)
    {
        if (!double.IsFinite(fullWidth) || fullWidth < 0) throw new ArgumentOutOfRangeException(nameof(fullWidth));
        return fullWidth * Math.Clamp(percent ?? 0, 0, 100) / 100d;
    }

    public static string GetLabel(TaskbarBatteryStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var parts = new List<string> { status.Percent is { } percent ? $"Battery: {percent}%" : "Battery status unavailable" };
        if (status.IsCharging) parts.Add("Charging");
        else if (status.IsOnAcPower) parts.Add("Plugged in");
        if (!status.IsCharging && status.RemainingSeconds is > 0 and var seconds)
        {
            var minutes = Math.Max(1, (int)Math.Round(seconds / 60d));
            if (minutes >= 60)
                parts.Add($"About {minutes / 60} h {minutes % 60} min remaining");
            else
                parts.Add($"About {minutes} min remaining");
        }
        return string.Join(" · ", parts);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}
