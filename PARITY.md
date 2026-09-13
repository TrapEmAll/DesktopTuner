# StartAllBack parity tracker

This tracker uses StartAllBack's public product description as the baseline. Its official page currently advertises a classic-style Start menu, a redesigned taskbar, File Explorer and Control Panel styling, context-menu changes, and cross-app visual consistency. The detailed list below is an implementation target, not a claim that each item has equal internals or exact visual fidelity.

| Area | StartAllBack capability | Desktop Tuner status |
|---|---|---|
| Start | Search and launch apps | Partial: companion launcher, Start-menu `.lnk` search, `Ctrl+Alt+Space`; Windows-key replacement and packaged-app indexing remain |
| Start | Classic menu styles and system-place shortcuts | Partial: one custom menu and Documents, Downloads, Settings shortcuts; legacy layouts, cascading folders, and richer system actions remain |
| Taskbar | Classic/replacement taskbar and edge placement | Missing |
| Taskbar | Labels, icon size, margins, grouping, drag/drop | Partial: grouping and alignment values only; visuals, drag/drop, and live behavior remain |
| Taskbar | Segmented/floating/translucent styles and auto-hide | Missing |
| Taskbar | Tray, taskbar context menus, widgets, flyouts, and multi-monitor behavior | Missing |
| Explorer | Restyled ribbon/command bar and translucent menus | Missing |
| Explorer | Bottom details pane and classic search | Missing |
| Explorer | Dark mode and common-dialog styling | Missing |
| Explorer | Context-menu appearance and behavior | Missing |
| Shell-wide | Classic applet behavior, fonts, colors, and UI consistency | Missing |
| Reliability | Windows-version compatibility, recovery and resource discipline | Partial: user-level setting snapshots and apply rollback exist; build matrix, updater, installer, telemetry-free resource profiling remain |

The current Windows App SDK and Win32 documentation do not provide public APIs for replacing the system taskbar or arbitrary built-in Start-menu visuals. Reaching behavioral parity in those areas will require compatibility-tested shell integration and a Windows-build test matrix; registry toggles alone do not meet the objective.
