# Native Explorer integration

Windows 11's modern File Explorer context menu accepts general-purpose commands through a packaged `IExplorerCommand` COM class. Desktop Tuner is currently distributed as an unpackaged per-user app, so the bridge uses a sparse identity package that points back to the installed app directory.

The native bridge in `shell/ExplorerContextMenu` registers the same command for selected folders and folder backgrounds. Its menu-construction methods return immediately; invocation resolves the filesystem path and starts `DesktopTuner.exe --open-folder "<path>"`. The existing single-instance message path then forwards the location to the running app.

The Windows workflow builds the x64 DLL, checks its COM exports, and asks `MakeAppx` to validate the sparse package manifest. It separately checks that the executable, DLL, and visual assets resolve in the external install layout. Sparse packages keep those files outside the package, so `MakeAppx` skips its package-internal path checks. CI does not register the package or exercise an interactive Explorer context menu.

## Distribution gate

The repository has no production code-signing certificate. Windows requires the sparse identity package to be signed by a publisher certificate trusted on the target PC before it can be registered. The unsigned package produced for validation is not installable for normal users. Do not ship a self-signed development certificate or install one into a user's Trusted People store as a workaround.

Until a trusted publisher certificate is available, the installer keeps using the existing per-user registry verbs, which Windows may place under **Show more options**. After signing is provisioned, the release flow can sign `DesktopTuner.identity.msix`, install it for the current user with the app directory as its external location, copy the native DLL and manifest assets beside the app, and remove the package during uninstall. Registration and invocation then need verification on supported Windows 11 desktop builds.

This extension improves native Explorer menu integration. It does not replace Explorer windows, the taskbar, or the Start menu.
