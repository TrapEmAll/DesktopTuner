# Desktop Tuner

A native Windows 11 desktop-personalization starter focused on the Start menu, taskbar, and File Explorer.

## What works in this build

- Opens a searchable Start-style app launcher from Ctrl+Alt+Space or the Start settings page. It indexes the user's and shared Start menu shortcuts and can open Documents, Downloads, and Windows Settings.
- Opens a primary-monitor taskbar overlay on any edge with live top-level window switching, minimize commands, a clock, and a shortcut to the companion Start menu. Edge, size, and auto-hide preferences persist between app launches.
- Adjusts eight per-user Windows settings across Start, taskbar, and File Explorer, including an experimental classic context-menu switch.
- Saves and imports portable JSON profiles. Import loads choices for review and never applies them automatically.
- Captures the exact prior registry values before applying and provides one-step undo for the last successful apply.
- Rolls back partial registry writes if an apply fails.
- Does not inject code into Explorer or require administrator privileges.

The taskbar overlay covers the native taskbar visually but leaves it running underneath; it currently has no notification-area icons, pinning, drag/drop, or multi-monitor support. Taskbar controls and the classic context-menu switch use registry preferences whose behavior may vary across Windows builds. They are labeled experimental in the app. Start's recent-items option is a Windows privacy setting shared with Jump Lists and File Explorer. Replacing the Windows key menu and reproducing legacy taskbar/Explorer styling remain parity work.

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
