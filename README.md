# Desktop Tuner

A native Windows 11 desktop-personalization starter focused on the Start menu, taskbar, and File Explorer.

## What works in this build

- Opens a searchable Start-style app launcher from Ctrl+Alt+Space or the Start settings page. It indexes the user's and shared Start menu shortcuts plus Windows AppsFolder applications, showing each app’s shell icon where available; unmatched Enter searches hand off to Windows Search, and separate Windows and Web buttons let users explicitly broaden any query. It can also open Documents, Downloads, Settings, This PC, Control Panel, Network, media folders, and Recent items.
- Offers Modern, Classic two-column, and Compact launcher layouts, with the selected style saved between sessions. Pin shortcuts and packaged Windows apps to a horizontally scrollable Start favorites row; pins persist and can be reordered or removed from each pinned app’s context menu. The Classic layout browses nested Start-menu program folders and packaged Windows apps. Its power menu provides Lock, Sleep, Hibernate, Sign out, Restart, and Shut down, with confirmation for actions that can close the session.
- Optionally opens the custom launcher when the user taps either Windows key; modified Win+key shortcuts pass through to Windows, and the setting is disabled by default.
- Opens a taskbar overlay on the primary display or every connected display, with live top-level window switching, second-click minimize/restore behavior, delayed hover previews with clickable DWM thumbnails and per-window close buttons, per-window and grouped minimize/close commands, and rounded context menus that follow the shared light/dark palette. Running windows for pinned executables share the pinned button and show a running indicator instead of appearing twice; other reorderable running-window buttons are shared across displays for the current session. A shortcut opens the companion Start menu on that display. Toggle idle auto-hide globally with Win+Alt+T. On supported bottom edge-to-edge and segmented layouts, it leaves the detected native Windows notification area uncovered so tray icons, clock, and flyouts remain visible and clickable; other layouts provide Tray, Clock, and audio controls (scroll to change volume, middle-click to mute, right-click to choose an output device, click for Sound settings). Output switching falls back to Windows Sound settings if the system policy interface is unavailable. A Widgets button opens the native Windows Widgets board. Optional per-user sign-in startup launches the taskbar in the background. Buttons can show extracted app icons at three sizes, hide or show labels, adjust their spacing, and center while Start stays left. Edge, size, transparency, full-edge, floating, segmented, display coverage, and auto-hide preferences persist between app launches.
- Pins running desktop apps to the custom taskbar, or drag `.exe` files, `.lnk` shortcuts, and folders onto it. Drag pinned buttons to reorder them; pinned buttons activate or launch apps and shortcuts, or open folders in File Explorer. Drop documents onto a pinned app to open them there; pins persist between app launches.
- Opens a companion File Explorer with a Home landing page for recently opened files, shell icons beside files and folders, drag-to-reorder tabs, per-tab navigation and search history, Ctrl+T/Ctrl+W/Ctrl+Tab shortcuts, and an option to open folders in a new tab. It also has a classic command strip, quick access to common folders and drives, recursive name search with quoted phrases, filename wildcards, and `ext:`/`kind:` filters, clickable Name/Date/Type/Size column sorting, and a bottom details pane that can be hidden or resized. Select multiple files or folders and cut, copy, paste, or send them to the Recycle Bin with the toolbar, context menu, or keyboard shortcuts; cut items are dimmed until moved and delete reports individual failures. It follows the app’s Windows preferences for startup location, hidden items, and file extensions. Create folders, rename items, and refresh from the toolbar or context menu; F2, Delete, F5, and Ctrl+Shift+N are supported. Double-click folders to browse and files to launch with their default app.
- Adjusts thirteen per-user Windows settings across Start, taskbar, and File Explorer, including full-path title bars, separate Explorer folder processes, and app/system color modes and transparency preferences. Some legacy Explorer preferences may be ignored by current tabbed File Explorer builds. Appearance settings are shared with Windows and can affect other apps and shell surfaces.
- The companion Start launcher and File Explorer follow the selected Windows app color mode. The custom taskbar and its window-preview cards follow Windows system light/dark mode and update when that setting changes.
- Saves and imports portable JSON profiles. Import loads choices for review and never applies them automatically.
- Captures the exact prior registry values before applying and provides one-step undo for the last successful apply.
- Rolls back partial registry writes if an apply fails.
- Does not inject code into Explorer or require administrator privileges.

The taskbar overlays leave the native Windows notification area exposed on supported bottom layouts; this depends on locating undocumented Shell window classes and falls back to shortcut controls if they are unavailable. Appearance follows each display's effective DPI, but mixed-DPI and hot-plug behavior still needs manual validation. Some Windows registry preferences and the classic context-menu switch may vary across Windows builds. Start's recent-items option is a Windows privacy setting shared with Jump Lists and File Explorer. Reproducing legacy taskbar/Explorer styling remains parity work.

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
