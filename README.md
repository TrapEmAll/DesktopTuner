# Desktop Tuner

A native Windows 11 desktop-personalization starter focused on the Start menu, taskbar, and File Explorer.

## What works in this first build

- Adjusts seven per-user Windows settings across Start, taskbar, and File Explorer.
- Saves and imports portable JSON profiles. Import loads choices for review and never applies them automatically.
- Captures the exact prior registry values before applying and provides one-step undo for the last successful apply.
- Rolls back partial registry writes if an apply fails.
- Does not inject code into Explorer or require administrator privileges.

Taskbar controls use registry preferences whose behavior may vary across Windows builds. They are labeled experimental in the app. Start's recent-items option is a Windows privacy setting shared with Jump Lists and File Explorer; Windows does not provide a public app API for rebuilding the built-in Start menu layout.

## Build

Requires the .NET 10 SDK and Windows Desktop targeting pack.

```powershell
dotnet restore --configfile NuGet.Config
dotnet build --no-restore
dotnet run --no-restore
```

The app targets `net10.0-windows` and uses WPF with no third-party package dependencies.
