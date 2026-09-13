using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class AudioEndpointVolumeService
{
    private static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EndpointVolumeInterfaceId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int RenderFlow = 0;
    private const int MultimediaRole = 1;
    private const int ClassContextAll = 23;

    public static (float Volume, bool Muted) ReadDefaultOutput() => WithDefaultOutput(volume =>
    {
        Check(volume.GetMasterVolumeLevelScalar(out var level));
        Check(volume.GetMute(out var muted));
        return (level, muted);
    });

    public static void SetDefaultOutputVolume(float level)
    {
        if (!float.IsFinite(level) || level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        WithDefaultOutput(volume =>
        {
            var eventContext = Guid.Empty;
            Check(volume.SetMasterVolumeLevelScalar(level, ref eventContext));
            return 0;
        });
    }

    public static bool ToggleDefaultOutputMute() => WithDefaultOutput(volume =>
    {
        Check(volume.GetMute(out var muted));
        var eventContext = Guid.Empty;
        Check(volume.SetMute(!muted, ref eventContext));
        return !muted;
    });

    private static T WithDefaultOutput<T>(Func<IAudioEndpointVolume, T> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? endpointObject = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            Check(enumerator.GetDefaultAudioEndpoint(RenderFlow, MultimediaRole, out device));
            var endpointInterfaceId = EndpointVolumeInterfaceId;
            Check(device.Activate(ref endpointInterfaceId, ClassContextAll, IntPtr.Zero, out endpointObject));
            return action((IAudioEndpointVolume)endpointObject);
        }
        finally
        {
            if (endpointObject is not null && Marshal.IsComObject(endpointObject)) Marshal.ReleaseComObject(endpointObject);
            if (device is not null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
            if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static void Check(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
