# Desktop Tuner

A native Windows 11 desktop-personalization starter focused on the Start menu, taskbar, and File Explorer.

## What works in this build

- Opens a searchable Start-style app launcher from Ctrl+Alt+Space or the Start settings page. It indexes the user's and shared Start menu shortcuts and can open Documents, Downloads, and Windows Settings.
- Offers Modern, Classic two-column, and Compact launcher layouts, with the selected style saved between sessions.
- Optionally opens the custom launcher when the user taps either Windows key; modified Win+key shortcuts pass through to Windows, and the setting is disabled by default.
- Opens a taskbar overlay on the primary display or every connected display, with live top-level window switching, minimize commands, a clock, and a shortcut to the companion Start menu on that display. Edge, size, display coverage, and auto-hide preferences persist between app launches.
- Pins running desktop apps to the custom taskbar, or drag `.exe` files, `.lnk` shortcuts, and folders onto it. Pinned buttons activate or launch apps and shortcuts, or open folders in File Explorer. Drop documents onto a pinned app to open them there; pins persist between app launches.
- Adjusts eleven per-user Windows settings across Start, taskbar, and File Explorer, including app/system color modes and transparency preferences. These appearance settings are shared with Windows and can affect other apps and shell surfaces.
- Saves and imports portable JSON profiles. Import loads choices for review and never applies them automatically.
- Captures the exact prior registry values before applying and provides one-step undo for the last successful apply.
- Rolls back partial registry writes if an apply fails.
- Does not inject code into Explorer or require administrator privileges.

The taskbar overlays cover the native taskbars visually but leave them running underneath and currently have no notification-area icons. Appearance follows each display's effective DPI, but mixed-DPI and hot-plug behavior still needs manual validation. Some Windows registry preferences and the classic context-menu switch may vary across Windows builds. Start's recent-items option is a Windows privacy setting shared with Jump Lists and File Explorer. Reproducing legacy taskbar/Explorer styling remains parity work.

See [PARITY.md](PARITY.md) for the feature-by-feature gap list against StartAllBack's current public feature description.

## Build

Requires the .NET 10 SDK and Windows Desktop targeting pack.

```powershell
dotnet restore --configfile NuGet.Config
dotnet build --no-restore
dotnet run --no-restore
dotnet run --project tests/DesktopTuner.Tests.csproj
```

The app targets `net10.0-windows` and uses WPF with no third-party package dependencies.
