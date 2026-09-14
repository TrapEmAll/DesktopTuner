# Windows shell integration

## Native Explorer context menu

Windows 11's modern File Explorer context menu accepts general-purpose commands through a packaged `IExplorerCommand` COM class. Desktop Tuner is currently distributed as an unpackaged per-user app, so the bridge uses a sparse identity package that points back to the installed app directory.

The native bridge in `shell/ExplorerContextMenu` registers the same command for selected folders and folder backgrounds. Its menu-construction methods return immediately; invocation resolves the filesystem path and starts `DesktopTuner.exe --open-folder "<path>"`. The existing single-instance message path then forwards the location to the running app.

The Windows workflow builds the x64 DLL, checks its COM exports, and asks `MakeAppx` to validate the sparse package manifest. It separately checks that the executable, DLL, and visual assets resolve in the external install layout. Sparse packages keep those files outside the package, so `MakeAppx` skips its package-internal path checks. CI does not register the package or exercise an interactive Explorer context menu.

## Replacement taskbar work area

Edge-docked replacement taskbars register as Windows application desktop toolbars (AppBars). They query and set their approved screen rectangle, reserve that edge from maximized windows, report activation and window-position changes so Windows can coordinate AppBar z-order, yield to full-screen apps, and temporarily hide for Shell cascade/tile operations. When replacement auto-hide is enabled, they also register the selected edge on that monitor with [`ABM_SETAUTOHIDEBAREX`](https://learn.microsoft.com/en-us/windows/win32/shell/abm-setautohidebarex), and release that registration when disabled, moved, or closed. If Windows reports that another auto-hide appbar already owns that monitor edge, Desktop Tuner keeps its local auto-hide behavior and logs a warning. They also respond when the shell reports that taskbar or AppBar positions changed. Closing the taskbar unregisters the AppBar. Floating taskbars do not register because they do not occupy a screen edge and continue to overlay the desktop.

CI verifies the edge geometry and auto-hide eligibility policies, and replacement mode now restores the native taskbar if Windows refuses to register or position an edge-docked AppBar. CI does not launch the custom taskbar or confirm work-area changes interactively. Positioning with a hidden native taskbar, multi-monitor layouts, Windows auto-hide reveal behavior and collisions with the native taskbar, full-screen z-order, cascade/tile handling, cleanup after unexpected process exit, and floating behavior still need validation on supported Windows 11 desktop builds.

This gives the replacement bar a supported taskbar-like work-area contract. It does not make it the Windows shell, replace native Start visuals, or provide all built-in taskbar and tray behavior.

## Replacing Explorer as the logon shell

Windows [Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/) can start a Win32 or UWP application in place of `Explorer.exe`, but Microsoft supports it only on Enterprise, Education, and IoT Enterprise editions. It is an optional Windows feature and its shell assignment takes effect at sign-in. Shell Launcher v2 hosts the replacement as the shell, so this is a whole-shell transition rather than a taskbar customization API.

An experimental desktop host can be started manually with `DesktopTuner.exe --desktop-host`. It displays the configured wallpaper and visible items from the user and public Desktop folders on the primary display, adds This PC and Recycle Bin as shell namespace entries with native shell icons, opens items through the Windows shell, watches for Desktop content changes, copies files or folders dropped from other apps onto the user Desktop, supports dragging filesystem items out to other apps, and exposes Windows' native context verbs for filesystem items. Its background menu also offers refresh and folder creation. It is a prototype that runs alongside Explorer: native desktop icons may remain behind it, it has no multi-monitor layout or full shell namespace support, and it has no automatic sign-in or Shell Launcher configuration.

Assigning Desktop Tuner as the logon shell today would remove the normal Explorer desktop as well as the native taskbar, Start menu, and notification area without replacing all of them. Shell Launcher alone is not a general solution for consumer Windows editions or a usable replacement mode for the current app. Moving from the prototype to a supported managed-edition shell mode still needs complete desktop behavior, sign-in/recovery handling, and a reversible configuration flow, while consumer editions remain on the current Explorer-hosted integration path.

## Distribution gate

The repository has no production code-signing certificate. Windows requires the sparse identity package to be signed by a publisher certificate trusted on the target PC before it can be registered. The unsigned package produced for validation is not installable for normal users. Do not ship a self-signed development certificate or install one into a user's Trusted People store as a workaround.

Until a trusted publisher certificate is available, the installer keeps using the existing per-user registry verbs, which Windows may place under **Show more options**. After signing is provisioned, the release flow can sign `DesktopTuner.identity.msix`, install it for the current user with the app directory as its external location, copy the native DLL and manifest assets beside the app, and remove the package during uninstall. Registration and invocation then need verification on supported Windows 11 desktop builds.

The Explorer extension improves context-menu integration. It does not replace Explorer windows or the Start menu.
