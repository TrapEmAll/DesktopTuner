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

Windows [Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/) can start a Win32 or UWP application in place of `Explorer.exe`. Microsoft supports it on Enterprise, Education, and IoT Enterprise editions; it is an optional Windows feature, and a shell assignment takes effect at sign-in. Shell Launcher v2 is a whole-shell transition, not a taskbar customization API. Its return-action mapping can restart a shell when it exits.

Desktop Tuner now has an opt-in `--shell-host` runtime mode for use as a per-user Shell Launcher target. It starts the desktop host, brings up companion taskbars on every connected display, supplies the custom Start menu and settings window, routes the Windows key to the custom Start menu, and leaves Explorer-taskbar hiding disabled because Explorer is not the shell. The companion settings window stays available from the custom taskbar and hides instead of closing the shell process. Exiting the desktop host ends the process; Shell Launcher should be configured to restart the shell after process exit.

## Home/Pro shell overlay

`DesktopTuner.exe --shell-overlay` provides a per-user alternative on Windows Home and Pro, where Shell Launcher is unavailable. It starts the custom desktop, Start menu, and taskbars on all connected displays, routes the Windows key to Desktop Tuner Start, hides Explorer's taskbars, and reserves the custom taskbar work area. Explorer remains running in the background, so its desktop, taskbar process, shell services, and recovery options still exist; this is a session overlay, not a Winlogon shell replacement. Enable **Start the shell overlay automatically when I sign in** in Taskbar settings to create the Startup shortcut with this launch mode. Choosing Exit from the custom taskbar restores the taskbars and stops Desktop Tuner. A recovery watchdog also restores captured taskbar visibility if Desktop Tuner exits unexpectedly; if both Desktop Tuner and its watchdog are terminated, restart Explorer or sign out.

The overlay is experimental. It can fail to hide or reserve space for an Explorer taskbar on a Windows build or display configuration; in that case Desktop Tuner restores the captured native taskbars and closes the custom bars. It does not prevent users or applications from starting Explorer, and it does not replace native Explorer windows, shell services, or every Windows flyout.

This runtime path is experimental and does not configure Windows. Shell Launcher configuration requires an administrator and a supported edition. A managed-edition test must use a separate, retained administrator account that remains on Explorer; target only a non-administrator test account. Keep Ctrl+Alt+Delete available for signing out, and test the rollback path before using it as a daily shell. Do not assign Desktop Tuner as the default shell or to a broad user group.

For a manual test on a supported Windows edition, install Desktop Tuner in the non-administrator test account so its per-user installation is readable there. Then enable the optional **Shell Launcher** Windows feature and use an elevated PowerShell session to add a per-user mapping. Replace the SID and executable path with values for the test account; do not use the elevated administrator's `%LOCALAPPDATA%` path:

```powershell
$testSid = "S-1-5-21-REPLACE-WITH-TEST-ACCOUNT-SID"
$executable = "C:\Users\TEST-ACCOUNT\AppData\Local\Programs\DesktopTuner\DesktopTuner.exe"
$shell = "`"$executable`" --shell-host"
$shellLauncher = [wmiclass]"\\localhost\root\standardcimv2\embedded:WESL_UserSetting"
$shellLauncher.SetCustomShell($testSid, $shell, @(), @(), 0)
$shellLauncher.SetEnabled($true)
```

Sign out and back into the test account for the mapping to take effect. `DefaultAction` value `0` restarts the shell after exit. To roll back the test account mapping from an elevated PowerShell session, remove it and sign out/in again:

```powershell
$testSid = "S-1-5-21-REPLACE-WITH-TEST-ACCOUNT-SID"
$shellLauncher = [wmiclass]"\\localhost\root\standardcimv2\embedded:WESL_UserSetting"
$shellLauncher.RemoveCustomShell($testSid)
```

The desktop host spans the Windows virtual desktop and persists free-form icon positions in that shared coordinate space, but does not yet support independent per-monitor layouts or the full shell namespace. The custom taskbar does not yet reproduce arbitrary notification-area extensions, Windows' native Start/taskbar relationship, or every shell flyout. CI can build the mode but cannot validate sign-in, recovery, Shell Launcher edition behavior, or display integration on a real Windows 11 desktop.

## Distribution gate

The repository has no production code-signing certificate. Windows requires the sparse identity package to be signed by a publisher certificate trusted on the target PC before it can be registered. The unsigned package produced for validation is not installable for normal users. Do not ship a self-signed development certificate or install one into a user's Trusted People store as a workaround.

Until a trusted publisher certificate is available, the installer keeps using the existing per-user registry verbs, which Windows may place under **Show more options**. After signing is provisioned, the release flow can sign `DesktopTuner.identity.msix`, install it for the current user with the app directory as its external location, copy the native DLL and manifest assets beside the app, and remove the package during uninstall. Registration and invocation then need verification on supported Windows 11 desktop builds.

The Explorer extension improves context-menu integration. It does not replace Explorer windows or the Start menu.
