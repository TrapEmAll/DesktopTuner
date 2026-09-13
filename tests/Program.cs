using DesktopTuner;

var count = 0;
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom), false), "bottom, standard");
Check(new TaskbarBounds(0, 0, 1920, 46), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Top, TaskbarSize.Small), false), "top, small");
Check(new TaskbarBounds(0, 0, 204, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Large), false), "left, large");
Check(new TaskbarBounds(1744, 0, 176, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Right), false), "right, standard");
Check(new TaskbarBounds(1916, 0, 4, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Right), true), "right, collapsed");
Check(new TaskbarBounds(0, 1076, 1920, 4), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom), true), "bottom, collapsed");
CheckTrue(TaskbarAutoHidePolicy.ShouldCollapse(true, false, false), "auto-hide collapses when idle");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(false, false, false), "auto-hide respects disabled state");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(true, true, false), "auto-hide stays expanded while pointer is over it");
CheckTrue(!TaskbarAutoHidePolicy.ShouldCollapse(true, false, true), "auto-hide stays expanded while Start is open");
var firstPin = TaskbarPinCatalog.Add([], "Editor", @"C:\Program Files\Editor\editor.exe");
Check(1, firstPin.Count, "pin a running app executable");
Check(1, TaskbarPinCatalog.Add(firstPin, "Editor", @"C:\Program Files\Editor\editor.exe").Count, "avoid duplicate pins");
Check(1, TaskbarPinCatalog.Add(firstPin, "Script", @"C:\Tools\script.cmd").Count, "reject non-executable pin paths");
Check(0, TaskbarPinCatalog.Remove(firstPin, @"C:\Program Files\Editor\editor.exe").Count, "unpin an app executable");
var droppedPins = TaskbarPinCatalog.AddDroppedFiles([], [@"C:\Apps\Editor.exe", @"C:\Apps\Editor.exe", @"C:\Docs\readme.txt", "relative.exe"]);
Check(1, droppedPins.Count, "accept unique absolute executable drops only");
Check("Editor", droppedPins[0].Name, "derive dropped app name from executable filename");
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
Throws<ArgumentOutOfRangeException>(() => TaskbarLayoutCalculator.Calculate(0, 1080, new(TaskbarEdge.Bottom), false), "rejects invalid screen bounds");
var appModeSetting = SettingsCatalog.ById("explorer-app-mode");
var systemModeSetting = SettingsCatalog.ById("explorer-system-mode");
var transparencySetting = SettingsCatalog.ById("explorer-transparency");
Check(SettingsCatalog.Personalize, appModeSetting.RegistryPath, "use shared Windows personalization registry location for app color mode");
Check("AppsUseLightTheme", appModeSetting.ValueName, "target Windows app color mode value");
Check("SystemUsesLightTheme", systemModeSetting.ValueName, "target Windows system color mode value");
Check("EnableTransparency", transparencySetting.ValueName, "target Windows transparency setting");
Check(0, appModeSetting.Choices.Single(choice => choice.Label == "Dark").Value, "map dark app mode to the Windows registry value");
Check(1, transparencySetting.Choices.Single(choice => choice.Label == "On").Value, "map enabled transparency to the Windows registry value");
var temporaryPreferencesDirectory = Path.Combine(Path.GetTempPath(), $"DesktopTuner.Tests-{Guid.NewGuid():N}");
var preferencesPath = Path.Combine(temporaryPreferencesDirectory, "preferences.json");
try
{
    var preferencesStore = new DesktopPreferencesStore(preferencesPath);
    var expectedPreferences = new DesktopPreferences(TaskbarEdge.Left, TaskbarSize.Large, true, [], true, StartMenuStyle.Classic);
    preferencesStore.Save(expectedPreferences);
    var loadedPreferences = preferencesStore.Load();
    Check(expectedPreferences.TaskbarEdge, loadedPreferences.TaskbarEdge, "persist taskbar edge");
    Check(expectedPreferences.TaskbarSize, loadedPreferences.TaskbarSize, "persist taskbar size");
    Check(expectedPreferences.StartMenuStyle, loadedPreferences.StartMenuStyle, "persist Start menu style");

    File.WriteAllText(preferencesPath, "{\"TaskbarEdge\":\"Bottom\"}");
    Check(StartMenuStyle.Modern, preferencesStore.Load().StartMenuStyle, "default legacy preferences to the Modern Start menu");

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
