using DesktopTuner;
using System.Windows;

var count = 0;
Check(false, new DesktopPreferences(TaskbarEdge.Bottom).TaskbarOnAllDisplays, "preserve the primary-display behavior for older preference data");
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
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom, TaskbarLayout: TaskbarStyle.Floating), nativeTray), "keep shortcut fallback on floating bars");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Top), nativeTray), "keep shortcut fallback on top bars");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), null), "keep shortcut fallback when the native tray is unavailable");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), new(100, 1030, 100, 50)), "keep shortcut fallback when the tray boundary leaves too little taskbar space");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), new(1500, 700, 420, 50)), "keep shortcut fallback when tray is not at the bottom edge");
var secondaryDisplay = new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false, 1.5, 1.5);
Check(new TaskbarBounds(-1920, 799, 1320, 81), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(secondaryDisplay, new(TaskbarEdge.Bottom), new(-600, 835, 600, 45)), "preserve native tray geometry on a scaled secondary display");
var primaryDisplay = new TaskbarDisplay("DISPLAY1", 0, 0, 2560, 1440, true, 1.25, 1.25);
var secondaryBar = TaskbarLayoutCalculator.Calculate(secondaryDisplay, new(TaskbarEdge.Bottom), false);
Check(-1920d, secondaryBar.Left, "place taskbar on a monitor with negative desktop coordinates");
Check(799d, secondaryBar.Top, "scale taskbar thickness for a high-DPI display");
Check(1920d, secondaryBar.Width, "span the taskbar across the selected monitor only");
Check(81d, secondaryBar.Height, "scale the taskbar height in physical pixels");
var secondaryStart = TaskbarLayoutCalculator.CalculateStartMenu(secondaryDisplay, 470, 650, new(TaskbarEdge.Bottom));
Check(-1902d, secondaryStart.Left, "anchor Start menu to the selected secondary display");
Check(-188d, secondaryStart.Top, "keep Start menu within display bounds above its taskbar");
var floatingStart = TaskbarLayoutCalculator.CalculateStartMenu(secondaryDisplay, 470, 650, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Floating));
Check(-1614d, floatingStart.Left, "anchor Start menu to the floating taskbar segment");
Check(2, TaskbarDisplayService.Select([secondaryDisplay, primaryDisplay], true).Count, "select all connected displays");
Check("DISPLAY1", TaskbarDisplayService.Select([secondaryDisplay, primaryDisplay], false).Single().DeviceName, "select only the primary display");
CheckTrue(TaskbarAutoHidePolicy.ShouldCollapse(true, false, false), "auto-hide collapses when idle");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(false, false, false), "auto-hide respects disabled state");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(true, true, false), "auto-hide stays expanded while pointer is over it");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(true, false, true), "auto-hide stays expanded while Start is open");
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
    new RunningWindow((nint)1, "Document one", "Editor", @"C:\Apps\editor.exe", false),
    new RunningWindow((nint)2, "Document two", "Editor", @"C:\Apps\EDITOR.exe", false),
    new RunningWindow((nint)3, "Inbox", "Mail", @"C:\Apps\mail.exe", false)
};
var alwaysGroupedWindows = TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Always, 10);
Check(2, alwaysGroupedWindows.Count, "always group windows from the same executable");
Check("Editor (2)", alwaysGroupedWindows[0].Label, "show the app name and window count for a grouped button");
Check("Document one" + Environment.NewLine + "Document two", alwaysGroupedWindows[0].ToolTip, "list window titles in a grouped button tooltip");
Check(3, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Never, 1).Count, "never group taskbar windows");
Check(3, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.WhenFull, 3).Count, "keep windows separate while the taskbar has capacity");
Check(2, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.WhenFull, 2).Count, "group windows when the taskbar is full");
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
var presentationPreferences = new DesktopPreferences(TaskbarEdge.Bottom, TaskbarShowLabels: false, TaskbarIconSize: TaskbarIconSize.Large);
var pinPresentation = TaskbarButtonViewModel.FromPin(new PinnedTaskbarApp("Editor", Environment.ProcessPath!), presentationPreferences, vertical: false);
Check("Editor", pinPresentation.Label, "retain pinned app labels in taskbar presentation data");
Check(false, pinPresentation.ShowLabel, "hide pinned app labels when the option is disabled");
Check(24, pinPresentation.IconPixels, "apply the selected icon size to pinned taskbar buttons");
Check(new Thickness(0), pinPresentation.IconMargin, "remove the icon-to-label gap when labels are hidden");
var verticalPinPresentation = TaskbarButtonViewModel.FromPin(new PinnedTaskbarApp("Editor", Environment.ProcessPath!), presentationPreferences with { TaskbarButtonSpacing = TaskbarButtonSpacing.Relaxed }, vertical: true);
Check(new Thickness(0, 4, 0, 4), verticalPinPresentation.ButtonMargin, "apply selected vertical button spacing in taskbar presentation data");
var largeAppCatalog = Enumerable.Range(0, 55)
    .Select(index => new AppEntry($"Application {index:D2}", $@"C:\Apps\application{index:D2}.lnk"))
    .Append(new AppEntry("Zebra Editor", @"C:\Apps\zebra-editor.lnk"))
    .ToList();
Check(56, AppCatalogService.Search(largeAppCatalog).Count, "show every app in a large Start menu catalog");
Check("Zebra Editor", AppCatalogService.Search(largeAppCatalog, " zebra ").Single().Name, "search apps beyond the first 40 catalog entries");
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
Check("shell:MyComputerFolder", StartMenuPlaceCatalog.ResolveTarget("computer"), "open This PC from the Start places menu");
Check("control.exe", StartMenuPlaceCatalog.ResolveTarget("control-panel"), "open Control Panel from the Start places menu");
Check(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), StartMenuPlaceCatalog.ResolveTarget("music"), "open the user's Music folder from the Start places menu");
Check(7, StartMenuPlaceCatalog.AdditionalPlaces.Count, "show the supported additional Start system places");
Throws<ArgumentOutOfRangeException>(() => StartMenuPlaceCatalog.ResolveTarget("unknown"), "reject unknown Start system places");
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
var shortcutGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, shortcutGesture.KeyDown(0x5c), "capture a right Windows key press");
Check(WindowsKeyAction.ForwardWindowsDownThenPass, shortcutGesture.KeyDown('R'), "forward the modifier for Win+R");
Check((uint?)0x5c, shortcutGesture.HeldWindowsKey, "preserve right Windows key identity");
Check(WindowsKeyAction.PassThrough, shortcutGesture.KeyUp('R'), "pass shortcut key release");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, shortcutGesture.KeyUp(0x5c), "release the forwarded Windows key");
Check<uint?>(null, shortcutGesture.HeldWindowsKey, "clear Windows-key state after a shortcut");
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
CheckTrue(SystemFlyoutService.GetNotificationAreaSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'B', false),
    new KeyboardKeyEvent((ushort)'B', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+B notification-area shortcut in balanced key order");
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
    var freshPreferencesStore = new DesktopPreferencesStore(Path.Combine(temporaryPreferencesDirectory, "new-install.json"));
    Check(true, freshPreferencesStore.Load().TaskbarOnAllDisplays, "enable all displays by default for a new installation");
    Check(TaskbarStyle.EdgeToEdge, freshPreferencesStore.Load().TaskbarLayout, "default a new install to the full-edge taskbar layout");
    var preferencesStore = new DesktopPreferencesStore(preferencesPath);
    var expectedPreferences = new DesktopPreferences(TaskbarEdge.Left, TaskbarSize.Large, true,
        [new PinnedTaskbarApp("Projects", @"C:\Users\test\Projects", true)], true, StartMenuStyle.Classic, false, TaskbarStyle.Floating);
    preferencesStore.Save(expectedPreferences);
    var loadedPreferences = preferencesStore.Load();
    Check(expectedPreferences.TaskbarEdge, loadedPreferences.TaskbarEdge, "persist taskbar edge");
    Check(expectedPreferences.TaskbarSize, loadedPreferences.TaskbarSize, "persist taskbar size");
    Check(expectedPreferences.StartMenuStyle, loadedPreferences.StartMenuStyle, "persist Start menu style");
    Check(expectedPreferences.TaskbarOnAllDisplays, loadedPreferences.TaskbarOnAllDisplays, "persist taskbar display coverage");
    Check(expectedPreferences.TaskbarLayout, loadedPreferences.TaskbarLayout, "persist floating taskbar style");
    Check(expectedPreferences.TaskbarGrouping, loadedPreferences.TaskbarGrouping, "persist taskbar grouping mode");
    Check(expectedPreferences.TaskbarButtonAlignment, loadedPreferences.TaskbarButtonAlignment, "persist taskbar button alignment");
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
    Check(TaskbarStyle.EdgeToEdge, preferencesStore.Load().TaskbarLayout, "default legacy preferences to a full-edge taskbar");
    Check(TaskbarGroupingMode.Always, preferencesStore.Load().TaskbarGrouping, "default legacy preferences to grouped taskbar buttons");
    Check(TaskbarButtonAlignment.Center, preferencesStore.Load().TaskbarButtonAlignment, "default legacy preferences to centered taskbar buttons");
    Check(true, preferencesStore.Load().TaskbarShowLabels, "default legacy preferences to visible taskbar labels");
    Check(TaskbarIconSize.Standard, preferencesStore.Load().TaskbarIconSize, "default legacy preferences to standard taskbar icons");
    Check(TaskbarButtonSpacing.Standard, preferencesStore.Load().TaskbarButtonSpacing, "default legacy preferences to standard button spacing");
    Check(false, preferencesStore.Load().PinnedApps!.Single().IsDirectory, "default old pin records to app launch behavior");

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
