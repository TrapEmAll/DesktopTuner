using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Security.Principal;
using Microsoft.Win32;

namespace DesktopTuner;

public sealed record StartMenuIdentity(string DisplayName, string Initials, string? PicturePath);

public static class StartMenuIdentityService
{
    private const string AccountPictureKey = @"Software\Microsoft\Windows\CurrentVersion\AccountPicture\Users";
    private static readonly string[] PictureValueNames = ["Image448", "Image240", "Image192", "Image96", "Image48"];

    public static StartMenuIdentity ReadCurrentUser()
    {
        var displayName = Environment.UserName;
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "User";

        return new StartMenuIdentity(displayName, GetInitials(displayName), TryReadPicturePath());
    }

    public static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "?";

        var words = displayName.Split([' ', '\t', '\r', '\n', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "?";
        if (words.Length == 1) return FirstTextElement(words[0]).ToUpperInvariant();
        return string.Concat(FirstTextElement(words[0]), FirstTextElement(words[^1])).ToUpperInvariant();
    }

    private static string FirstTextElement(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        return enumerator.MoveNext() ? enumerator.GetTextElement() : string.Empty;
    }

    private static string? TryReadPicturePath()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(sid)) return null;

            using var key = Registry.CurrentUser.OpenSubKey($"{AccountPictureKey}\\{sid}");
            foreach (var valueName in PictureValueNames)
            {
                if (key?.GetValue(valueName) is not string value || string.IsNullOrWhiteSpace(value)) continue;
                var path = Environment.ExpandEnvironmentVariables(value);
                if (File.Exists(path)) return path;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            // The account image is optional; the Start menu uses the initials avatar if Windows does not expose it.
            Trace.TraceWarning($"Could not read the local Windows account picture location: {ex.Message}");
        }

        return null;
    }
}
