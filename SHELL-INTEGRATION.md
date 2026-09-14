# Windows shell integration

## Native Explorer context menu

Windows 11's modern File Explorer context menu accepts general-purpose commands through a packaged `IExplorerCommand` COM class. Desktop Tuner is currently distributed as an unpackaged per-user app, so the bridge uses a sparse identity package that points back to the installed app directory.

The native bridge in `shell/ExplorerContextMenu` registers the same command for selected folders and folder backgrounds. Its menu-construction methods return immediately; invocation resolves the filesystem path and starts `DesktopTuner.exe --open-folder "<path>"`. The existing single-instance message path then forwards the location to the running app.

The Windows workflow builds the x64 DLL, checks its COM exports, and asks `MakeAppx` to validate the sparse package manifest. It separately checks that the executable, DLL, and visual assets resolve in the external install layout. Sparse packages keep those files outside the package, so `MakeAppx` skips its package-internal path checks. CI does not register the package or exercise an interactive Explorer context menu.

## Replacement taskbar work area

Edge-docked replacement taskbars register as Windows application desktop toolbars (AppBars). They query and set their approved screen rectangle, reserve that edge from maximized windows, and report activation and window-position changes so Windows can coordinate AppBar z-order. They also respond when the shell reports that taskbar or AppBar positions changed. Closing the taskbar unregisters the AppBar. Floating taskbars do not register because they do not occupy a screen edge and continue to overlay the desktop.

CI verifies the edge geometry policy, but does not launch the custom taskbar or confirm work-area changes interactively. Positioning with a hidden native taskbar, multi-monitor layouts, auto-hide, cleanup after unexpected process exit, and floating behavior still need validation on supported Windows 11 desktop builds.

This gives the replacement bar a supported taskbar-like work-area contract. It does not make it the Windows shell, replace native Start visuals, or provide all built-in taskbar and tray behavior.

## Distribution gate

The repository has no production code-signing certificate. Windows requires the sparse identity package to be signed by a publisher certificate trusted on the target PC before it can be registered. The unsigned package produced for validation is not installable for normal users. Do not ship a self-signed development certificate or install one into a user's Trusted People store as a workaround.

Until a trusted publisher certificate is available, the installer keeps using the existing per-user registry verbs, which Windows may place under **Show more options**. After signing is provisioned, the release flow can sign `DesktopTuner.identity.msix`, install it for the current user with the app directory as its external location, copy the native DLL and manifest assets beside the app, and remove the package during uninstall. Registration and invocation then need verification on supported Windows 11 desktop builds.

The Explorer extension improves context-menu integration. It does not replace Explorer windows or the Start menu.
