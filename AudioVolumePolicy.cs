namespace DesktopTuner;

public static class AudioVolumePolicy
{
    public static float Adjust(float volume, int wheelDelta)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        return Math.Clamp(volume + wheelDelta / 120f * 0.05f, 0f, 1f);
    }

    public static string GetLabel(float volume, bool muted)
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        return muted ? $"Muted · {volume:P0}" : $"Volume · {volume:P0}";
    }
}
