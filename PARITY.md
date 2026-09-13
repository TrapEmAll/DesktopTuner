# StartAllBack parity tracker

This tracker uses StartAllBack's public product description as the baseline. Its official page currently advertises a classic-style Start menu, a redesigned taskbar, File Explorer and Control Panel styling, context-menu changes, and cross-app visual consistency. The detailed list below is an implementation target, not a claim that each item has equal internals or exact visual fidelity.

| Area | StartAllBack capability | Desktop Tuner status |
|---|---|---|
| Start | Search and launch apps | Partial: companion launcher ranks exact, prefix, word-boundary, and substring matches across Start-menu app and folder names, plus `shell:AppsFolder` apps; `Ctrl+Alt+Space` and optional Windows-key replacement with Win+key passthrough work; web search and Windows Search integration remain |
| Start | Classic menu styles and system-place shortcuts | Partial: Modern, Classic two-column, and Compact layouts, nested Start-menu folders, and Documents, Downloads, Settings, power, This PC, Control Panel, Network, media-folder, and Recent shortcuts; authentic Win7/8 styling and richer power actions remain |
| Taskbar | Classic/replacement taskbar and edge placement | Partial: live overlays on the primary display or all connected displays, with per-display placement and window switching; native taskbars remain underneath |
| Taskbar | Labels, icon size, margins, grouping, drag/drop | Partial: optional window labels, extracted app icons with three icon sizes, compact/standard/relaxed button spacing, centered or left-aligned app buttons while Start stays left, persistent reorderable launch pins, executable/shortcut/folder drag-and-drop, document-to-app drop, and three bar sizes/grouping preference exist; native taskbar integration remains |
| Taskbar | Segmented/floating/translucent styles and auto-hide | Partial: edge-aware auto-hide, floating inset style, and separate Start/apps/system segments; aura effects and Windows material translucency remain |
| Taskbar | Tray, taskbar context menus, widgets, flyouts, and multi-monitor behavior | Partial: per-window and grouped minimize/close menus; on supported bottom edge-to-edge and segmented layouts, the overlay stops at the detected native notification-area boundary so Windows' tray icons, clock, and flyouts remain visible and clickable; other layouts retain Tray and Clock shortcuts; widgets and other shell flyouts remain |
| Explorer | Restyled ribbon/command bar and translucent menus | Missing |
| Explorer | Bottom details pane and classic search | Missing |
| Explorer | Dark mode and common-dialog styling | Partial: shared Windows app/system color mode and transparency preferences; dark common-dialog restyling remains |
| Explorer | Context-menu appearance and behavior | Partial: per-user classic full-menu switch; custom acrylic styling and taskbar menus remain |
| Shell-wide | Classic applet behavior, fonts, colors, and UI consistency | Missing |
| Reliability | Windows-version compatibility, recovery and resource discipline | Partial: user-level setting snapshots and apply rollback exist; build matrix, updater, installer, telemetry-free resource profiling remain |

The current Windows App SDK and Win32 documentation do not provide public APIs for replacing the system taskbar or arbitrary built-in Start-menu visuals. Reaching behavioral parity in those areas will require compatibility-tested shell integration and a Windows-build test matrix; registry toggles alone do not meet the objective.
