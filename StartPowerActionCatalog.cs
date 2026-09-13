using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTuner;

public sealed record StartPowerAction(string Id, string Label, bool RequiresConfirmation);

public static class StartPowerActionCatalog
{
    public static IReadOnlyList<StartPowerAction> Actions { get; } =
    [
        new("lock", "Lock", false),
        new("sleep", "Sleep", false),
        new("hibernate", "Hibernate", false),
        new("sign-out", "Sign out", true),
        new("restart", "Restart", true),
        new("shutdown", "Shut down", true)
    ];

    public static StartPowerAction ById(string id) =>
        Actions.SingleOrDefault(action => action.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown power action.");
}

public static class StartPowerActionService
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    public static void Execute(string actionId)
    {
        var action = StartPowerActionCatalog.ById(actionId);
        switch (action.Id)
        {
            case "lock":
                if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not lock the workstation.");
                return;
            case "sleep":
                Suspend(hibernate: false);
                return;
            case "hibernate":
                Suspend(hibernate: true);
                return;
            case "sign-out":
                RunShutdownCommand("/l");
                return;
            case "restart":
                RunShutdownCommand("/r", "/t", "0");
                return;
            case "shutdown":
                RunShutdownCommand("/s", "/t", "0");
                return;
            default:
                throw new InvalidOperationException($"No executor is registered for power action {action.Id}.");
        }
    }

    private static void Suspend(bool hibernate)
    {
        EnableShutdownPrivilege();
        if (!SetSuspendState(hibernate, forceCritical: false, disableWakeEvent: false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), hibernate
                ? "Windows could not hibernate. Hibernation may be disabled on this device."
                : "Windows could not enter sleep mode.");
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not access the power-action privilege.");

        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var privilege))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not locate the power-action privilege.");
            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privilege = new LuidAndAttributes { Luid = privilege, Attributes = SePrivilegeEnabled }
            };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enable the power-action privilege.");
            if (Marshal.GetLastWin32Error() == ErrorNotAllAssigned)
                throw new UnauthorizedAccessException("This Windows account is not allowed to enter sleep or hibernation.");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void RunShutdownCommand(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start its power action command.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privilege;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenProcessToken")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAllPrivileges, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
