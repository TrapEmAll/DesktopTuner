using DesktopTuner;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var count = 0;
var navigationTestRoot = Path.Combine(Path.GetTempPath(), $"desktop-tuner-navigation-{Guid.NewGuid():N}");
try
{
    var nestedPath = Path.Combine(navigationTestRoot, "Alpha");
    var hiddenPath = Path.Combine(navigationTestRoot, "Hidden");
    Directory.CreateDirectory(nestedPath);
    Directory.CreateDirectory(hiddenPath);
    File.SetAttributes(hiddenPath, FileAttributes.Hidden);
    Check("Alpha", string.Join(',', ExplorerNavigationService.ReadDirectories(navigationTestRoot, showHiddenItems: false).Directories.Select(directory => directory.Name)), "hide hidden folders in Explorer navigation when Windows hidden items are off");
    Check("Alpha,Hidden", string.Join(',', ExplorerNavigationService.ReadDirectories(navigationTestRoot, showHiddenItems: true).Directories.Select(directory => directory.Name)), "include hidden folders in Explorer navigation when enabled");
    var breadcrumbs = ExplorerBreadcrumbPolicy.Create(nestedPath);
    Check(Path.GetPathRoot(nestedPath), breadcrumbs[0].Label, "show the drive root as the first Explorer breadcrumb");
    Check(nestedPath, breadcrumbs[^1].Path, "keep the final Explorer breadcrumb pointed at the active folder");
    CheckTrue(ExplorerNavigationService.ReadDirectories(Path.Combine(navigationTestRoot, "Missing"), showHiddenItems: false).Error is not null, "report unavailable folders in the Explorer navigation tree");
    var nestedNavigationPath = Path.Combine(navigationTestRoot, "Alpha", "Nested");
    var siblingPrefixPath = Path.Combine(navigationTestRoot + "-other", "Nested");
    CheckTrue(ExplorerNavigationPathPolicy.IsSameOrDescendant(navigationTestRoot, navigationTestRoot), "match the Explorer navigation root itself");
    CheckTrue(ExplorerNavigationPathPolicy.IsSameOrDescendant(navigationTestRoot, nestedNavigationPath), "match a descendant in the Explorer navigation tree");
    Check(false, ExplorerNavigationPathPolicy.IsSameOrDescendant(navigationTestRoot, siblingPrefixPath), "avoid matching folders that only share a path prefix");
    Check("Alpha,Nested", string.Join(',', ExplorerNavigationPathPolicy.GetRelativeSegments(navigationTestRoot, nestedNavigationPath)), "split active Explorer folders into navigation-tree segments");
    using var canceledNavigation = new CancellationTokenSource();
    canceledNavigation.Cancel();
    Throws<OperationCanceledException>(() => ExplorerNavigationService.ReadDirectories(navigationTestRoot, showHiddenItems: false, canceledNavigation.Token), "cancel Explorer navigation enumeration before it reads a folder");
    var driveRoots = ExplorerNavigationService.ReadDriveRoots().Directories;
    CheckTrue(driveRoots.Count > 0, "list available drive roots in Explorer This PC navigation");
    CheckTrue(driveRoots.All(drive => !string.IsNullOrWhiteSpace(drive.Path)), "provide navigable paths for Explorer This PC drive roots");
}
finally
{
    if (Directory.Exists(navigationTestRoot)) Directory.Delete(navigationTestRoot, recursive: true);
}

Check(false, SystemBackdropService.TryApplyTransientBackdrop(IntPtr.Zero), "leave unsupported menu handles on the solid background");
Check(false, SystemBackdropService.TryApplySmallRoundedCorners(IntPtr.Zero), "leave unsupported menu handles with system-default corners");
Check(false, new DesktopPreferences(TaskbarEdge.Bottom).TaskbarOnAllDisplays, "preserve the primary-display behavior for older preference data");
Check(false, new DesktopPreferences(TaskbarEdge.Bottom).ReplaceNativeTaskbar, "leave native taskbar replacement disabled by default");
Check<TaskbarBatteryStatus?>(null, TaskbarBatteryService.Parse(0, 0x80, 57, 3600), "hide the replacement taskbar battery control when no battery is installed");
var chargingBattery = TaskbarBatteryService.Parse(1, 0x08, 72, 5400)!;
Check(new TaskbarBatteryStatus(72, true, true, 5400), chargingBattery, "decode charging and AC state with a valid battery estimate");
Check("Battery: 72% · Charging", TaskbarBatteryService.GetLabel(chargingBattery), "describe charging battery status without showing a discharge estimate");
var dischargingBattery = TaskbarBatteryService.Parse(0, 0, 42, 8100)!;
Check("Battery: 42% · About 2 h 15 min remaining", TaskbarBatteryService.GetLabel(dischargingBattery), "format remaining battery time in the replacement taskbar tooltip");
var unknownBattery = TaskbarBatteryService.Parse(255, 0, 255, uint.MaxValue)!;
Check(new TaskbarBatteryStatus(null, false, false, null), unknownBattery, "treat unknown battery readings as unavailable without inventing values");
Check("Battery status unavailable", TaskbarBatteryService.GetLabel(unknownBattery), "show a useful label when the battery percentage is unavailable");
Check(6.5d, TaskbarBatteryService.GetFillWidth(50, 13), "scale the battery glyph fill to its reported percentage");
Check(0d, TaskbarBatteryService.GetFillWidth(-1, 13), "clamp battery glyph fill below zero");
Check(13d, TaskbarBatteryService.GetFillWidth(150, 13), "clamp battery glyph fill above one hundred percent");
Throws<ArgumentOutOfRangeException>(() => TaskbarBatteryService.GetFillWidth(50, double.NaN), "reject invalid battery glyph width");
Check("Details,List,SmallIcons,MediumIcons,LargeIcons,Tiles", string.Join(',', ExplorerViewModeCatalog.Options.Select(option => option.Mode)), "offer familiar Explorer details, list, small, medium, large, and tile layouts");
Check("ExplorerMediumIconTemplate", ExplorerViewModeCatalog.Get(ExplorerViewMode.MediumIcons).ItemTemplateKey, "map the medium icon layout to its item template");
CheckTrue(ExplorerViewModeCatalog.Get(ExplorerViewMode.LargeIcons).WrapItems, "wrap large Explorer icons to the available viewport");
Check("ExplorerSmallIconTemplate", ExplorerViewModeCatalog.Get(ExplorerViewMode.SmallIcons).ItemTemplateKey, "map the small icon layout to its item template");
CheckTrue(ExplorerViewModeCatalog.Get(ExplorerViewMode.Tiles).WrapItems, "wrap Explorer tiles to the available viewport");
CheckTrue(NativeTaskbarWatchdog.IsWatchdogInvocation(["--taskbar-watchdog", "123", "snapshot.json"]), "recognize the taskbar recovery process entry point");
CheckTrue(NativeTaskbarWatchdog.TryReadInvocation(["--taskbar-watchdog", "123", "snapshot.json"], out var watchdogOwner, out var watchdogSnapshot), "parse taskbar recovery process arguments");
Check((123, "snapshot.json"), (watchdogOwner, watchdogSnapshot), "recover the watchdog owner and snapshot path");
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom), false), "bottom, standard");
Check(new TaskbarBounds(0, 0, 1920, 46), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Top, TaskbarSize.Small), false), "top, small");
Check(new TaskbarBounds(0, 0, 204, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Large), false), "left, large");
Check(new TaskbarBounds(1744, 0, 176, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Right), false), "right, standard");
Check(new TaskbarBounds(1916, 0, 4, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Right), true), "right, collapsed");
Check(new TaskbarBounds(0, 1076, 1920, 4), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom), true), "bottom, collapsed");
Check(new TaskbarBounds(288, 1014, 1344, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Floating), false), "center a floating bar with a bottom screen inset");
Check(new TaskbarBounds(12, 162, 176, 756), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Floating), false), "shorten and inset a floating vertical bar");
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Segmented), false), "keep segmented taskbar regions across the selected screen edge");
Check(new TaskbarBounds(0, 0, 176, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Segmented), false), "retain full-height geometry for vertical segmented regions");
var trayDisplay = new TaskbarDisplay("DISPLAY1", 0, 0, 1920, 1080, true);
var nativeTray = new TaskbarBounds(1500, 1030, 420, 50);
Check(new TaskbarBounds(0, 1026, 1500, 54), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), nativeTray), "leave the native notification area uncovered on an edge-to-edge bar");
Check(new TaskbarBounds(0, 1026, 1500, 54), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom, TaskbarLayout: TaskbarStyle.Segmented), nativeTray), "leave the native notification area uncovered on a segmented bar");
Check(new TaskbarBounds(0, 0, 1500, 46), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Top, TaskbarSize.Small), new(1500, 0, 420, 50)), "leave a top-edge native notification area uncovered");
Check(new TaskbarBounds(0, 0, 176, 800), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Left), new(0, 800, 176, 280)), "leave a bottom-corner native notification area uncovered on a left bar");
Check(new TaskbarBounds(1744, 0, 176, 800), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Right), new(1744, 800, 176, 280)), "leave a bottom-corner native notification area uncovered on a right bar");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom, TaskbarLayout: TaskbarStyle.Floating), nativeTray), "keep shortcut fallback on floating bars");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Top), nativeTray), "keep shortcut fallback on top bars");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Left), new(900, 800, 176, 280)), "keep shortcut fallback when a vertical bar does not share the tray edge");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), null), "keep shortcut fallback when the native tray is unavailable");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), new(100, 1030, 100, 50)), "keep shortcut fallback when the tray boundary leaves too little taskbar space");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), new(1500, 700, 420, 50)), "keep shortcut fallback when tray is not at the bottom edge");
var secondaryDisplay = new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false, 1.5, 1.5);
Check(new TaskbarBounds(-1920, 799, 1320, 81), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(secondaryDisplay, new(TaskbarEdge.Bottom), new(-600, 835, 600, 45)), "preserve native tray geometry on a scaled secondary display");
CheckTrue(TaskbarDisplayService.Overlaps(nativeTray, trayDisplay), "match native taskbar windows to their display bounds");
CheckTrue(!TaskbarDisplayService.Overlaps(nativeTray, secondaryDisplay), "keep native taskbars scoped to selected displays");
var maximizedOnSecondary = new RunningWindow((nint)4, "Document", "Editor", @"C:\Apps\editor.exe", false)
{
    IsMaximized = true,
    IsForeground = true,
    Bounds = new TaskbarBounds(-1920, -200, 1920, 1080)
};
CheckTrue(TaskbarAutoHidePolicy.HasMaximizedWindowOnDisplay([maximizedOnSecondary], secondaryDisplay), "detect a maximized app covering its own display");
CheckTrue(!TaskbarAutoHidePolicy.HasMaximizedWindowOnDisplay([maximizedOnSecondary], trayDisplay), "do not hide other displays for a maximized app");
var halfScreenWindow = maximizedOnSecondary with { Bounds = new TaskbarBounds(-1920, -200, 960, 1080) };
CheckTrue(!TaskbarAutoHidePolicy.HasMaximizedWindowOnDisplay([halfScreenWindow], secondaryDisplay), "do not treat a half-width window as a display-covering maximized app");
var backgroundMaximizedWindow = maximizedOnSecondary with { IsForeground = false };
CheckTrue(!TaskbarAutoHidePolicy.HasMaximizedWindowOnDisplay([backgroundMaximizedWindow], secondaryDisplay), "do not hide for a maximized app behind the foreground window");
var primaryDisplay = new TaskbarDisplay("DISPLAY1", 0, 0, 2560, 1440, true, 1.25, 1.25);
var secondaryBar = TaskbarLayoutCalculator.Calculate(secondaryDisplay, new(TaskbarEdge.Bottom), false);
Check(-1920d, secondaryBar.Left, "place taskbar on a monitor with negative desktop coordinates");
Check(799d, secondaryBar.Top, "scale taskbar thickness for a high-DPI display");
Check(1920d, secondaryBar.Width, "span the taskbar across the selected monitor only");
Check(81d, secondaryBar.Height, "scale the taskbar height in physical pixels");
Check(new TaskbarBounds(570, 786, 760, 232), TaskbarPreviewLayoutPolicy.Calculate(trayDisplay, TaskbarEdge.Bottom, new(900, 1026, 100, 54), 760, 232), "position window thumbnails above a bottom taskbar button");
Check(new TaskbarBounds(570, 54, 760, 232), TaskbarPreviewLayoutPolicy.Calculate(trayDisplay, TaskbarEdge.Top, new(900, 0, 100, 46), 760, 232), "position window thumbnails below a top taskbar button");
Check(new TaskbarBounds(62, 234, 760, 232), TaskbarPreviewLayoutPolicy.Calculate(trayDisplay, TaskbarEdge.Left, new(0, 300, 54, 100), 760, 232), "position window thumbnails to the right of a left taskbar button");
Check(new TaskbarBounds(976, 234, 760, 232), TaskbarPreviewLayoutPolicy.Calculate(trayDisplay, TaskbarEdge.Right, new(1744, 300, 176, 100), 760, 232), "position window thumbnails to the left of a right taskbar button");
var secondaryStart = TaskbarLayoutCalculator.CalculateStartMenu(secondaryDisplay, 470, 650, new(TaskbarEdge.Bottom));
Check(-1902d, secondaryStart.Left, "anchor Start menu to the selected secondary display");
Check(-188d, secondaryStart.Top, "keep Start menu within display bounds above its taskbar");
var floatingStart = TaskbarLayoutCalculator.CalculateStartMenu(secondaryDisplay, 470, 650, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Floating));
Check(-1614d, floatingStart.Left, "anchor Start menu to the floating taskbar segment");
Check(2, TaskbarDisplayService.Select([secondaryDisplay, primaryDisplay], true).Count, "select all connected displays");
Check("DISPLAY1", TaskbarDisplayService.Select([secondaryDisplay, primaryDisplay], false).Single().DeviceName, "select only the primary display");
CheckTrue(TaskbarAutoHidePolicy.ShouldCollapse(true, false, false), "auto-hide collapses when idle");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(false, false, false), "auto-hide respects disabled state");
var autoHideShortcutPreferences = new DesktopPreferences(TaskbarEdge.Top, AutoHideWhenMaximized: true, TaskbarTransparency: 35);
var autoHideShortcutEnabled = TaskbarAutoHideHotkeyPolicy.Toggle(autoHideShortcutPreferences);
Check(true, autoHideShortcutEnabled.AutoHide, "turn on taskbar auto-hide from its global shortcut");
Check(true, autoHideShortcutEnabled.AutoHideWhenMaximized, "preserve maximize-aware auto-hide while toggling idle auto-hide");
Check(35, autoHideShortcutEnabled.TaskbarTransparency, "preserve other taskbar settings while toggling auto-hide");
Check(false, TaskbarAutoHideHotkeyPolicy.Toggle(autoHideShortcutEnabled).AutoHide, "turn taskbar auto-hide off with the same shortcut");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(true, true, false), "auto-hide stays expanded while pointer is over it");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(true, false, true), "auto-hide stays expanded while Start is open");
CheckTrue(TaskbarAutoHidePolicy.ShouldCollapse(false, true, true, false, false), "hide when maximize-aware auto-hide is enabled and the display is covered");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(false, true, false, false, false), "keep the taskbar visible without a maximized window on the display");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(false, true, true, true, false), "reveal a hidden taskbar when the pointer reaches its edge");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(false, true, true, false, true), "keep the taskbar visible while its Start menu is open");
Check((byte)255, TaskbarTransparencyPolicy.GetAlpha(0), "make a zero-transparency taskbar fully opaque");
CheckTrue(Math.Abs(AudioVolumePolicy.Adjust(0.3f, 120) - 0.35f) < 0.0001f, "raise audio volume by one mouse-wheel notch");
CheckTrue(Math.Abs(AudioVolumePolicy.Adjust(0.3f, -120) - 0.25f) < 0.0001f, "lower audio volume by one mouse-wheel notch");
Check(1f, AudioVolumePolicy.Adjust(0.99f, 120), "clamp audio volume to the maximum");
Check(0f, AudioVolumePolicy.Adjust(0.01f, -120), "clamp audio volume to silence");
Check("Muted · 42%", AudioVolumePolicy.GetLabel(0.42f, true), "format muted audio status for the taskbar control");
CheckTrue(AudioVolumePolicy.IsDefaultOutput("endpoint-a", "ENDPOINT-A"), "identify the selected audio output without case-sensitive endpoint matching");
CheckTrue(!AudioVolumePolicy.IsDefaultOutput("endpoint-a", null), "leave audio outputs unselected when Windows has no default endpoint");
Throws<ArgumentOutOfRangeException>(() => AudioVolumePolicy.Adjust(float.NaN, 120), "reject invalid audio volume values");
Check((byte)128, TaskbarTransparencyPolicy.GetAlpha(50), "convert fifty percent transparency to the expected alpha");
Check((byte)77, TaskbarTransparencyPolicy.GetAlpha(100), "bound taskbar transparency to retain a visible backdrop");
Check(0, TaskbarTransparencyPolicy.Clamp(-10), "clamp negative transparency values");
Check(70, TaskbarTransparencyPolicy.Clamp(100), "cap transparency to preserve taskbar contrast");
Check(30, TaskbarTransparencyPolicy.GetEffectiveTransparency(30, dynamicTransparency: false, maximizedWindowOnDisplay: false), "keep the configured transparency when adaptive mode is off");
Check(0, TaskbarTransparencyPolicy.GetEffectiveTransparency(30, dynamicTransparency: true, maximizedWindowOnDisplay: false), "make the taskbar solid on the desktop in adaptive mode");
Check(30, TaskbarTransparencyPolicy.GetEffectiveTransparency(30, dynamicTransparency: true, maximizedWindowOnDisplay: true), "apply the selected transparency when a maximized app covers the display");
var elevatedShortcutApp = new AppEntry("Editor", @"C:\Apps\Editor.lnk");
CheckTrue(AppCatalogService.CanRunAsAdministrator(elevatedShortcutApp), "offer elevation for Start menu shortcuts");
CheckTrue(AppCatalogService.CanRunAsAdministrator(new AppEntry("Editor", @"C:\Apps\Editor.exe")), "offer elevation for direct executables");
CheckTrue(!AppCatalogService.CanRunAsAdministrator(new AppEntry("Calculator", "CalculatorApp!App", IsPackagedApp: true)), "do not offer elevation for packaged Windows apps");
CheckTrue(!AppCatalogService.CanRunAsAdministrator(new AppEntry("Readme", @"C:\Docs\readme.txt")), "do not offer elevation for documents");
var elevatedLaunchInfo = AppCatalogService.BuildLaunchInfo(elevatedShortcutApp, runAsAdministrator: true);
Check(elevatedShortcutApp.ShortcutPath, elevatedLaunchInfo.FileName, "launch the selected shortcut path when elevating");
Check("runas", elevatedLaunchInfo.Verb, "request the Windows elevation verb only for an explicit Start menu action");
CheckTrue(elevatedLaunchInfo.UseShellExecute, "use ShellExecute for the Windows elevation prompt");
Throws<NotSupportedException>(() => AppCatalogService.BuildLaunchInfo(new AppEntry("Calculator", "CalculatorApp!App", IsPackagedApp: true), runAsAdministrator: true), "reject unsupported elevation for packaged Start apps");
var systemAccent = TaskbarTheme.ResolveAccentBrushes(0xFF336699);
Check("#FF336699", systemAccent.Accent, "read DWM colorization values as Windows accent RGB");
Check("#FF264C73", systemAccent.Fallback, "darken the Windows accent for white taskbar fallback icons");
var desktopAccent = SystemAccentColorService.ResolveDesktopAccent(dark: false, 0xFF336699);
Check("#FFE1E8F1", desktopAccent.Tint, "blend the Windows accent into the Start and Explorer surface tint");
Check("#FF336699", desktopAccent.Text, "keep the Windows accent readable on the light surface tint");
var darkDesktopAccent = SystemAccentColorService.ResolveDesktopAccent(dark: true, 0xFF336699);
Check("#FF273141", darkDesktopAccent.Tint, "blend the Windows accent into the dark Start and Explorer surface tint");
Check("#FF86A4C3", darkDesktopAccent.Text, "keep the Windows accent readable on the dark surface tint");
var firstPin = TaskbarPinCatalog.Add([], "Editor", @"C:\Program Files\Editor\editor.exe");
Check(1, firstPin.Count, "pin a running app executable");
Check(1, TaskbarPinCatalog.Add(firstPin, "Editor", @"C:\Program Files\Editor\editor.exe").Count, "avoid duplicate pins");
Check(1, TaskbarPinCatalog.Add(firstPin, "Script", @"C:\Tools\script.cmd").Count, "reject non-executable pin paths");
Check(0, TaskbarPinCatalog.Remove(firstPin, @"C:\Program Files\Editor\editor.exe").Count, "unpin an app executable");
var orderedPins = new[]
{
    new PinnedTaskbarApp("Editor", @"C:\Apps\editor.exe"),
    new PinnedTaskbarApp("Browser", @"C:\Apps\browser.exe"),
    new PinnedTaskbarApp("Mail", @"C:\Apps\mail.exe")
};
CheckTrue(TaskbarPinCatalog.Move(orderedPins, @"C:\Apps\editor.exe", @"C:\Apps\mail.exe")
    .Select(pin => pin.Name).SequenceEqual(["Browser", "Editor", "Mail"]), "move a pinned app before its drop target");
CheckTrue(TaskbarPinCatalog.Move(orderedPins, @"C:\Apps\mail.exe", @"C:\Apps\editor.exe")
    .Select(pin => pin.Name).SequenceEqual(["Mail", "Editor", "Browser"]), "move a pinned app toward the start of the taskbar");
CheckTrue(TaskbarPinCatalog.Move(orderedPins, @"C:\Apps\missing.exe", @"C:\Apps\editor.exe")
    .Select(pin => pin.Name).SequenceEqual(["Editor", "Browser", "Mail"]), "leave pins unchanged for an unknown drag source");
var droppedPaths = new[]
{
    @"C:\Apps\Editor.exe",
    @"C:\Apps\Editor.exe",
    @"C:\Apps\Editor Shortcut.lnk",
    @"C:\Docs\Projects",
    @"C:\Docs\readme.txt",
    "relative.exe"
};
var droppedPins = TaskbarPinCatalog.AddDroppedFiles([], droppedPaths, path => path == @"C:\Docs\Projects");
Check(3, droppedPins.Count, "accept executable, shortcut, and folder drops while rejecting documents and relative paths");
Check("Editor", droppedPins[0].Name, "derive dropped app name from executable filename");
Check("Editor Shortcut", droppedPins[1].Name, "derive dropped shortcut name without its extension");
Check(true, droppedPins[2].IsDirectory, "preserve folder pins as folder targets");
Check(3, TaskbarPinCatalog.AddDroppedFiles(droppedPins, [@"C:\Docs\Projects"], path => path == @"C:\Docs\Projects").Count, "avoid duplicate folder pins");
var runningWindows = new[]
{
    new RunningWindow((nint)1, "Document one", "Editor", @"C:\Apps\editor.exe", false) { IsForeground = true },
    new RunningWindow((nint)2, "Document two", "Editor", @"C:\Apps\EDITOR.exe", false),
    new RunningWindow((nint)3, "Inbox", "Mail", @"C:\Apps\mail.exe", false)
};
var alwaysGroupedWindows = TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Always, 10);
CheckTrue(TaskbarWindowActivationPolicy.ShouldMinimize(isForeground: true, isMinimized: false), "minimize an already active taskbar window on a second click");
CheckTrue(!TaskbarWindowActivationPolicy.ShouldMinimize(isForeground: true, isMinimized: true), "restore rather than minimize an already minimized taskbar window");
CheckTrue(!TaskbarWindowActivationPolicy.ShouldMinimize(isForeground: false, isMinimized: false), "activate a taskbar window when another app is foreground");
Check(2, alwaysGroupedWindows.Count, "always group windows from the same executable");
Check("Editor (2)", alwaysGroupedWindows[0].Label, "show the app name and window count for a grouped button");
CheckTrue(alwaysGroupedWindows[0].IsActive, "mark an app group active when one of its windows is foreground");
Check("Document one" + Environment.NewLine + "Document two", alwaysGroupedWindows[0].ToolTip, "list window titles in a grouped button tooltip");
Check((nint)1, TaskbarWindowGrouping.SelectCloseTarget(alwaysGroupedWindows[0])!.Handle, "middle-click closes the foreground window in a taskbar group");
var inactiveWindowGroup = new TaskbarWindowGroup("Editor", "Editor", [runningWindows[1]]);
Check((nint)2, TaskbarWindowGrouping.SelectCloseTarget(inactiveWindowGroup)!.Handle, "middle-click closes the only window when no group member is foreground");
CheckTrue(TaskbarWindowGrouping.SelectCloseTarget(new TaskbarWindowGroup("Empty", "Empty", [])) is null, "ignore middle-click when a taskbar group has no live windows");
Check(3, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Never, 1).Count, "never group taskbar windows");
Check(3, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.WhenFull, 3).Count, "keep windows separate while the taskbar has capacity");
Check(2, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.WhenFull, 2).Count, "group windows when the taskbar is full");
var editorPin = new PinnedTaskbarApp("Editor", @"C:\Apps\editor.exe");
CheckTrue(TaskbarWindowGrouping.MatchesPinnedApp(editorPin, runningWindows[1]), "match a running window to its pinned app without case-sensitive path differences");
var editorShortcutPin = new PinnedTaskbarApp("Editor shortcut", @"C:\Apps\Editor.lnk");
CheckTrue(TaskbarPinIdentityService.Matches(editorShortcutPin, runningWindows[0], _ => @"C:\Apps\Editor.exe"), "match a running app to the executable target of its pinned shortcut");
CheckTrue(!TaskbarPinIdentityService.Matches(editorShortcutPin, runningWindows[0], _ => @"C:\Apps\Other.exe"), "keep a shortcut separate when its target is a different executable");
Check(1, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Never, 10, [editorPin]).Count, "show pinned apps only once instead of duplicating their running windows");
Check("Mail", TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Never, 10, [editorPin]).Single().ApplicationName, "retain unrelated running apps when hiding pinned duplicates");
RunningWindow[] twoMailWindows =
[
    .. runningWindows,
    new RunningWindow((nint)4, "Sent", "Mail", @"C:\Apps\mail.exe", false)
];
var pinnedWindowsBelowCapacity = TaskbarWindowGrouping.Create(twoMailWindows, TaskbarGroupingMode.WhenFull, 2, [editorPin]);
Check(2, pinnedWindowsBelowCapacity.Count, "keep unpinned windows separate until their own taskbar area is full");
Check("Inbox,Sent", string.Join(',', pinnedWindowsBelowCapacity.Select(group => group.Windows.Single().Title)), "exclude windows already shown by pinned apps from grouping capacity");
CheckTrue(!TaskbarWindowGrouping.MatchesPinnedApp(editorPin with { IsDirectory = true }, runningWindows[0]), "do not associate a folder pin with an app window");
var windowOrder = new TaskbarWindowOrder();
Check("1,2,3", string.Join(',', windowOrder.Synchronize(runningWindows).Select(window => window.Handle)), "initialize running-window order from the current enumeration");
CheckTrue(windowOrder.MoveGroup(runningWindows, alwaysGroupedWindows[1], alwaysGroupedWindows[0]), "move a grouped app's taskbar button before another group");
Check("3,1,2", string.Join(',', windowOrder.Synchronize(runningWindows).Select(window => window.Handle)), "move all windows in a grouped button as one block");
CheckTrue(!windowOrder.MoveGroup(runningWindows, alwaysGroupedWindows[0], alwaysGroupedWindows[0]), "ignore a drop onto the same running-window group");
CheckTrue(windowOrder.MoveWindow(runningWindows, runningWindows[1], runningWindows[2]), "move an individual window preview before another preview");
Check("2,3,1", string.Join(',', windowOrder.Synchronize(runningWindows).Select(window => window.Handle)), "retain the selected order of individual window previews");
CheckTrue(!windowOrder.MoveWindow(runningWindows, runningWindows[1], runningWindows[1]), "ignore dropping a preview onto itself");
CheckTrue(!windowOrder.MoveWindow(runningWindows, runningWindows[1], new RunningWindow((nint)99, "Missing", "Missing", string.Empty, false)), "ignore moving a preview to a window that is no longer open");
var updatedRunningWindows = new[] { runningWindows[0], runningWindows[2], new RunningWindow((nint)4, "Calendar", "Calendar", @"C:\Apps\calendar.exe", false) };
Check("3,1,4", string.Join(',', windowOrder.Synchronize(updatedRunningWindows).Select(window => window.Handle)), "remove closed windows and append newly opened windows without losing the chosen order");
Check(120d, TaskbarButtonAlignmentPolicy.CalculateLeadingSpacer(500, 260, TaskbarButtonAlignment.Center), "center taskbar buttons within the free app area");
Check(0d, TaskbarButtonAlignmentPolicy.CalculateLeadingSpacer(500, 260, TaskbarButtonAlignment.Left), "keep left-aligned taskbar buttons at the start of the app area");
Check(0d, TaskbarButtonAlignmentPolicy.CalculateLeadingSpacer(260, 500, TaskbarButtonAlignment.Center), "keep overflowing taskbar buttons reachable from the start");
Check(16, TaskbarIconSizePolicy.GetPixels(TaskbarIconSize.Small), "use small taskbar app icons");
Check(20, TaskbarIconSizePolicy.GetPixels(TaskbarIconSize.Standard), "use standard taskbar app icons");
Check(24, TaskbarIconSizePolicy.GetPixels(TaskbarIconSize.Large), "use large taskbar app icons");
Throws<ArgumentOutOfRangeException>(() => TaskbarIconSizePolicy.GetPixels((TaskbarIconSize)99), "reject unknown taskbar icon sizes");
Check(new Thickness(1, 0, 1, 0), TaskbarButtonSpacingPolicy.GetButtonMargin(TaskbarButtonSpacing.Compact, false), "apply compact spacing on a horizontal taskbar");
Check(new Thickness(0, 4, 0, 4), TaskbarButtonSpacingPolicy.GetButtonMargin(TaskbarButtonSpacing.Relaxed, true), "apply relaxed spacing on a vertical taskbar");
Throws<ArgumentOutOfRangeException>(() => TaskbarButtonSpacingPolicy.GetGap((TaskbarButtonSpacing)99), "reject unknown taskbar spacing values");
CheckTrue(TaskbarIconService.LoadIcon(Environment.ProcessPath!) is not null, "extract a taskbar icon from an executable file");
CheckTrue(new AppEntry("Desktop Tuner", Environment.ProcessPath!).Icon is not null, "expose extracted app icons to Start menu entries");
var auraIcon = BitmapSource.Create(5, 1, 96, 96, PixelFormats.Bgra32, null,
new byte[]
{
    0, 255, 0, 0,
    45, 50, 220, 255,
    40, 55, 210, 255,
    220, 60, 40, 255,
    128, 128, 128, 255
}, 20);
Check<Color?>(Color.FromRgb(215, 52, 42), TaskbarAuraColorPolicy.ResolvePrimaryColor(auraIcon), "derive an Aura highlight from the dominant saturated app-icon color");
var monochromeIcon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 128, 128, 128, 255 }, 4);
Check<Color?>(null, TaskbarAuraColorPolicy.ResolvePrimaryColor(monochromeIcon), "use the system accent when an app icon has no dominant chromatic color");
var auraBrush = TaskbarAuraColorPolicy.CreateBrush(Color.FromRgb(220, 50, 40));
Check(Color.FromArgb(140, 220, 50, 40), auraBrush.GradientStops[0].Color, "create a soft app-colored Aura glow");
var presentationPreferences = new DesktopPreferences(TaskbarEdge.Bottom, TaskbarShowLabels: false, TaskbarIconSize: TaskbarIconSize.Large);
var pinPresentation = TaskbarButtonViewModel.FromPin(new PinnedTaskbarApp("Editor", Environment.ProcessPath!), presentationPreferences, vertical: false);
Check("Editor", pinPresentation.Label, "retain pinned app labels in taskbar presentation data");
Check(false, pinPresentation.ShowLabel, "hide pinned app labels when the option is disabled");
Check(24, pinPresentation.IconPixels, "apply the selected icon size to pinned taskbar buttons");
Check(new Thickness(0), pinPresentation.IconMargin, "remove the icon-to-label gap when labels are hidden");
CheckTrue(TaskbarButtonViewModel.FromPin(editorPin, presentationPreferences, vertical: false, isRunning: true).IsRunning, "show a running indicator for an app absorbed into its pinned taskbar button");
CheckTrue(TaskbarButtonViewModel.FromPin(editorPin, presentationPreferences, vertical: false, isRunning: true, isActive: true).IsActive, "mark a running pinned app active when one of its windows is foreground");
Check(false, pinPresentation.AuraEnabled, "preserve the Windows accent highlight by default");
var dynamicAuraPresentation = TaskbarButtonViewModel.FromPin(editorPin, presentationPreferences with { TaskbarButtonEffect = TaskbarButtonEffect.DynamicAura }, vertical: false);
Check(true, dynamicAuraPresentation.AuraEnabled, "enable app-icon colors for the Aura button effect");
Check(true, dynamicAuraPresentation.DynamicAura, "enable pointer tracking for Dynamic Aura");
Check("#7A8497", TaskbarTheme.Resolve(dark: false).RunningIndicator, "use a subdued indicator for running apps in light mode");
var verticalPinPresentation = TaskbarButtonViewModel.FromPin(new PinnedTaskbarApp("Editor", Environment.ProcessPath!), presentationPreferences with { TaskbarButtonSpacing = TaskbarButtonSpacing.Relaxed }, vertical: true);
Check(new Thickness(0, 4, 0, 4), verticalPinPresentation.ButtonMargin, "apply selected vertical button spacing in taskbar presentation data");
var largeAppCatalog = Enumerable.Range(0, 55)
    .Select(index => new AppEntry($"Application {index:D2}", $@"C:\Apps\application{index:D2}.lnk"))
    .Append(new AppEntry("Zebra Editor", @"C:\Apps\zebra-editor.lnk"))
    .ToList();
Check(56, AppCatalogService.Search(largeAppCatalog).Count, "show every app in a large Start menu catalog");
Check("Zebra Editor", AppCatalogService.Search(largeAppCatalog, " zebra ").Single().Name, "search apps beyond the first 40 catalog entries");
var alphabeticalStartApps = StartMenuAppListPolicy.AddAlphabetMarkers(
[
    new AppEntry("! Audio Player", "audio-player.lnk"),
    new AppEntry("3D Viewer", "3d-viewer.lnk"),
    new AppEntry("7-Zip", "7-zip.lnk"),
    new AppEntry("Audio", "audio.lnk"),
    new AppEntry("Calculator", "calculator.lnk"),
    new AppEntry("Calendar", "calendar.lnk"),
    new AppEntry("Microsoft Edge", "edge.lnk")
]);
Check("#,-,-,A,C,-,M", string.Join(',', alphabeticalStartApps.Select(app => app.AlphabetMarker ?? "-")), "mark the first Start app in each alphabetic section and group numeric or symbol prefixes");
Check("Calendar", alphabeticalStartApps[5].Application.Name, "preserve app entries while adding Start alphabet markers");
var rankedSearchResults = AppCatalogService.Search(
[
    new AppEntry("TextEditor", "text-editor.lnk"),
    new AppEntry("Text Editor", "text editor.lnk"),
    new AppEntry("Editor Pro", "editor-pro.lnk"),
    new AppEntry("Editor", "editor.lnk"),
    new AppEntry("Documents", "documents.lnk", CategoryPath: "Creative Tools")
], "editor");
Check("Editor", rankedSearchResults[0].Name, "rank an exact app-name search first");
Check("Editor Pro", rankedSearchResults[1].Name, "rank app-name prefixes before broader matches");
Check("Text Editor", rankedSearchResults[2].Name, "rank word-boundary matches above mid-word matches");
Check("TextEditor", rankedSearchResults[3].Name, "match camel-case word boundaries");
Check("Documents", AppCatalogService.Search([new AppEntry("Documents", "documents.lnk", CategoryPath: "Creative Tools")], "creative tool").Single().Name, "search nested Start menu folder names");
var startPins = StartPinCatalog.Pin([], new AppEntry("Editor", @"C:\Apps\Editor.lnk", CategoryPath: "Tools"));
Check("Editor", startPins.Single().Name, "pin a Start menu shortcut to the Start favorites list");
Check(1, StartPinCatalog.Pin(startPins, new AppEntry("Editor", @"c:\apps\editor.LNK")).Count, "avoid duplicate Start pins regardless of path casing");
var packagedStartPin = new AppEntry("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IsPackagedApp: true);
Check("Calculator", StartPinCatalog.Pin(startPins, packagedStartPin).Last().Name, "pin a packaged Windows app to Start");
Check(0, StartPinCatalog.Unpin(startPins, @"C:\Apps\Editor.lnk").Count, "remove an app from Start favorites");
var orderedStartPins = StartPinCatalog.Pin(startPins, new AppEntry("Browser", @"C:\Apps\Browser.lnk"));
Check("Browser,Editor", string.Join(',', StartPinCatalog.Move(orderedStartPins, @"C:\Apps\Browser.lnk", -1).Select(app => app.Name)), "move a Start favorite earlier");
Check("Browser,Editor", string.Join(',', StartPinCatalog.Move(orderedStartPins, @"C:\Apps\Editor.lnk", 1).Select(app => app.Name)), "move a Start favorite later");
Check("Editor,Browser", string.Join(',', StartPinCatalog.Move(orderedStartPins, @"C:\Apps\Editor.lnk", -1).Select(app => app.Name)), "keep the first Start favorite in place at the list boundary");
Throws<ArgumentOutOfRangeException>(() => StartPinCatalog.Move(orderedStartPins, @"C:\Apps\Editor.lnk", 2), "reject multi-position Start favorite moves");
Check("Editor,Browser", string.Join(',', StartPinCatalog.Reorder(orderedStartPins, @"C:\Apps\Editor.lnk", 1).Select(app => app.Name)), "preserve a Start favorite dropped before its current next pin");
Check("Browser,Editor", string.Join(',', StartPinCatalog.Reorder(orderedStartPins, @"C:\Apps\Editor.lnk", 2).Select(app => app.Name)), "drag a Start favorite after the last pin");
Check("Browser,Editor", string.Join(',', StartPinCatalog.Reorder(orderedStartPins, @"C:\Apps\Browser.lnk", 0).Select(app => app.Name)), "drag a Start favorite before the first pin");
Check("Editor,Browser", string.Join(',', StartPinCatalog.Reorder(orderedStartPins, @"C:\Apps\Browser.lnk", 3).Select(app => app.Name)), "ignore an out-of-range Start favorite drop");
Check("Editor,Calculator,Browser", string.Join(',', StartPinCatalog.Reorder(StartPinCatalog.Pin(orderedStartPins, packagedStartPin), packagedStartPin.ShortcutPath, 1).Select(app => app.Name)), "insert a newly dragged app before a Start favorite");
Check("Editor,Browser", string.Join(',', StartPinCatalog.Reorder(orderedStartPins, @"C:\Apps\Missing.lnk", 0).Select(app => app.Name)), "ignore a Start favorite drop with an unknown source");
var explorerTabOrder = new List<string> { "Home", "Documents", "Downloads" };
CheckTrue(ExplorerTabOrdering.Move(explorerTabOrder, 0, 3), "move an Explorer tab after the final tab");
Check("Documents,Downloads,Home", string.Join(',', explorerTabOrder), "preserve Explorer tab order when dragging a tab to the end");
CheckTrue(ExplorerTabOrdering.Move(explorerTabOrder, 2, 0), "move an Explorer tab before the first tab");
Check("Home,Documents,Downloads", string.Join(',', explorerTabOrder), "preserve Explorer tab order when dragging a tab to the beginning");
CheckTrue(!ExplorerTabOrdering.Move(explorerTabOrder, 1, 2), "ignore an Explorer tab drop that keeps it in the same position");
CheckTrue(!ExplorerTabOrdering.Move(explorerTabOrder, -1, 0), "ignore an invalid Explorer tab drag source");
var closedExplorerTabs = new List<ExplorerTabState>();
var closedDocumentsTab = new ExplorerTabState(new ExplorerLocation(@"C:\Users\test\Documents"));
var closedDownloadsTab = new ExplorerTabState(new ExplorerLocation(@"C:\Users\test\Downloads"));
var closedPicturesTab = new ExplorerTabState(new ExplorerLocation(@"C:\Users\test\Pictures"));
ExplorerClosedTabHistory.Remember(closedExplorerTabs, closedDocumentsTab, maximum: 2);
ExplorerClosedTabHistory.Remember(closedExplorerTabs, closedDownloadsTab, maximum: 2);
ExplorerClosedTabHistory.Remember(closedExplorerTabs, closedPicturesTab, maximum: 2);
Check(@"C:\Users\test\Pictures", ExplorerClosedTabHistory.RestoreLast(closedExplorerTabs)?.Location.Path, "reopen Explorer tabs in last-closed order");
Check(@"C:\Users\test\Downloads", ExplorerClosedTabHistory.RestoreLast(closedExplorerTabs)?.Location.Path, "retain the next most recently closed Explorer tab");
Check(null, ExplorerClosedTabHistory.RestoreLast(closedExplorerTabs), "return no Explorer tab when the closed-tab history is empty");
Throws<ArgumentOutOfRangeException>(() => ExplorerClosedTabHistory.Remember(closedExplorerTabs, closedDocumentsTab, maximum: -1), "reject negative Explorer closed-tab history limits");
var fullStartPinList = Enumerable.Range(0, StartPinCatalog.MaximumPins)
    .Select(index => new AppEntry($"App {index}", $@"C:\Apps\app{index}.lnk"))
    .ToList();
Check(StartPinCatalog.MaximumPins, StartPinCatalog.Pin(fullStartPinList, new AppEntry("Extra", @"C:\Apps\extra.lnk")).Count, "respect the Start pin limit");
Check(1, StartPinCatalog.Pin([], new AppEntry("Editor", @"C:\Apps\Editor.exe")).Count, "pin executable targets to Start");
CheckTrue(new StartMenuNode("Editor", new AppEntry("Editor", @"C:\Apps\Editor.lnk")).CanPinApplication, "allow Start app-tree leaves to be pinned");
Check(false, new StartMenuNode("Tools").CanPinApplication, "keep Start app-tree folders from being pinned as apps");
var droppedStartPins = StartPinCatalog.AddDroppedFiles([], [@"C:\Apps\Editor.exe", @"C:\Apps\Editor.lnk", @"C:\Apps\Notes.txt", "relative.exe"]);
Check("Editor,Editor", string.Join(',', droppedStartPins.Select(app => app.Name)), "pin dropped executables and shortcuts while ignoring documents and relative paths");
Throws<ArgumentException>(() => StartPinCatalog.Pin([], new AppEntry("Unsupported", @"C:\Apps\unsupported.txt")), "reject unsupported Start pin targets");
Check("shell:MyComputerFolder", StartMenuPlaceCatalog.ResolveTarget("computer"), "open This PC from the Start places menu");
Check("control.exe", StartMenuPlaceCatalog.ResolveTarget("control-panel"), "open Control Panel from the Start places menu");
Check(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), StartMenuPlaceCatalog.ResolveTarget("music"), "open the user's Music folder from the Start places menu");
Check(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StartMenuPlaceCatalog.ResolveTarget("documents"), "resolve Documents from the Start dropdown places");
Check(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), StartMenuPlaceCatalog.ResolveTarget("downloads"), "resolve Downloads from the Start dropdown places");
Check(5, StartMenuPlaceCatalog.DropdownPlaces.Count, "show the supported Start places with dropdown navigation");
Check(5, StartMenuPlaceCatalog.AdditionalPlaces.Count, "show the remaining additional Start system places");
Check("Run...", StartMenuPlaceCatalog.AdditionalPlaces.Single(place => place.Id == "run").Label, "offer the classic Run dialog from the Start places menu");
Throws<ArgumentOutOfRangeException>(() => StartMenuPlaceCatalog.ResolveTarget("unknown"), "reject unknown Start system places");
Throws<ArgumentOutOfRangeException>(() => StartMenuPlaceCatalog.ReadChildren("missing", -1), "reject a negative Start place dropdown limit");
Check("lock,sleep,hibernate,sign-out,restart,shutdown", string.Join(',', StartPowerActionCatalog.Actions.Select(action => action.Id)), "offer common Start power actions");
Check(true, StartPowerActionCatalog.ById("shutdown").RequiresConfirmation, "confirm shutdown before execution");
Check(true, StartPowerActionCatalog.ById("restart").RequiresConfirmation, "confirm restart before execution");
Check(true, StartPowerActionCatalog.ById("sign-out").RequiresConfirmation, "confirm sign-out before execution");
Check(false, StartPowerActionCatalog.ById("lock").RequiresConfirmation, "allow the reversible lock action directly");
Throws<ArgumentOutOfRangeException>(() => StartPowerActionCatalog.ById("unknown"), "reject unknown power actions");
Check("search:query=quarter%20%26%20year", StartSearchTargetBuilder.WindowsSearch(" quarter & year "), "encode a query for Windows Search");
Check("https://www.bing.com/search?q=caf%C3%A9%20%2F%20tea", StartSearchTargetBuilder.WebSearch("café / tea"), "encode a query for explicit web search");
Throws<ArgumentException>(() => StartSearchTargetBuilder.WindowsSearch("  "), "reject empty Windows Search requests");
Throws<ArgumentException>(() => StartSearchTargetBuilder.WebSearch(string.Empty), "reject empty web search requests");
Check(ExplorerKeyboardAction.FocusAddress, ExplorerKeyboardPolicy.Resolve(Key.L, ModifierKeys.Control), "focus the Explorer address field with Ctrl+L");
Check(ExplorerKeyboardAction.FocusAddress, ExplorerKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, Key.D), "focus the Explorer address field with Alt+D system-key events");
Check(ExplorerKeyboardAction.FocusSearch, ExplorerKeyboardPolicy.Resolve(Key.F, ModifierKeys.Control), "focus Explorer search with Ctrl+F");
Check(ExplorerKeyboardAction.ReopenClosedTab, ExplorerKeyboardPolicy.Resolve(Key.T, ModifierKeys.Control | ModifierKeys.Shift), "reopen the last closed Explorer tab with Ctrl+Shift+T");
Check(ExplorerKeyboardAction.NextPane, ExplorerKeyboardPolicy.Resolve(Key.F6, ModifierKeys.None), "cycle Explorer navigation panes with F6");
Check(ExplorerKeyboardAction.PreviousPane, ExplorerKeyboardPolicy.Resolve(Key.F6, ModifierKeys.Shift), "cycle Explorer navigation panes in reverse with Shift+F6");
Check(ExplorerKeyboardAction.None, ExplorerKeyboardPolicy.Resolve(Key.L, ModifierKeys.Control | ModifierKeys.Shift), "leave modified Ctrl+L combinations untouched");
var folderCatalog = new[]
{
    new AppEntry("Word", @"C:\Apps\Word.lnk", CategoryPath: @"Office\Editors"),
    new AppEntry("Mail", @"C:\Apps\Mail.lnk", CategoryPath: "Office"),
    new AppEntry("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IsPackagedApp: true)
};
var menuTree = AppCatalogService.BuildTree(folderCatalog);
Check(2, menuTree.Count, "build Start menu root folders from nested shortcut categories");
Check("Word", menuTree.Single(node => node.Name == "Office").Children.Single(node => node.Name == "Editors").Children.Single().Name, "preserve nested program folder levels");
Check("Calculator", menuTree.Single(node => node.Name == "Windows apps").Children.Single().Name, "group packaged apps under Windows apps");
var packagedApp = new AppEntry("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IsPackagedApp: true);
Check("Windows app", packagedApp.SourceDescription, "label packaged apps without exposing the application ID");
var discoveredPackagedApps = new AppCatalogService().FindStartMenuApps().Where(entry => entry.IsPackagedApp).ToList();
CheckTrue(discoveredPackagedApps.Count > 0, "discover installed packaged apps from the Windows AppsFolder namespace");
CheckTrue(discoveredPackagedApps.All(entry => entry.ShortcutPath.Contains('!')), "retain application IDs for packaged app activation");
var fullPinList = Enumerable.Range(0, TaskbarPinCatalog.MaximumPins)
    .Select(index => new PinnedTaskbarApp($"App {index}", $@"C:\Apps\app{index}.exe"))
    .ToList();
Check(TaskbarPinCatalog.MaximumPins, TaskbarPinCatalog.AddDroppedFiles(fullPinList, [@"C:\Apps\extra.exe"]).Count, "respect taskbar pin limit for dropped files");
var tapGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, tapGesture.KeyDown(0x5b), "capture a bare left Windows key press");
Check(WindowsKeyAction.Suppress, tapGesture.KeyDown(0x5b), "suppress Windows-key repeat events");
Check(WindowsKeyAction.OpenStartMenu, tapGesture.KeyUp(0x5b), "open Start after a bare Windows key press");
Check<uint?>(null, tapGesture.HeldWindowsKey, "clear Windows-key state after opening Start");
Check(1, TaskbarShortcutCatalog.GetOneBasedPinIndex((uint)'1'), "map Win+1 to the first taskbar pin");
Check(9, TaskbarShortcutCatalog.GetOneBasedPinIndex(0x69), "map the numpad 9 key to the ninth taskbar pin");
Check<int?>(null, TaskbarShortcutCatalog.GetOneBasedPinIndex((uint)'R'), "leave ordinary Windows-key shortcuts unmapped");
var pinnedTaskbarGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, pinnedTaskbarGesture.KeyDown(0x5b), "capture Windows while custom taskbar shortcuts are enabled");
Check(WindowsKeyAction.ActivateTaskbarPin, pinnedTaskbarGesture.KeyDown((uint)'3', _ => true), "route Win+3 to the third custom taskbar pin");
Check(3, pinnedTaskbarGesture.TaskbarPinIndex, "retain the selected taskbar pin index for activation");
Check(WindowsKeyAction.Suppress, pinnedTaskbarGesture.KeyDown((uint)'3', _ => true), "suppress repeat events for a held taskbar shortcut");
Check(WindowsKeyAction.Suppress, pinnedTaskbarGesture.KeyUp((uint)'3'), "suppress the custom taskbar shortcut key release");
Check(WindowsKeyAction.Suppress, pinnedTaskbarGesture.KeyUp(0x5b), "avoid opening Start after a custom taskbar shortcut");
Check<int?>(null, pinnedTaskbarGesture.TaskbarPinIndex, "clear custom taskbar shortcut state after Windows-key release");
var taskbarFocusGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyDown(0x5b), "capture Windows before focusing the custom taskbar");
Check(WindowsKeyAction.FocusTaskbar, taskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => true), "route Win+T to the custom taskbar when it is available");
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => true), "suppress Win+T repeat events while the shortcut is held");
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyUp((uint)'T'), "suppress the custom taskbar focus shortcut release");
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyUp(0x5b), "avoid opening Start after focusing the custom taskbar");
var nativeTaskbarFocusGesture = new WindowsKeyGesture();
nativeTaskbarFocusGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeTaskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => false), "preserve native Win+T when the custom taskbar is unavailable");
Check(WindowsKeyAction.PassThrough, nativeTaskbarFocusGesture.KeyUp((uint)'T'), "pass through native taskbar focus key release");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeTaskbarFocusGesture.KeyUp(0x5b), "release native Win+T modifier state");
Check<int?>(0, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(-1, 4, forward: true), "start Win+T keyboard navigation at the first taskbar button");
Check<int?>(3, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(-1, 4, forward: false), "start reverse taskbar keyboard navigation at the last button");
Check<int?>(0, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(3, 4, forward: true), "wrap forward taskbar keyboard navigation");
Check<int?>(3, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(0, 4, forward: false), "wrap reverse taskbar keyboard navigation");
Check<int?>(null, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(-1, 0, forward: true), "ignore keyboard navigation when the taskbar has no buttons");
var shortcutGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, shortcutGesture.KeyDown(0x5c), "capture a right Windows key press");
Check(WindowsKeyAction.ForwardWindowsDownThenPass, shortcutGesture.KeyDown('R'), "forward the modifier for Win+R");
Check((uint?)0x5c, shortcutGesture.HeldWindowsKey, "preserve right Windows key identity");
Check(WindowsKeyAction.PassThrough, shortcutGesture.KeyUp('R'), "pass shortcut key release");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, shortcutGesture.KeyUp(0x5c), "release the forwarded Windows key");
Check<uint?>(null, shortcutGesture.HeldWindowsKey, "clear Windows-key state after a shortcut");
var unpinnedShortcutGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, unpinnedShortcutGesture.KeyDown(0x5b), "capture a Windows-key press before checking for custom pins");
Check(WindowsKeyAction.ForwardWindowsDownThenPass, unpinnedShortcutGesture.KeyDown((uint)'2', _ => false), "forward Win+2 when no custom taskbar pin can handle it");
Check(WindowsKeyAction.PassThrough, unpinnedShortcutGesture.KeyUp((uint)'2'), "pass through the release of an unhandled taskbar shortcut");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, unpinnedShortcutGesture.KeyUp(0x5b), "release the native Windows shortcut modifier when no custom pin is available");
var cancelGesture = new WindowsKeyGesture();
cancelGesture.KeyDown(0x5b);
cancelGesture.KeyDown('E');
Check((uint?)0x5b, cancelGesture.Cancel(), "release a forwarded modifier when disabling the hook");
Check<uint?>(null, cancelGesture.Cancel(), "cancel clears gesture state");
CheckTrue(SystemFlyoutService.GetNotificationCenterSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'N', false),
    new KeyboardKeyEvent((ushort)'N', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+N notification-center shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetQuickSettingsSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'A', false),
    new KeyboardKeyEvent((ushort)'A', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+A Quick Settings shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetNotificationAreaSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'B', false),
    new KeyboardKeyEvent((ushort)'B', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+B notification-area shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetWidgetsSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'W', false),
    new KeyboardKeyEvent((ushort)'W', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+W Widgets shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetRunDialogSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'R', false),
    new KeyboardKeyEvent((ushort)'R', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+R Run dialog shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetWindowsSearchSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'S', false),
    new KeyboardKeyEvent((ushort)'S', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+S search shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetTaskViewSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent(0x09, false),
    new KeyboardKeyEvent(0x09, true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+Tab Task View shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetShowDesktopSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'D', false),
    new KeyboardKeyEvent((ushort)'D', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+D Show desktop shortcut in balanced key order");
var appHostStartup = StartupShortcutService.BuildCommand(
    @"C:\Program Files\Desktop Tuner\DesktopTuner.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll");
Check(@"C:\Program Files\Desktop Tuner\DesktopTuner.exe", appHostStartup.TargetPath, "launch the packaged app host at sign-in");
Check("--startup", appHostStartup.Arguments, "start the taskbar in background mode at sign-in");
var dotnetStartup = StartupShortcutService.BuildCommand(
    @"C:\Program Files\dotnet\dotnet.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll");
Check(@"C:\Program Files\dotnet\dotnet.exe", dotnetStartup.TargetPath, "support development launches through dotnet at sign-in");
Check("\"C:\\Program Files\\Desktop Tuner\\DesktopTuner.dll\" --startup", dotnetStartup.Arguments, "quote an assembly path with spaces in the Startup shortcut");
Throws<ArgumentOutOfRangeException>(() => TaskbarLayoutCalculator.Calculate(0, 1080, new(TaskbarEdge.Bottom), false), "rejects invalid screen bounds");
var appModeSetting = SettingsCatalog.ById("explorer-app-mode");
var systemModeSetting = SettingsCatalog.ById("explorer-system-mode");
var transparencySetting = SettingsCatalog.ById("explorer-transparency");
var fullPathSetting = SettingsCatalog.ById("explorer-full-path");
var separateExplorerProcessesSetting = SettingsCatalog.ById("explorer-separate-process");
var alignmentSetting = SettingsCatalog.ById("taskbar-alignment");
Check(SettingsCatalog.Personalize, appModeSetting.RegistryPath, "use shared Windows personalization registry location for app color mode");
Check("AppsUseLightTheme", appModeSetting.ValueName, "target Windows app color mode value");
Check("SystemUsesLightTheme", systemModeSetting.ValueName, "target Windows system color mode value");
Check("#1B2434", TaskbarTheme.Resolve(dark: false).Foreground, "use dark taskbar text in Windows light system mode");
Check("#F4F6FA", TaskbarTheme.Resolve(dark: true).Foreground, "use light taskbar text in Windows dark system mode");
Check((byte)77, ((SolidColorBrush)TaskbarTheme.CreateBackground(dark: false, 70)).Color.A, "apply the selected transparency to the light taskbar surface");
Check("EnableTransparency", transparencySetting.ValueName, "target Windows transparency setting");
Check(SettingsCatalog.ExplorerCabinetState, fullPathSetting.RegistryPath, "use Windows Explorer's cabinet-state registry location");
Check("FullPath", fullPathSetting.ValueName, "target the documented full-path title-bar preference");
Check(1, fullPathSetting.Choices.Single(choice => choice.Label == "Show full path").Value, "map full-path title bars to the Explorer option");
Check("SeparateProcess", separateExplorerProcessesSetting.ValueName, "target separate Explorer folder processes");
Check(1, separateExplorerProcessesSetting.Choices.Single(choice => choice.Label == "Enabled").Value, "map process isolation to the Explorer option");
Check(0, appModeSetting.Choices.Single(choice => choice.Label == "Dark").Value, "map dark app mode to the Windows registry value");
Check(1, transparencySetting.Choices.Single(choice => choice.Label == "On").Value, "map enabled transparency to the Windows registry value");
Check((int)TaskbarButtonAlignment.Left, alignmentSetting.Choices.Single(choice => choice.Label == "Left").Value, "map left taskbar alignment to the overlay setting");
Check((int)TaskbarButtonAlignment.Center, alignmentSetting.Choices.Single(choice => choice.Label == "Center").Value, "map centered taskbar alignment to the overlay setting");
var temporaryPreferencesDirectory = Path.Combine(Path.GetTempPath(), $"DesktopTuner.Tests-{Guid.NewGuid():N}");
var preferencesPath = Path.Combine(temporaryPreferencesDirectory, "preferences.json");
try
{
    var explorerTestDirectory = Path.Combine(temporaryPreferencesDirectory, "ExplorerOperations");
    Directory.CreateDirectory(explorerTestDirectory);
    var propertiesTestFile = Path.Combine(explorerTestDirectory, "Details.txt");
    File.WriteAllText(propertiesTestFile, "properties fixture");
    var propertiesFileEntry = new ExplorerEntry("Details.txt", propertiesTestFile, false, false, 18, DateTime.Now);
    var propertiesDirectoryEntry = new ExplorerEntry("ExplorerOperations", explorerTestDirectory, true, false, null, DateTime.Now);
    CheckTrue(ExplorerPropertiesService.CanShowProperties(propertiesFileEntry), "offer the native Properties sheet for an existing file");
    CheckTrue(ExplorerPropertiesService.CanShowProperties(propertiesDirectoryEntry), "offer the native Properties sheet for an existing folder");
    CheckTrue(!ExplorerPropertiesService.CanShowProperties(new ExplorerEntry("C:\\", "C:\\", true, true, null, DateTime.Now)), "keep drive-list entries out of file Properties selection");
    CheckTrue(!ExplorerPropertiesService.CanShowProperties(new ExplorerEntry("Missing", Path.Combine(explorerTestDirectory, "missing.txt"), false, false, null, DateTime.Now)), "disable Properties for a removed item");
    var startShortcutDirectory = Path.Combine(temporaryPreferencesDirectory, "Start Shortcuts");
    Directory.CreateDirectory(startShortcutDirectory);
    var startShortcutPath = Path.Combine(startShortcutDirectory, "Editor Preview.lnk");
    File.WriteAllText(startShortcutPath, "test shortcut fixture");
    var startShortcut = new AppEntry("Editor", startShortcutPath);
    CheckTrue(startShortcut.CanOpenFileLocation, "offer file location for an existing Start shortcut");
    var fileLocationLaunchInfo = AppCatalogService.BuildFileLocationLaunchInfo(startShortcut);
    Check("explorer.exe", fileLocationLaunchInfo.FileName, "open shortcut locations in File Explorer");
    Check($"/select,\"{startShortcutPath}\"", fileLocationLaunchInfo.ArgumentList.Single(), "select the original shortcut path including spaces");
    CheckTrue(fileLocationLaunchInfo.UseShellExecute, "open shortcut locations through the Windows shell");
    CheckTrue(!AppCatalogService.CanOpenFileLocation(new AppEntry("Missing", Path.Combine(startShortcutDirectory, "missing.lnk"))), "disable file location for a removed Start shortcut");
    CheckTrue(!AppCatalogService.CanOpenFileLocation(new AppEntry("Calculator", "CalculatorApp!App", IsPackagedApp: true)), "do not offer a file location for packaged Windows apps");
    Throws<NotSupportedException>(() => AppCatalogService.BuildFileLocationLaunchInfo(new AppEntry("Missing", Path.Combine(startShortcutDirectory, "missing.lnk"))), "reject file location for a removed Start shortcut");
    var pinnedStartShortcut = new PinnedTaskbarApp("Editor", startShortcutPath);
    CheckTrue(pinnedStartShortcut.CanOpenLocation, "offer file location for an existing taskbar app pin");
    Check($"/select,\"{startShortcutPath}\"", TaskbarPinCatalog.BuildLocationLaunchInfo(pinnedStartShortcut).ArgumentList.Single(), "select the pinned app's executable or shortcut in Explorer");
    var pinnedFolder = new PinnedTaskbarApp("ExplorerOperations", explorerTestDirectory, IsDirectory: true);
    CheckTrue(pinnedFolder.CanOpenLocation, "offer the folder itself for an existing taskbar folder pin");
    Check(explorerTestDirectory, TaskbarPinCatalog.BuildLocationLaunchInfo(pinnedFolder).FileName, "open a pinned folder directly in File Explorer");
    var pinnedFolderMenuDirectory = Path.Combine(temporaryPreferencesDirectory, "PinnedFolderMenu");
    var pinnedFolderMenuSubdirectory = Path.Combine(pinnedFolderMenuDirectory, "AlphaFolder");
    Directory.CreateDirectory(pinnedFolderMenuSubdirectory);
    File.WriteAllText(Path.Combine(pinnedFolderMenuSubdirectory, "Nested.txt"), "nested fixture");
    File.WriteAllText(Path.Combine(pinnedFolderMenuDirectory, "Zebra.txt"), "file fixture");
    for (var index = 0; index < TaskbarFolderMenuCatalog.MaximumVisibleEntries + 5; index++)
        File.WriteAllText(Path.Combine(pinnedFolderMenuDirectory, $"File {index:D2}.txt"), "bounded menu fixture");
    var hiddenPinnedFolderItem = Path.Combine(pinnedFolderMenuDirectory, "Hidden.txt");
    File.WriteAllText(hiddenPinnedFolderItem, "hidden fixture");
    File.SetAttributes(hiddenPinnedFolderItem, FileAttributes.Hidden);
    var pinnedFolderMenuEntries = TaskbarFolderMenuCatalog.ReadChildren(pinnedFolderMenuDirectory);
    Check("AlphaFolder", pinnedFolderMenuEntries[0].Name, "list folders before files in pinned taskbar folder menus");
    CheckTrue(pinnedFolderMenuEntries[0].IsDirectory, "identify folders in pinned taskbar folder menus");
    Check(TaskbarFolderMenuCatalog.MaximumVisibleEntries, pinnedFolderMenuEntries.Count, "bound the default number of visible pinned folder menu entries");
    Check(false, pinnedFolderMenuEntries.Any(entry => entry.Name == "Hidden.txt"), "hide hidden files from pinned folder menus");
    Check(1, TaskbarFolderMenuCatalog.ReadChildren(pinnedFolderMenuDirectory, maximum: 1).Count, "honor a smaller pinned folder menu entry bound");
    Check(0, TaskbarFolderMenuCatalog.ReadChildren(pinnedFolderMenuDirectory, maximum: 0).Count, "allow pinned folder menus to request no entries");
    Check("Nested.txt", TaskbarFolderMenuCatalog.ReadChildren(pinnedFolderMenuSubdirectory).Single().Name, "enumerate nested pinned folder menu contents on demand");
    Check(0, TaskbarFolderMenuCatalog.ReadChildren(Path.Combine(temporaryPreferencesDirectory, "MissingFolderMenu")).Count, "return no entries for a missing pinned folder");
    Throws<ArgumentOutOfRangeException>(() => TaskbarFolderMenuCatalog.ReadChildren(pinnedFolderMenuDirectory, maximum: -1), "reject negative pinned folder menu bounds");
    CheckTrue(!TaskbarPinCatalog.CanOpenLocation(new PinnedTaskbarApp("Missing", Path.Combine(startShortcutDirectory, "missing.exe"))), "disable file location for a removed taskbar app pin");
    var quickAccessStore = new ExplorerQuickAccessStore(Path.Combine(temporaryPreferencesDirectory, "explorer-quick-access.json"));
    Check(true, quickAccessStore.Add(explorerTestDirectory), "pin an existing Explorer folder to quick access");
    Check(false, quickAccessStore.Add(explorerTestDirectory.ToUpperInvariant()), "avoid duplicate quick access pins without regard to path casing");
    Check(Path.GetFullPath(explorerTestDirectory), quickAccessStore.Load().Single().Path, "persist a quick access folder path");
    Check("ExplorerOperations", quickAccessStore.Load().Single().Name, "derive the quick access label from its folder name");
    var secondQuickAccessFolder = Path.Combine(temporaryPreferencesDirectory, "SecondQuickAccess");
    Directory.CreateDirectory(secondQuickAccessFolder);
    Check(true, quickAccessStore.Add(secondQuickAccessFolder), "pin a second folder for quick access ordering");
    Check(true, quickAccessStore.Move(secondQuickAccessFolder, 0), "reorder a quick access folder before the first pin");
    Check("SecondQuickAccess,ExplorerOperations", string.Join(',', quickAccessStore.Load().Select(pin => pin.Name)), "persist the user-selected quick access order");
    Check(true, quickAccessStore.Remove(explorerTestDirectory), "remove a folder from quick access");
    Check("SecondQuickAccess", string.Join(',', quickAccessStore.Load().Select(pin => pin.Name)), "persist quick access removals without disturbing other pins");
    var folderViewStore = new ExplorerFolderViewStore(Path.Combine(temporaryPreferencesDirectory, "explorer-folder-views.json"));
    var folderViewPreference = new ExplorerFolderViewPreference(ExplorerViewMode.LargeIcons, ExplorerSortColumn.DateModified, false);
    Check(true, folderViewStore.Save(explorerTestDirectory, folderViewPreference), "save a folder's Explorer view preferences");
    Check(folderViewPreference, folderViewStore.Load(explorerTestDirectory), "restore saved Explorer view preferences");
    Check(folderViewPreference, folderViewStore.Load(explorerTestDirectory.ToUpperInvariant()), "match saved Explorer view preferences regardless of path casing");
    Check(false, folderViewStore.Save("relative-folder", folderViewPreference), "reject relative paths for saved Explorer folder views");
    var corruptFolderViewPath = Path.Combine(temporaryPreferencesDirectory, "corrupt-folder-views.json");
    File.WriteAllText(corruptFolderViewPath, "invalid-json");
    Check(null, new ExplorerFolderViewStore(corruptFolderViewPath).Load(explorerTestDirectory), "recover from corrupt saved Explorer folder views");
    Check(0, ExplorerQuickAccessCatalog.Normalize([
        new ExplorerQuickAccessPin("relative", "relative-folder")
    ]).Count, "reject relative paths from imported quick access pins");
    var maximumQuickAccessPins = ExplorerQuickAccessCatalog.Normalize(Enumerable.Range(0, ExplorerQuickAccessCatalog.MaximumPins + 1)
        .Select(index => new ExplorerQuickAccessPin($"Folder {index}", $"C:\\Pinned\\Folder {index}")));
    Check(ExplorerQuickAccessCatalog.MaximumPins, maximumQuickAccessPins.Count, "bound imported quick access pins");
    List<ExplorerQuickAccessPin> reorderableQuickAccessPins =
    [
        new("First", @"C:\Pinned\First"),
        new("Second", @"C:\Pinned\Second"),
        new("Third", @"C:\Pinned\Third")
    ];
    CheckTrue(ExplorerQuickAccessCatalog.Move(reorderableQuickAccessPins, @"C:\Pinned\Third", 0), "move a quick access folder before the list");
    Check("Third,First,Second", string.Join(',', reorderableQuickAccessPins.Select(pin => pin.Name)), "retain quick access order after moving an item to the front");
    CheckTrue(!ExplorerQuickAccessCatalog.Move(reorderableQuickAccessPins, @"C:\Pinned\Missing", 0), "ignore reorder requests for an unknown quick access pin");
    var droppableQuickAccessFolders = ExplorerQuickAccessCatalog.GetDroppableFolders(
        [@"C:\Folders\Projects", @"C:\Folders\Projects", @"C:\Folders\readme.txt", "relative-folder"],
        path => string.Equals(path, @"C:\Folders\Projects", StringComparison.OrdinalIgnoreCase));
    Check(1, droppableQuickAccessFolders.Count, "accept only distinct absolute folders from drag-and-drop paths");
    var startPlaceTestDirectory = Path.Combine(temporaryPreferencesDirectory, "StartPlaceFlyout");
    Directory.CreateDirectory(startPlaceTestDirectory);
    Directory.CreateDirectory(Path.Combine(startPlaceTestDirectory, "Folder"));
    var startPlaceFile = Path.Combine(startPlaceTestDirectory, "Notes.txt");
    var hiddenStartPlaceFile = Path.Combine(startPlaceTestDirectory, "Hidden.txt");
    File.WriteAllText(startPlaceFile, "notes");
    File.WriteAllText(hiddenStartPlaceFile, "hidden");
    File.SetAttributes(hiddenStartPlaceFile, File.GetAttributes(hiddenStartPlaceFile) | FileAttributes.Hidden);
    var startPlaceEntries = StartMenuPlaceCatalog.ReadChildren(startPlaceTestDirectory);
    Check("Folder", startPlaceEntries[0].Name, "show folders first in a Start place dropdown");
    Check("Folder,Notes.txt", string.Join(',', startPlaceEntries.Select(entry => entry.Name)), "hide hidden items from a Start place dropdown");
    Check(1, StartMenuPlaceCatalog.ReadChildren(startPlaceTestDirectory, 1).Count, "bound the number of entries shown in a Start place dropdown");
    Check(0, StartMenuPlaceCatalog.ReadChildren(Path.Combine(temporaryPreferencesDirectory, "missing-place")).Count, "return no Start place entries for a missing directory");
    var linkedExecutablePath = Environment.ProcessPath!;
    var taskbarShortcutPath = Path.Combine(explorerTestDirectory, "Test application.lnk");
    object? shortcutShellObject = null;
    object? shortcutObject = null;
    try
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable for shortcut integration tests.");
        shortcutShellObject = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows Script Host automation object.");
        dynamic shortcutShell = shortcutShellObject;
        shortcutObject = shortcutShell.CreateShortcut(taskbarShortcutPath);
        dynamic testShortcut = shortcutObject;
        testShortcut.TargetPath = linkedExecutablePath;
        testShortcut.Save();
    }
    finally
    {
        if (shortcutObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcutObject)) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcutObject);
        if (shortcutShellObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcutShellObject)) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcutShellObject);
    }
    var linkedExecutableWindow = new RunningWindow((nint)90, "Test process", "Test app", linkedExecutablePath, false);
    CheckTrue(TaskbarPinIdentityService.Matches(new PinnedTaskbarApp("Test application", taskbarShortcutPath), linkedExecutableWindow), "resolve a Windows shortcut target when matching a running taskbar app");
    var firstCreatedFolder = ExplorerFileOperationService.CreateFolder(explorerTestDirectory);
    var secondCreatedFolder = ExplorerFileOperationService.CreateFolder(explorerTestDirectory);
    Check("New folder", Path.GetFileName(firstCreatedFolder), "create a new folder using the familiar default name");
    Check("New folder (2)", Path.GetFileName(secondCreatedFolder), "avoid overwriting an existing folder when creating another");
    var sourceFile = Path.Combine(explorerTestDirectory, "draft.txt");
    File.WriteAllText(sourceFile, "draft");
    CheckTrue(new ExplorerEntry("draft.txt", sourceFile, false, false, new FileInfo(sourceFile).Length, File.GetLastWriteTime(sourceFile)).Icon is not null, "expose shell file icons to Explorer rows");
    CheckTrue(new ExplorerEntry("ExplorerOperations", explorerTestDirectory, true, false, null, Directory.GetLastWriteTime(explorerTestDirectory)).Icon is not null, "expose shell folder icons to Explorer rows");
    var renamedFile = ExplorerFileOperationService.Rename(sourceFile, "notes.txt");
    Check(true, File.Exists(renamedFile), "rename a file within its current folder");
    Throws<IOException>(() => ExplorerFileOperationService.Rename(renamedFile, "New folder"), "reject a rename that would replace a folder");
    Throws<ArgumentException>(() => ExplorerFileOperationService.ValidateName("invalid/name"), "reject file names containing reserved characters");
    var transferSource = Path.Combine(explorerTestDirectory, "TransferSource");
    var transferDestination = Path.Combine(explorerTestDirectory, "TransferDestination");
    var transferMoveDestination = Path.Combine(explorerTestDirectory, "TransferMoveDestination");
    Directory.CreateDirectory(transferSource);
    Directory.CreateDirectory(transferDestination);
    Directory.CreateDirectory(transferMoveDestination);
    var transferSourceFile = Path.Combine(transferSource, "clipboard.txt");
    File.WriteAllText(transferSourceFile, "clipboard data");
    var copiedFile = ExplorerFileOperationService.Transfer([transferSourceFile, transferSourceFile], transferDestination, move: false).Single();
    Check("clipboard data", File.ReadAllText(copiedFile), "copy a file into the destination folder");
    Check(true, File.Exists(transferSourceFile), "retain a source file after copy");
    var movedFile = ExplorerFileOperationService.Transfer([copiedFile], transferMoveDestination, move: true).Single();
    Check("clipboard data", File.ReadAllText(movedFile), "move a file into the destination folder");
    Check(false, File.Exists(copiedFile), "remove a source file after move");
    Check(true, ExplorerDragDropPolicy.ResolveMove([transferSourceFile], transferDestination, controlPressed: false, shiftPressed: false), "move dragged items by default when source and destination share a volume");
    Check(false, ExplorerDragDropPolicy.ResolveMove([transferSourceFile], transferDestination, controlPressed: true, shiftPressed: false), "copy dragged items when Control is pressed");
    Check(true, ExplorerDragDropPolicy.ResolveMove([transferSourceFile], transferDestination, controlPressed: false, shiftPressed: true), "move dragged items when Shift is pressed");
    Check(false, ExplorerDragDropPolicy.ResolveMove([@"C:\source.txt"], @"D:\destination", controlPressed: false, shiftPressed: false), "copy dragged items by default across volumes");
    var nestedTransferFolder = Path.Combine(transferSource, "Nested");
    Directory.CreateDirectory(nestedTransferFolder);
    File.WriteAllText(Path.Combine(nestedTransferFolder, "nested.txt"), "nested data");
    var copiedFolder = ExplorerFileOperationService.Transfer([transferSource], transferDestination, move: false).Single();
    Check("nested data", File.ReadAllText(Path.Combine(copiedFolder, "Nested", "nested.txt")), "copy a folder tree recursively");
    Throws<IOException>(() => ExplorerFileOperationService.Transfer([transferSource], nestedTransferFolder, move: false), "reject copying a folder into its own subtree");
    Throws<IOException>(() => ExplorerFileOperationService.Transfer([transferSource], transferDestination, move: false), "reject paste collisions without overwriting destination data");
    Check(0, ExplorerFileOperationService.Transfer([copiedFolder], transferDestination, move: false).Count, "treat pasting an item into its current folder as a no-op");
    var nestedExplorerFolder = Path.Combine(firstCreatedFolder, "Nested");
    Directory.CreateDirectory(nestedExplorerFolder);
    var nestedMatchPath = Path.Combine(nestedExplorerFolder, "meeting-notes.txt");
    File.WriteAllText(nestedMatchPath, "notes");
    var searchablePdf = Path.Combine(explorerTestDirectory, "final invoice.pdf");
    File.WriteAllText(searchablePdf, "invoice data");
    var archiveFolder = Path.Combine(explorerTestDirectory, "Archive");
    Directory.CreateDirectory(archiveFolder);
    var recursiveSearchResults = ExplorerSearchService.SearchAsync(explorerTestDirectory, "NOTES").GetAwaiter().GetResult();
    Check(2, recursiveSearchResults.Entries.Count, "search case-insensitively through nested folders");
    Check(true, recursiveSearchResults.Entries.Any(entry => entry.FullPath == nestedMatchPath), "return full paths for nested search results");
    var wideSearchDirectory = Path.Combine(explorerTestDirectory, "Wide");
    Directory.CreateDirectory(wideSearchDirectory);
    for (var index = 0; index < 128; index++)
        File.WriteAllText(Path.Combine(wideSearchDirectory, $"stream-{index:D3}.txt"), string.Empty);
    Check(128, ExplorerSearchService.SearchAsync(explorerTestDirectory, "stream- ext:txt").GetAwaiter().GetResult().Entries.Count,
        "enumerate every match in a wide folder while streaming directory entries");
    using (var canceledSearch = new CancellationTokenSource())
    {
        canceledSearch.Cancel();
        Throws<OperationCanceledException>(() => ExplorerSearchService.SearchAsync(explorerTestDirectory, "*.txt", canceledSearch.Token).GetAwaiter().GetResult(),
            "cancel Explorer search before reading directory entries");
    }
    Check(searchablePdf, ExplorerSearchService.SearchAsync(explorerTestDirectory, "*.pdf").GetAwaiter().GetResult().Entries.Single().FullPath, "match file names with wildcard patterns");
    Check(searchablePdf, ExplorerSearchService.SearchAsync(explorerTestDirectory, "final invoice ext:pdf kind:document").GetAwaiter().GetResult().Entries.Single().FullPath, "combine name terms with extension and document-kind filters");
    Check(searchablePdf, ExplorerSearchService.SearchAsync(explorerTestDirectory, "name:\"final invoice.pdf\"").GetAwaiter().GetResult().Entries.Single().FullPath, "keep quoted file-name phrases together in a search");
    var boundedSearch = ExplorerSearchQuery.Parse("after:2024-01-10 before:2024-01-20 size:>=1KB ext:pdf kind:document");
    var inRangePdf = new ExplorerEntry("report.pdf", searchablePdf, false, false, 1024, new DateTime(2024, 1, 15));
    CheckTrue(boundedSearch.Matches(inRangePdf), "combine modified-date, size, extension, and file-kind search filters");
    CheckTrue(!boundedSearch.Matches(inRangePdf with { Length = 1023 }), "apply binary size thresholds to Explorer search results");
    CheckTrue(!boundedSearch.Matches(inRangePdf with { Modified = new DateTime(2024, 1, 9) }), "exclude search results modified before the inclusive date range");
    CheckTrue(!boundedSearch.Matches(inRangePdf with { Modified = new DateTime(2024, 1, 21) }), "exclude search results modified after the inclusive date range");
    CheckTrue(!boundedSearch.Matches(inRangePdf with { IsDirectory = true }), "do not match folders against file-size filters");
    CheckTrue(ExplorerSearchQuery.Parse("size:=1KB").Matches(inRangePdf), "support exact file-size search filters");
    CheckTrue(ExplorerSearchQuery.Parse("size:1.5KB").Matches(inRangePdf with { Length = 1536 }), "parse fractional binary file-size filters");
    CheckTrue(ExplorerSearchQuery.Parse("size:<2KB").Matches(inRangePdf), "support less-than file-size search filters");
    Throws<ArgumentException>(() => ExplorerSearchQuery.Parse("after:2024-13-01"), "explain malformed modified-date search filters");
    Throws<ArgumentException>(() => ExplorerSearchQuery.Parse("size:large"), "explain malformed file-size search filters");
    Check(true, ExplorerSearchService.SearchAsync(explorerTestDirectory, "kind:folder").GetAwaiter().GetResult().Entries.Any(entry => entry.FullPath == archiveFolder), "search for folders with a kind filter");
    Check(0, ExplorerSearchService.SearchAsync(explorerTestDirectory, "ext:pdf kind:folder").GetAwaiter().GetResult().Entries.Count, "apply file extension filters only to files");
    var hiddenMatchPath = Path.Combine(explorerTestDirectory, "classified-notes.txt");
    File.WriteAllText(hiddenMatchPath, "hidden");
    File.SetAttributes(hiddenMatchPath, File.GetAttributes(hiddenMatchPath) | FileAttributes.Hidden);
    Check(0, ExplorerSearchService.SearchAsync(explorerTestDirectory, "classified").GetAwaiter().GetResult().Entries.Count, "respect hidden-item preferences during search");
    Check(1, ExplorerSearchService.SearchAsync(explorerTestDirectory, "classified", showHiddenItems: true).GetAwaiter().GetResult().Entries.Count, "include hidden items when the preference allows them");
    Check("report", new ExplorerEntry("report.txt", Path.Combine(explorerTestDirectory, "report.txt"), false, false, 0, DateTime.MinValue).GetDisplayName(true), "hide only the displayed extension while retaining the full file name");
    var recentExplorerFiles = ExplorerHomeService.SelectRecentFiles(
    [
        new ExplorerEntry("older.txt", @"C:\items\older.txt", false, false, 10, new DateTime(2024, 1, 3)) { RecentAccessed = new DateTime(2024, 1, 1) },
        new ExplorerEntry("newer.txt", @"C:\items\newer.txt", false, false, 20, new DateTime(2024, 1, 1)) { RecentAccessed = new DateTime(2024, 1, 3) },
        new ExplorerEntry("folder", @"C:\items\folder", true, false, null, new DateTime(2024, 1, 4)),
        new ExplorerEntry("hidden.txt", @"C:\items\hidden.txt", false, false, 5, new DateTime(2024, 1, 5)) { IsHidden = true },
        new ExplorerEntry("duplicate.txt", @"C:\items\newer.txt", false, false, 20, new DateTime(2024, 1, 1)) { RecentAccessed = new DateTime(2024, 1, 3) }
    ], showHiddenItems: false, maximum: 2);
    Check("newer.txt,older.txt", string.Join(',', recentExplorerFiles.Select(entry => entry.Name)), "show deduplicated recent files newest-first and exclude folders and hidden entries");
    Check(0, ExplorerHomeService.SelectHomeFiles(recentExplorerFiles, showRecentItems: false, showHiddenItems: false).Count, "respect the Windows recent-items privacy setting in Explorer Home");
    Check(2, ExplorerHomeService.SelectRecentFiles(
    [
        new ExplorerEntry("visible.txt", @"C:\items\visible.txt", false, false, 5, new DateTime(2024, 1, 1)),
        new ExplorerEntry("hidden.txt", @"C:\items\hidden.txt", false, false, 5, new DateTime(2024, 1, 2)) { IsHidden = true }
    ], showHiddenItems: true).Count, "honor the hidden-item setting in Explorer Home recent files");
    var sharedModifiedDate = new DateTime(2025, 2, 3, 16, 30, 0);
    var selectedFiles = new[]
    {
        new ExplorerEntry("first.txt", @"C:\Docs\first.txt", false, false, 5, sharedModifiedDate),
        new ExplorerEntry("second.txt", @"C:\Docs\second.txt", false, false, 7, sharedModifiedDate)
    };
    var fileSelectionSummary = ExplorerSelectionSummaryService.Resolve(selectedFiles);
    Check("2 items selected", fileSelectionSummary.Name, "summarize a multi-file Explorer selection");
    Check("2 files", fileSelectionSummary.Type, "show selected file counts in Explorer details");
    Check(@"C:\Docs", fileSelectionSummary.Location, "show the common parent for a multi-file Explorer selection");
    Check("12 B", fileSelectionSummary.Size, "sum known file sizes in Explorer details");
    Check(sharedModifiedDate.ToString("f"), fileSelectionSummary.Modified, "show a shared modified date for selected files");
    var mixedSelectionSummary = ExplorerSelectionSummaryService.Resolve(
    [
        .. selectedFiles,
        new ExplorerEntry("folder", @"C:\Docs\folder", true, false, null, sharedModifiedDate)
    ]);
    Check("2 files, 1 folder", mixedSelectionSummary.Type, "summarize mixed file and folder selections");
    Check("12 B · folder sizes not included", mixedSelectionSummary.Size, "state explicitly that selected folder sizes are not included");
    Check("Multiple locations", ExplorerSelectionSummaryService.Resolve(
    [
        selectedFiles[0],
        new ExplorerEntry("other.txt", @"D:\Other\other.txt", false, false, 1, new DateTime(2025, 2, 4))
    ]).Location, "report when selected Explorer items come from different locations");
    Throws<ArgumentException>(() => ExplorerSelectionSummaryService.Resolve([]), "reject an empty Explorer selection summary");
    var explorerSortEntries = new[]
    {
        new ExplorerEntry("z-folder", @"C:\items\z-folder", true, false, null, new DateTime(2024, 1, 1)),
        new ExplorerEntry("a.txt", @"C:\items\a.txt", false, false, 20, new DateTime(2024, 1, 2)),
        new ExplorerEntry("b-folder", @"C:\items\b-folder", true, false, null, new DateTime(2024, 1, 3)),
        new ExplorerEntry("b.log", @"C:\items\b.log", false, false, 5, new DateTime(2024, 1, 4))
    };
    Check("z-folder,b-folder,b.log,a.txt", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.Name, ascending: false).Select(entry => entry.Name)), "sort names descending while keeping folders grouped first");
    Check("b-folder,z-folder,b.log,a.txt", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.DateModified, ascending: false).Select(entry => entry.Name)), "sort by modification date with directory grouping");
    Check("b-folder,z-folder,a.txt,b.log", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.Type, ascending: false).Select(entry => entry.Name)), "sort by type with folders grouped first");
    Check("b-folder,z-folder,a.txt,b.log", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.Size, ascending: false).Select(entry => entry.Name)), "sort files by numeric size instead of formatted size text");

    var driveEntries = new[]
    {
        new ExplorerEntry("Zeta (Z:)", @"Z:\", true, true, null, DateTime.MinValue) { DriveType = DriveType.Fixed, DriveGroupOrder = 0, DriveGroup = "Hard disk drives" },
        new ExplorerEntry("Alpha (C:)", @"C:\", true, true, null, DateTime.MinValue) { DriveType = DriveType.Fixed, DriveGroupOrder = 0, DriveGroup = "Hard disk drives" },
        new ExplorerEntry("USB (E:)", @"E:\", true, true, null, DateTime.MinValue) { DriveType = DriveType.Removable, DriveGroupOrder = 1, DriveGroup = "Devices with removable storage" },
        new ExplorerEntry("Share (N:)", @"N:\", true, true, null, DateTime.MinValue) { DriveType = DriveType.Network, DriveGroupOrder = 3, DriveGroup = "Network locations" }
    };
    Check("Hard disk drives", ExplorerDriveCatalog.GetGroup(DriveType.Fixed).Name, "group fixed drives as hard disks");
    Check("Devices with removable storage", ExplorerDriveCatalog.GetGroup(DriveType.Removable).Name, "group removable drives separately");
    Check("CD drive", ExplorerDriveCatalog.GetTypeName(DriveType.CDRom), "label optical drive types");
    Check(75d, ExplorerDriveCatalog.GetUsagePercent(100, 25), "calculate used drive space as a percentage");
    Check(0d, ExplorerDriveCatalog.GetUsagePercent(0, 0), "avoid dividing by zero for unavailable drive capacity");
    Check("25 B free of 100 B", ExplorerDriveCatalog.GetSpaceSummary(100, 25), "summarize available and total drive capacity");
    Check("100 B free of 100 B", ExplorerDriveCatalog.GetSpaceSummary(100, 200), "clamp reported free space to the drive capacity");
    Check("0 B free of 100 B", ExplorerDriveCatalog.GetSpaceSummary(100, -1), "clamp invalid negative free space to zero");
    Check("Alpha (C:),Zeta (Z:),USB (E:),Share (N:)", string.Join(',', ExplorerDriveCatalog.Sort(driveEntries, ExplorerSortColumn.Name, ascending: true).Select(entry => entry.Name)), "sort drives within stable drive-type groups");

    var firstExplorerTab = new ExplorerTabState(new ExplorerLocation(explorerTestDirectory));
    Check(true, firstExplorerTab.GroupDrives, "enable This PC drive grouping by default per Explorer tab");
    firstExplorerTab.GroupDrives = false;
    firstExplorerTab.ViewMode = ExplorerViewMode.LargeIcons;
    firstExplorerTab.SortColumn = ExplorerSortColumn.DateModified;
    firstExplorerTab.SortAscending = false;
    firstExplorerTab.HomeSortColumn = ExplorerSortColumn.Size;
    firstExplorerTab.HomeSortAscending = false;
    firstExplorerTab.HomeSortExplicitly = true;
    firstExplorerTab.PushHistory(new ExplorerLocation(explorerTestDirectory, SearchQuery: "draft"));
    firstExplorerTab.Location = new ExplorerLocation(firstCreatedFolder);
    var secondExplorerTab = new ExplorerTabState(new ExplorerLocation(nestedExplorerFolder));
    var thirdExplorerTab = new ExplorerTabState(new ExplorerLocation(null, IsHome: true));
    var explorerTabs = new[] { firstExplorerTab, secondExplorerTab, thirdExplorerTab };
    var folderEntryForTab = new ExplorerEntry("Documents", @"C:\Users\test\Documents", true, false, null, DateTime.MinValue);
    Check(@"C:\Users\test\Documents", ExplorerTabManagement.GetNewTabLocation(folderEntryForTab)!.Path, "open an Explorer folder in a new tab");
    Check(true, ExplorerTabManagement.GetNewTabLocation(new ExplorerEntry("C:", @"C:\", true, true, null, DateTime.MinValue))!.IsDriveList, "open a drive in a new This PC tab");
    CheckTrue(ExplorerTabManagement.GetNewTabLocation(new ExplorerEntry("notes.txt", @"C:\notes.txt", false, false, 10, DateTime.MinValue)) is null, "do not offer a new tab for files");
    Check(2, ExplorerTabManagement.GetTabsToClose(explorerTabs, firstExplorerTab, closeOtherTabs: true).Count, "close every Explorer tab except the selected tab");
    Check(true, ExplorerTabManagement.GetTabsToClose(explorerTabs, secondExplorerTab, closeOtherTabs: false).Single().Location.IsHome, "close only Explorer tabs to the right of the selected tab");
    Check(0, ExplorerTabManagement.GetTabsToClose(explorerTabs, new ExplorerTabState(new ExplorerLocation(null, IsHome: true)), closeOtherTabs: true).Count, "leave tabs unchanged when a stale tab is not in the strip");
    var duplicatedExplorerTab = firstExplorerTab.Duplicate();
    Check(firstExplorerTab.Location, duplicatedExplorerTab.Location, "duplicate an Explorer tab at its current location");
    Check(firstExplorerTab.ViewMode, duplicatedExplorerTab.ViewMode, "duplicate an Explorer tab with its view layout");
    Check(firstExplorerTab.SortColumn, duplicatedExplorerTab.SortColumn, "duplicate an Explorer tab with its sort column");
    Check(firstExplorerTab.SortAscending, duplicatedExplorerTab.SortAscending, "duplicate an Explorer tab with its sort direction");
    Check(firstExplorerTab.HomeSortColumn, duplicatedExplorerTab.HomeSortColumn, "duplicate an Explorer tab with its Home sort column");
    Check(firstExplorerTab.HomeSortAscending, duplicatedExplorerTab.HomeSortAscending, "duplicate an Explorer tab with its Home sort direction");
    Check(firstExplorerTab.HomeSortExplicitly, duplicatedExplorerTab.HomeSortExplicitly, "duplicate an Explorer tab with its Home sort preference");
    Check(firstExplorerTab.GroupDrives, duplicatedExplorerTab.GroupDrives, "duplicate an Explorer tab with its drive grouping preference");
    Check(1, duplicatedExplorerTab.Back.Count, "copy an Explorer tab's back history when duplicating");
    Check(true, !ReferenceEquals(firstExplorerTab.Back, duplicatedExplorerTab.Back), "keep duplicated Explorer navigation history independent");
    Check(true, secondExplorerTab.GroupDrives, "keep drive grouping preferences isolated per tab");
    Check(0, secondExplorerTab.Back.Count, "keep Explorer tab history isolated per tab");
    Check(ExplorerViewMode.Details, secondExplorerTab.ViewMode, "keep Explorer view layout state isolated per tab");
    Check(ExplorerSortColumn.Name, secondExplorerTab.SortColumn, "keep Explorer sort columns isolated per tab");
    Check(true, secondExplorerTab.SortAscending, "keep Explorer sort directions isolated per tab");
    Check(ExplorerSortColumn.Name, secondExplorerTab.HomeSortColumn, "keep Home sort columns isolated per tab");
    Check(true, secondExplorerTab.HomeSortAscending, "keep Home sort directions isolated per tab");
    Check(false, secondExplorerTab.HomeSortExplicitly, "keep the Home recent-order preference isolated per tab");
    Check("draft", firstExplorerTab.GoBack(new ExplorerLocation(firstCreatedFolder))!.SearchQuery, "navigate back to a tab's previous search query");
    Check(firstCreatedFolder, firstExplorerTab.GoForward(new ExplorerLocation(explorerTestDirectory, SearchQuery: "draft"))!.Path, "navigate forward to a tab's previous folder");
    firstExplorerTab.PushHistory(new ExplorerLocation(firstCreatedFolder));
    Check(0, firstExplorerTab.Forward.Count, "clear forward history after navigating to a new Explorer location");

    var freshPreferencesStore = new DesktopPreferencesStore(Path.Combine(temporaryPreferencesDirectory, "new-install.json"));
    Check(true, freshPreferencesStore.Load().TaskbarOnAllDisplays, "enable all displays by default for a new installation");
    Check(TaskbarStyle.EdgeToEdge, freshPreferencesStore.Load().TaskbarLayout, "default a new install to the full-edge taskbar layout");
    var staleTaskbarSnapshot = Path.Combine(temporaryPreferencesDirectory, "taskbar-restore.json");
    File.WriteAllText(staleTaskbarSnapshot, """[{"Handle":-1,"WasVisible":true}]""");
    NativeTaskbarVisibilityService.RestoreSnapshot(staleTaskbarSnapshot);
    Check(false, File.Exists(staleTaskbarSnapshot), "clean up a valid taskbar recovery snapshot after skipping a stale window handle");
    var preferencesStore = new DesktopPreferencesStore(preferencesPath);
    var expectedPreferences = new DesktopPreferences(TaskbarEdge.Left, TaskbarSize.Large, true,
        [new PinnedTaskbarApp("Projects", @"C:\Users\test\Projects", true)], true, StartMenuStyle.Classic, false, TaskbarStyle.Floating,
        PinnedStartApps: [new AppEntry("Editor", @"C:\Apps\Editor.lnk")], ReplaceNativeTaskbar: true, TaskbarDynamicTransparency: true, TaskbarButtonEffect: TaskbarButtonEffect.DynamicAura);
    preferencesStore.Save(expectedPreferences);
    var loadedPreferences = preferencesStore.Load();
    Check(expectedPreferences.TaskbarEdge, loadedPreferences.TaskbarEdge, "persist taskbar edge");
    Check(expectedPreferences.TaskbarSize, loadedPreferences.TaskbarSize, "persist taskbar size");
    Check(expectedPreferences.StartMenuStyle, loadedPreferences.StartMenuStyle, "persist Start menu style");
    Check("Editor", loadedPreferences.PinnedStartApps!.Single().Name, "persist pinned Start apps");
    Check(expectedPreferences.TaskbarOnAllDisplays, loadedPreferences.TaskbarOnAllDisplays, "persist taskbar display coverage");
    Check(expectedPreferences.TaskbarLayout, loadedPreferences.TaskbarLayout, "persist floating taskbar style");
    Check(true, loadedPreferences.ReplaceNativeTaskbar, "persist native taskbar replacement mode");
    Check(true, loadedPreferences.TaskbarDynamicTransparency, "persist adaptive taskbar transparency");
    Check(TaskbarButtonEffect.DynamicAura, loadedPreferences.TaskbarButtonEffect, "persist the Dynamic Aura button effect");
    Check(expectedPreferences.TaskbarGrouping, loadedPreferences.TaskbarGrouping, "persist taskbar grouping mode");
    Check(expectedPreferences.TaskbarButtonAlignment, loadedPreferences.TaskbarButtonAlignment, "persist taskbar button alignment");
    preferencesStore.Save(expectedPreferences with { StartWithWindows = true });
    Check(true, preferencesStore.Load().StartWithWindows, "persist automatic taskbar startup preference");
    preferencesStore.Save(expectedPreferences with { AutoHideWhenMaximized = true });
    Check(true, preferencesStore.Load().AutoHideWhenMaximized, "persist maximize-aware taskbar auto-hide preference");
    preferencesStore.Save(expectedPreferences with { TaskbarTransparency = 30 });
    Check(30, preferencesStore.Load().TaskbarTransparency, "persist taskbar transparency");
    Check(false, new DesktopPreferences(TaskbarEdge.Bottom).StartWithWindows, "leave automatic taskbar startup disabled for older preferences");
    preferencesStore.Save(expectedPreferences with { TaskbarLayout = TaskbarStyle.Segmented });
    Check(TaskbarStyle.Segmented, preferencesStore.Load().TaskbarLayout, "persist segmented taskbar style");
    preferencesStore.Save(expectedPreferences with { TaskbarGrouping = TaskbarGroupingMode.Never });
    Check(TaskbarGroupingMode.Never, preferencesStore.Load().TaskbarGrouping, "persist ungrouped taskbar mode");
    preferencesStore.Save(expectedPreferences with { TaskbarButtonAlignment = TaskbarButtonAlignment.Left });
    Check(TaskbarButtonAlignment.Left, preferencesStore.Load().TaskbarButtonAlignment, "persist left-aligned taskbar buttons");
    preferencesStore.Save(expectedPreferences with { TaskbarShowLabels = false, TaskbarIconSize = TaskbarIconSize.Large });
    Check(false, preferencesStore.Load().TaskbarShowLabels, "persist hidden taskbar labels");
    Check(TaskbarIconSize.Large, preferencesStore.Load().TaskbarIconSize, "persist large taskbar icons");
    preferencesStore.Save(expectedPreferences with { TaskbarButtonSpacing = TaskbarButtonSpacing.Relaxed });
    Check(TaskbarButtonSpacing.Relaxed, preferencesStore.Load().TaskbarButtonSpacing, "persist relaxed taskbar button spacing");
    Check(true, loadedPreferences.PinnedApps!.Single().IsDirectory, "persist folder pin type");

    File.WriteAllText(preferencesPath, """{"TaskbarEdge":0,"PinnedApps":[{"Name":"Legacy app","ExecutablePath":"C:\\Apps\\Editor.exe"}]}""");
    Check(StartMenuStyle.Modern, preferencesStore.Load().StartMenuStyle, "default legacy preferences to the Modern Start menu");
    Check(false, preferencesStore.Load().TaskbarOnAllDisplays, "keep legacy taskbar preferences on the primary display");
    Check(false, preferencesStore.Load().ReplaceNativeTaskbar, "keep native taskbar replacement disabled for legacy preferences");
    Check(false, preferencesStore.Load().StartWithWindows, "disable sign-in startup for older preference files");
    Check(5, preferencesStore.Load().TaskbarTransparency, "default taskbar transparency for older preference files");
    Check(false, preferencesStore.Load().TaskbarDynamicTransparency, "disable adaptive transparency for older preference files");
    Check(TaskbarButtonEffect.Accent, preferencesStore.Load().TaskbarButtonEffect, "default older preference files to the Windows accent button effect");
    Check(TaskbarStyle.EdgeToEdge, preferencesStore.Load().TaskbarLayout, "default legacy preferences to a full-edge taskbar");
    Check(TaskbarGroupingMode.Always, preferencesStore.Load().TaskbarGrouping, "default legacy preferences to grouped taskbar buttons");
    Check(TaskbarButtonAlignment.Center, preferencesStore.Load().TaskbarButtonAlignment, "default legacy preferences to centered taskbar buttons");
    Check(true, preferencesStore.Load().TaskbarShowLabels, "default legacy preferences to visible taskbar labels");
    Check(TaskbarIconSize.Standard, preferencesStore.Load().TaskbarIconSize, "default legacy preferences to standard taskbar icons");
    Check(TaskbarButtonSpacing.Standard, preferencesStore.Load().TaskbarButtonSpacing, "default legacy preferences to standard button spacing");
    Check(false, preferencesStore.Load().PinnedApps!.Single().IsDirectory, "default old pin records to app launch behavior");
    File.WriteAllText(preferencesPath, """{"TaskbarEdge":0,"TaskbarButtonEffect":99}""");
    Check(TaskbarButtonEffect.Accent, preferencesStore.Load().TaskbarButtonEffect, "reject an unknown taskbar button effect and fall back to the default");

    var profilePath = Path.Combine(temporaryPreferencesDirectory, "appearance-profile.json");
    var profileStore = new ProfileStore();
    var appearanceValues = new Dictionary<string, int>
    {
        [appModeSetting.Id] = 0,
        [systemModeSetting.Id] = 0,
        [transparencySetting.Id] = 1
    };
    profileStore.SaveProfile(profilePath, "Dark appearance", appearanceValues);
    var loadedAppearanceProfile = profileStore.LoadProfile(profilePath);
    Check(appearanceValues[appModeSetting.Id], loadedAppearanceProfile.Settings[appModeSetting.Id], "round-trip app mode in a profile");
    Check(appearanceValues[systemModeSetting.Id], loadedAppearanceProfile.Settings[systemModeSetting.Id], "round-trip system mode in a profile");
    Check(appearanceValues[transparencySetting.Id], loadedAppearanceProfile.Settings[transparencySetting.Id], "round-trip transparency in a profile");
}
finally
{
    if (Directory.Exists(temporaryPreferencesDirectory)) Directory.Delete(temporaryPreferencesDirectory, recursive: true);
}

Console.WriteLine($"Passed {count} desktop customization checks.");

void Check<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    count++;
}

void CheckTrue(bool result, string label)
{
    if (!result) throw new InvalidOperationException($"{label}: expected true.");
    count++;
}

void Throws<TException>(Action action, string label) where TException : Exception
{
    try { action(); }
    catch (TException) { count++; return; }
    throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
}
