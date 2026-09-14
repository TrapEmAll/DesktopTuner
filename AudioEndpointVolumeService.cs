using System.Runtime.InteropServices;

namespace DesktopTuner;

public static class AudioEndpointVolumeService
{
    private static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EndpointVolumeInterfaceId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int RenderFlow = 0;
    private const int CaptureFlow = 1;
    private const int HResultErrorNotFound = unchecked((int)0x80070490);
    private const int MultimediaRole = 1;
    private const int ClassContextAll = 23;
    private const int ActiveDeviceState = 1;
    private const int PropertyStoreReadOnly = 0;
    private static readonly Guid DeviceFriendlyNamePropertySet = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private static readonly Guid PolicyConfigClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    public static IReadOnlyList<AudioOutputDevice> EnumerateOutputs() => EnumerateEndpoints(RenderFlow)
        .Select(endpoint => new AudioOutputDevice(endpoint.Id, endpoint.Name, endpoint.IsDefault)).ToArray();

    public static IReadOnlyList<AudioInputDevice> EnumerateInputs() => EnumerateEndpoints(CaptureFlow)
        .Select(endpoint => new AudioInputDevice(endpoint.Id, endpoint.Name, endpoint.IsDefault)).ToArray();

    private static IReadOnlyList<AudioEndpointDevice> EnumerateEndpoints(int flow)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        IMMDevice? defaultDevice = null;
        try
        {
            enumerator = CreateEnumerator();
            Check(enumerator.EnumAudioEndpoints(flow, ActiveDeviceState, out devices));
            var defaultId = GetDefaultDeviceId(enumerator, flow, out defaultDevice);
            Check(devices.GetCount(out var count));

            var endpoints = new List<AudioEndpointDevice>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                IPropertyStore? properties = null;
                IntPtr deviceId = IntPtr.Zero;
                var name = default(PropVariant);
                try
                {
                    Check(devices.Item(index, out device));
                    Check(device.GetId(out deviceId));
                    Check(device.OpenPropertyStore(PropertyStoreReadOnly, out properties));
                    var key = new PropertyKey(DeviceFriendlyNamePropertySet, 14);
                    Check(properties.GetValue(ref key, out name));
                    var endpointId = Marshal.PtrToStringUni(deviceId);
                    var friendlyName = name.Type == 31 ? Marshal.PtrToStringUni(name.StringValue) : null;
                    if (!string.IsNullOrWhiteSpace(endpointId) && !string.IsNullOrWhiteSpace(friendlyName))
                        endpoints.Add(new AudioEndpointDevice(endpointId, friendlyName,
                            AudioVolumePolicy.IsDefaultEndpoint(endpointId, defaultId)));
                }
                finally
                {
                    PropVariantClear(ref name);
                    if (deviceId != IntPtr.Zero) Marshal.FreeCoTaskMem(deviceId);
                    ReleaseComObject(properties);
                    ReleaseComObject(device);
                }
            }

            return endpoints.OrderByDescending(device => device.IsDefault).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally
        {
            ReleaseComObject(defaultDevice);
            ReleaseComObject(devices);
            ReleaseComObject(enumerator);
        }
    }

    // Endpoint enumeration uses the documented MMDevice API. Default selection uses Windows' policy COM interface,
    // which is outside the documented MMDevice API; the click handler reports failures and opens Sound settings as a fallback.
    public static void SetDefaultOutput(string endpointId)
        => SetDefaultEndpoint(endpointId);

    public static void SetDefaultInput(string endpointId)
        => SetDefaultEndpoint(endpointId);

    private static void SetDefaultEndpoint(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        object? policyObject = null;
        try
        {
            var policyType = Type.GetTypeFromCLSID(PolicyConfigClassId, throwOnError: true)!;
            policyObject = Activator.CreateInstance(policyType)!;
            var policy = (IPolicyConfig)policyObject;
            foreach (var role in new[] { 0, 1, 2 }) Check(policy.SetDefaultEndpoint(endpointId, role));
        }
        finally
        {
            ReleaseComObject(policyObject);
        }
    }

    public static (float Volume, bool Muted) ReadDefaultOutput() => ReadDefaultEndpoint(RenderFlow);

    public static (float Volume, bool Muted) ReadDefaultInput() => ReadDefaultEndpoint(CaptureFlow);

    private static (float Volume, bool Muted) ReadDefaultEndpoint(int flow) => WithDefaultEndpoint(flow, volume =>
    {
        Check(volume.GetMasterVolumeLevelScalar(out var level));
        Check(volume.GetMute(out var muted));
        return (level, muted);
    });

    public static void SetDefaultOutputVolume(float level)
    {
        if (!float.IsFinite(level) || level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        WithDefaultEndpoint(RenderFlow, volume =>
        {
            var eventContext = Guid.Empty;
            Check(volume.SetMasterVolumeLevelScalar(level, ref eventContext));
            return 0;
        });
    }

    public static bool ToggleDefaultOutputMute() => WithDefaultEndpoint(RenderFlow, volume =>
    {
        Check(volume.GetMute(out var muted));
        var eventContext = Guid.Empty;
        Check(volume.SetMute(!muted, ref eventContext));
        return !muted;
    });

    public static void SetDefaultInputVolume(float level)
    {
        if (!float.IsFinite(level) || level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        WithDefaultEndpoint(CaptureFlow, volume =>
        {
            var eventContext = Guid.Empty;
            Check(volume.SetMasterVolumeLevelScalar(level, ref eventContext));
            return 0;
        });
    }

    public static bool ToggleDefaultInputMute() => WithDefaultEndpoint(CaptureFlow, volume =>
    {
        Check(volume.GetMute(out var muted));
        var eventContext = Guid.Empty;
        Check(volume.SetMute(!muted, ref eventContext));
        return !muted;
    });

    private static T WithDefaultEndpoint<T>(int flow, Func<IAudioEndpointVolume, T> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? endpointObject = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            Check(enumerator.GetDefaultAudioEndpoint(flow, MultimediaRole, out device));
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

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var enumeratorType = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
        return (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
    }

    private static string? GetDefaultDeviceId(IMMDeviceEnumerator enumerator, int flow, out IMMDevice? device)
    {
        device = null;
        var result = enumerator.GetDefaultAudioEndpoint(flow, MultimediaRole, out device);
        if (result == HResultErrorNotFound) return null;
        Check(result);
        if (device is null) throw new InvalidOperationException("Windows returned no default audio endpoint without an error.");
        Check(device.GetId(out var deviceId));
        try { return Marshal.PtrToStringUni(deviceId); }
        finally { Marshal.FreeCoTaskMem(deviceId); }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
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
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string endpointId, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr notificationClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr notificationClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int storageAccess, out IPropertyStore properties);

        [PreserveSig]
        int GetId(out IntPtr endpointId);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr endpointId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr endpointId, int defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr endpointId);
        [PreserveSig] int SetDeviceFormat(IntPtr endpointId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(IntPtr endpointId, out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(IntPtr endpointId, IntPtr period, int hasChanged);
        [PreserveSig] int GetShareMode(IntPtr endpointId, IntPtr shareMode);
        [PreserveSig] int SetShareMode(IntPtr endpointId, IntPtr shareMode);
        [PreserveSig] int GetPropertyValue(IntPtr endpointId, IntPtr propertyKey, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr endpointId, IntPtr propertyKey, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string endpointId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string endpointId, int visible);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr StringValue;
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

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);
public sealed record AudioInputDevice(string Id, string Name, bool IsDefault);
internal sealed record AudioEndpointDevice(string Id, string Name, bool IsDefault);
