using DesktopTuner;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.Text.Json;
using System.Buffers.Binary;

var count = 0;
var desktopHostTestRoot = Path.Combine(Path.GetTempPath(), $"desktop-tuner-desktop-host-{Guid.NewGuid():N}");
var sharedDesktopRoot = Path.Combine(desktopHostTestRoot, "Shared");
var userDesktopRoot = Path.Combine(desktopHostTestRoot, "User");
Directory.CreateDirectory(sharedDesktopRoot);
Directory.CreateDirectory(userDesktopRoot);
File.WriteAllText(Path.Combine(sharedDesktopRoot, "shared.txt"), "shared");
File.WriteAllText(Path.Combine(userDesktopRoot, "user.txt"), "user");
Directory.CreateDirectory(Path.Combine(userDesktopRoot, "Folder"));
File.WriteAllText(Path.Combine(userDesktopRoot, "hidden.txt"), "hidden");
File.SetAttributes(Path.Combine(userDesktopRoot, "hidden.txt"), FileAttributes.Hidden);
var desktopHostEntries = DesktopHostCatalog.ReadItems([userDesktopRoot, sharedDesktopRoot, userDesktopRoot, Path.Combine(desktopHostTestRoot, "Missing")]);
Check("Folder,Recycle Bin,shared.txt,This PC,user.txt", string.Join(',', desktopHostEntries.Select(entry => entry.Name)), "merge desktop item roots, add common shell namespace entries, and skip hidden or missing entries");
Check(true, desktopHostEntries.Single(entry => entry.Name == "Folder").IsDirectory, "identify desktop folders for shell item activation");
Check(true, desktopHostEntries.Single(entry => entry.Name == "This PC").IsShellNamespace, "mark This PC for Shell namespace activation");
Check(true, desktopHostEntries.Single(entry => entry.Name == "user.txt").CanShowNativeContextMenu, "allow native filesystem context verbs for a desktop file");
Check(true, desktopHostEntries.Single(entry => entry.Name == "user.txt").CanRename, "allow inline rename for filesystem desktop items");
Check(true, desktopHostEntries.Single(entry => entry.Name == "This PC").CanShowNativeContextMenu, "offer native Shell context verbs for This PC");
Check(false, desktopHostEntries.Single(entry => entry.Name == "This PC").CanRename, "keep This PC inline rename disabled when the Shell does not advertise rename support");
Check(true, desktopHostEntries.Single(entry => entry.Name == "Recycle Bin").CanShowNativeContextMenu, "offer native Shell context verbs for Recycle Bin");
CheckTrue(ShellOpenWithPolicy.CanOpenWith(true, false, true), "offer Open with for one native file selection");
Check(false, ShellOpenWithPolicy.CanOpenWith(false, false, true), "hide Open with for an empty or multi-item selection");
Check(false, ShellOpenWithPolicy.CanOpenWith(true, true, true), "hide Open with for a folder selection");
Check(false, ShellOpenWithPolicy.CanOpenWith(true, false, false), "hide Open with when native Shell handling is unavailable");
CheckTrue(TaskbarIconService.LoadNamespaceIcon("shell:MyComputerFolder") is not null, "extract a shell icon for a namespace parsing name");
Check(ShellNamespaceOpenAction.NavigateCurrentWindow, ShellNamespaceOpenPolicy.Resolve(isFolder: true, selectionCount: 1), "open one Shell folder in the current namespace browser");
Check(ShellNamespaceOpenAction.OpenCompanionWindow, ShellNamespaceOpenPolicy.Resolve(isFolder: true, selectionCount: 2), "open selected Shell folders in separate companion windows");
Check(ShellNamespaceOpenAction.UseShellHandler, ShellNamespaceOpenPolicy.Resolve(isFolder: false, selectionCount: 2), "open selected Shell documents through their registered handlers");
var browserRenameEntry = new DesktopShellNamespaceEntry("Rename me", "shell:RenameFixture", false);
browserRenameEntry.IsCut = true;
Check(true, browserRenameEntry.IsCut, "mark a Shell namespace item as cut for Explorer-style dimming");
Check(true, ShellClipboardPolicy.IsCutDropEffect(2), "recognize the native Shell move effect as a cut operation");
Check(false, ShellClipboardPolicy.IsCutDropEffect(1), "keep copied clipboard items at normal opacity");
Check(false, ShellClipboardPolicy.IsCutDropEffect(0), "leave clipboard data without a preferred transfer effect unmarked");
Check(false, ShellClipboardPolicy.IsCutDropEffect(3), "avoid treating an ambiguous combined transfer effect as a cut");
var shellClipboardIdList = new byte[18];
BinaryPrimitives.WriteUInt32LittleEndian(shellClipboardIdList, 1);
BinaryPrimitives.WriteUInt32LittleEndian(shellClipboardIdList.AsSpan(4), 12);
BinaryPrimitives.WriteUInt32LittleEndian(shellClipboardIdList.AsSpan(8), 14);
BinaryPrimitives.WriteUInt16LittleEndian(shellClipboardIdList.AsSpan(14), 2);
Check(true, ShellClipboardPolicy.TryReadShellIdListArray(shellClipboardIdList, out var shellClipboardParentOffset, out var shellClipboardItemOffsets), "validate a native Shell clipboard ID-list array");
Check(12, shellClipboardParentOffset, "read the parent PIDL offset from the Shell clipboard array");
Check("14", string.Join(',', shellClipboardItemOffsets), "read relative item PIDL offsets from the Shell clipboard array");
Check(false, ShellClipboardPolicy.TryReadShellIdListArray(shellClipboardIdList.AsSpan(0, 11), out _, out _), "reject truncated Shell clipboard array headers");
var malformedShellClipboardIdList = shellClipboardIdList.ToArray();
BinaryPrimitives.WriteUInt32LittleEndian(malformedShellClipboardIdList.AsSpan(8), uint.MaxValue);
Check(false, ShellClipboardPolicy.TryReadShellIdListArray(malformedShellClipboardIdList, out _, out _), "reject out-of-range Shell clipboard PIDL offsets");
var overlappingShellClipboardIdList = shellClipboardIdList.ToArray();
BinaryPrimitives.WriteUInt16LittleEndian(overlappingShellClipboardIdList.AsSpan(12), 6);
Check(false, ShellClipboardPolicy.TryReadShellIdListArray(overlappingShellClipboardIdList, out _, out _), "reject Shell clipboard PIDLs that overlap adjacent entries");
browserRenameEntry.IsCut = false;
Check(false, browserRenameEntry.IsCut, "clear cut-state dimming when the clipboard changes");
Check(false, browserRenameEntry.CanRename, "keep Shell browser rename disabled until the native capability is checked");
browserRenameEntry.SetRenameCapability(true);
Check(true, browserRenameEntry.CanRename && browserRenameEntry.RenameCapabilityChecked, "cache native Shell rename capability on a browser item");
browserRenameEntry.IsRenaming = true;
browserRenameEntry.RenameText = "Renamed";
Check("Renamed", browserRenameEntry.RenameText, "store inline rename text for a Shell browser item");
var shellNamespaceDesktopEntries = DesktopHostCatalog.ReadItems([userDesktopRoot, sharedDesktopRoot], includeDesktopNamespace: true);
CheckTrue(shellNamespaceDesktopEntries.Any(entry => entry.Name == "Network" && entry.IsShellNamespace), "include Network from the Windows desktop Shell namespace");
CheckTrue(shellNamespaceDesktopEntries.Any(entry => entry.Name == "Libraries" && entry.IsShellNamespace), "include Libraries from the Windows desktop Shell namespace");
CheckTrue(shellNamespaceDesktopEntries.Any(entry => entry.Name == "Control Panel" && entry.IsShellNamespace), "include Control Panel from the Windows desktop Shell namespace");
CheckTrue(shellNamespaceDesktopEntries.Any(entry => entry.FullPath == "shell:MyComputerFolder"), "keep This PC's existing persisted Shell identity during namespace enumeration");
CheckTrue(shellNamespaceDesktopEntries.Any(entry => entry.FullPath == "shell:RecycleBinFolder"), "keep Recycle Bin's existing persisted Shell identity during namespace enumeration");
CheckTrue(shellNamespaceDesktopEntries.Where(entry => entry.IsShellNamespace).All(entry => entry.CanShowNativeContextMenu), "enable native context menus for every discovered Shell namespace entry");
CheckTrue(shellNamespaceDesktopEntries.Where(entry => entry.IsShellNamespace).All(entry =>
    entry.CanRename == NativeShellContextMenuService.CanRenameShellItem(entry.FullPath)), "show inline rename only when the native Shell advertises SFGAO_CANRENAME");
CheckTrue(shellNamespaceDesktopEntries.Any(entry => entry.FullPath == Path.Combine(userDesktopRoot, "user.txt")), "retain custom filesystem roots alongside Shell namespace entries");
Check(shellNamespaceDesktopEntries.Where(entry => entry.IsShellNamespace).Count(), shellNamespaceDesktopEntries.Where(entry => entry.IsShellNamespace)
    .Select(entry => entry.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).Count(), "show each Windows desktop namespace label once");
var desktopLibraries = shellNamespaceDesktopEntries.Single(entry => entry.Name == "Libraries");
CheckTrue(await NativeShellContextMenuService.ProbeShellItemContextMenuAsync(desktopLibraries.FullPath), "build a native context menu for a dynamically enumerated Shell namespace item");
CheckTrue(await NativeShellContextMenuService.ProbeShellItemDataObjectAsync(desktopLibraries.FullPath), "create a native Shell drag object for a virtual desktop item");
CheckTrue(await NativeShellContextMenuService.ProbeShellItemsDataObjectAsync([
    shellNamespaceDesktopEntries.Single(entry => entry.Name == "user.txt").FullPath,
    desktopLibraries.FullPath
]), "create a combined Shell drag object for filesystem and virtual desktop items");
var desktopLayoutStore = new DesktopHostLayoutStore(Path.Combine(desktopHostTestRoot, "desktop-layout.json"));
var laidOutDesktopEntries = desktopLayoutStore.ApplyLayout(desktopHostEntries, 600, 400).ToArray();
laidOutDesktopEntries.Single(entry => entry.Name == "user.txt").SetPosition(new DesktopHostPosition(0, 112));
Check(true, desktopLayoutStore.SaveOrder([laidOutDesktopEntries.Single(entry => entry.Name == "user.txt"), laidOutDesktopEntries.Single(entry => entry.Name == "Folder")]), "save desktop icon order and positions");
var restoredDesktopEntries = desktopLayoutStore.ApplyLayout(desktopHostEntries, 600, 400);
Check("user.txt,Folder,Recycle Bin,shared.txt,This PC", string.Join(',', restoredDesktopEntries.Select(entry => entry.Name)), "restore the saved icon order while appending unrecorded items");
Check(new DesktopHostPosition(0, 112), new DesktopHostPosition(restoredDesktopEntries.Single(entry => entry.Name == "user.txt").Left, restoredDesktopEntries.Single(entry => entry.Name == "user.txt").Top), "restore a desktop icon's free-form position");
Check(new DesktopHostPosition(0, 0), new DesktopHostPosition(restoredDesktopEntries.Single(entry => entry.Name == "Folder").Left, restoredDesktopEntries.Single(entry => entry.Name == "Folder").Top), "place newly added desktop items in an unoccupied icon slot");
var renameLayoutStore = new DesktopHostLayoutStore(Path.Combine(desktopHostTestRoot, "desktop-rename-layout.json"));
var oldDesktopNamePath = Path.Combine(userDesktopRoot, "before.txt");
var newDesktopNamePath = Path.Combine(userDesktopRoot, "after.txt");
var renameLayoutItems = new[]
{
    new DesktopHostItem("before.txt", oldDesktopNamePath, false),
    new DesktopHostItem("second.txt", Path.Combine(userDesktopRoot, "second.txt"), false)
};
renameLayoutItems[0].SetPosition(new DesktopHostPosition(224, 56));
Check(true, renameLayoutStore.SaveLayout(renameLayoutItems), "save desktop icon layout before a rename");
Check(true, renameLayoutStore.RenamePath(oldDesktopNamePath, newDesktopNamePath), "migrate a renamed desktop item's saved layout identity");
var restoredRenamedLayout = renameLayoutStore.ApplyLayout(
    [new DesktopHostItem("after.txt", newDesktopNamePath, false), renameLayoutItems[1]], 600, 400);
Check("after.txt,second.txt", string.Join(',', restoredRenamedLayout.Select(item => item.Name)), "preserve desktop icon order across a rename");
Check(new DesktopHostPosition(224, 56), new DesktopHostPosition(restoredRenamedLayout[0].Left, restoredRenamedLayout[0].Top), "preserve desktop icon position across a rename");
var desktopMonitorViewports = new DesktopHostMonitorViewport[]
{
    new("DISPLAY1", 0, 0, 600, 400, true),
    new("DISPLAY2", 600, 0, 800, 600, false)
};
var monitorLayoutStore = new DesktopHostLayoutStore(Path.Combine(desktopHostTestRoot, "monitor-layout.json"));
var monitorLayoutItems = monitorLayoutStore.ApplyMonitorLayout(desktopHostEntries, desktopMonitorViewports).ToArray();
Check("DISPLAY1", monitorLayoutItems[0].MonitorDeviceName, "place new desktop items on the primary monitor by default");
var monitorItem = monitorLayoutItems.Single(item => item.Name == "user.txt");
monitorItem.SetPosition(new DesktopHostPosition(620, 112));
monitorItem.MonitorDeviceName = "DISPLAY2";
Check(true, monitorLayoutStore.SaveMonitorLayout(monitorLayoutItems, desktopMonitorViewports), "save desktop positions in monitor-local layouts");
var restoredMonitorItems = monitorLayoutStore.ApplyMonitorLayout(desktopHostEntries, desktopMonitorViewports);
Check(new DesktopHostPosition(620, 112), new DesktopHostPosition(restoredMonitorItems.Single(item => item.Name == "user.txt").Left,
    restoredMonitorItems.Single(item => item.Name == "user.txt").Top), "restore an icon at its independent secondary-monitor position");
Check("DISPLAY2", restoredMonitorItems.Single(item => item.Name == "user.txt").MonitorDeviceName, "restore the monitor identity with its desktop icon position");
var restoredAfterDisconnect = monitorLayoutStore.ApplyMonitorLayout(desktopHostEntries, [desktopMonitorViewports[0]]);
Check(new DesktopHostPosition(20, 112), new DesktopHostPosition(restoredAfterDisconnect.Single(item => item.Name == "user.txt").Left,
    restoredAfterDisconnect.Single(item => item.Name == "user.txt").Top), "move saved icon coordinates to the primary display when their monitor disconnects");
Check("DISPLAY1", restoredAfterDisconnect.Single(item => item.Name == "user.txt").MonitorDeviceName, "reassign disconnected-monitor icons to the primary display");
restoredDesktopEntries.Single(entry => entry.Name == "user.txt").SetPosition(new DesktopHostPosition(900, 900));
Check(true, desktopLayoutStore.SaveLayout(restoredDesktopEntries), "save an icon position outside the current screen bounds");
var clampedDesktopEntry = desktopLayoutStore.ApplyLayout(desktopHostEntries, 600, 400).Single(entry => entry.Name == "user.txt");
Check(new DesktopHostPosition(500, 288), new DesktopHostPosition(clampedDesktopEntry.Left, clampedDesktopEntry.Top), "clamp saved icon positions to the current display bounds");
var legacyLayoutPath = Path.Combine(desktopHostTestRoot, "legacy-layout.json");
File.WriteAllText(legacyLayoutPath, JsonSerializer.Serialize(new[] { desktopHostEntries.Single(entry => entry.Name == "Folder").FullPath }));
Check("Folder,Recycle Bin,shared.txt,This PC,user.txt", string.Join(',', new DesktopHostLayoutStore(legacyLayoutPath).ApplyLayout(desktopHostEntries, 600, 400).Select(entry => entry.Name)), "continue reading legacy order-only desktop layout files");
var desktopPreferencesStore = new DesktopHostLayoutStore(Path.Combine(desktopHostTestRoot, "desktop-preferences.json"));
Check(new DesktopHostLayoutPreferences(), desktopPreferencesStore.ReadPreferences(), "default desktop layout to manual positions with name sorting");
var savedDesktopPreferences = new DesktopHostLayoutPreferences(AutoArrange: true, AlignToGrid: true, SortMode: DesktopHostSortMode.Size);
Check(true, desktopPreferencesStore.SavePreferences(savedDesktopPreferences), "save desktop layout preferences");
Check(savedDesktopPreferences, desktopPreferencesStore.ReadPreferences(), "restore auto-arrange, grid, and sort preferences");
desktopPreferencesStore.SaveLayout(desktopHostEntries);
Check(savedDesktopPreferences, desktopPreferencesStore.ReadPreferences(), "preserve desktop layout preferences when icon positions are saved");

var sortTestRoot = Path.Combine(desktopHostTestRoot, "Sort");
Directory.CreateDirectory(sortTestRoot);
var largeTextPath = Path.Combine(sortTestRoot, "a.txt");
var smallBinaryPath = Path.Combine(sortTestRoot, "b.bin");
File.WriteAllBytes(largeTextPath, [1, 2, 3]);
File.WriteAllBytes(smallBinaryPath, [1]);
File.SetLastWriteTime(largeTextPath, new DateTime(2021, 1, 1));
File.SetLastWriteTime(smallBinaryPath, new DateTime(2022, 1, 1));
var sortableDesktopItems = new[]
{
    new DesktopHostItem("b.bin", smallBinaryPath, false),
    new DesktopHostItem("a.txt", largeTextPath, false)
};
Check("a.txt,b.bin", string.Join(',', DesktopHostArrangementPolicy.Sort(sortableDesktopItems, DesktopHostSortMode.Name).Select(item => item.Name)), "sort desktop icons by name");
Check("b.bin,a.txt", string.Join(',', DesktopHostArrangementPolicy.Sort(sortableDesktopItems, DesktopHostSortMode.ItemType).Select(item => item.Name)), "sort desktop icons by item type");
Check("b.bin,a.txt", string.Join(',', DesktopHostArrangementPolicy.Sort(sortableDesktopItems, DesktopHostSortMode.Size).Select(item => item.Name)), "sort desktop icons by file size");
Check("a.txt,b.bin", string.Join(',', DesktopHostArrangementPolicy.Sort(sortableDesktopItems, DesktopHostSortMode.DateModified).Select(item => item.Name)), "sort desktop icons by modification date");
var arrangeItems = new[] { "c", "a", "b" }.Select(name => new DesktopHostItem(name, Path.Combine(desktopHostTestRoot, name), false)).ToArray();
foreach (var item in arrangeItems) item.SetPosition(new DesktopHostPosition(250, 200));
DesktopHostArrangementPolicy.Arrange(arrangeItems, [desktopMonitorViewports[0]], DesktopHostSortMode.Name);
Check(new DesktopHostPosition(0, 0), new DesktopHostPosition(arrangeItems.Single(item => item.Name == "a").Left, arrangeItems.Single(item => item.Name == "a").Top), "auto-arrange icons from the upper-left in name order");
Check(new DesktopHostPosition(0, 112), new DesktopHostPosition(arrangeItems.Single(item => item.Name == "b").Left, arrangeItems.Single(item => item.Name == "b").Top), "stack auto-arranged desktop icons down each column");
var gridItems = new[] { new DesktopHostItem("first", "first", false), new DesktopHostItem("second", "second", false) };
foreach (var item in gridItems) item.SetPosition(new DesktopHostPosition(205, 126));
DesktopHostArrangementPolicy.AlignToGrid(gridItems, [desktopMonitorViewports[0]]);
Check(new DesktopHostPosition(200, 112), new DesktopHostPosition(gridItems[0].Left, gridItems[0].Top), "align desktop icons to their nearest grid cells");
Check(new DesktopHostPosition(100, 112), new DesktopHostPosition(gridItems[1].Left, gridItems[1].Top), "move colliding grid icons into the nearest free cell");
var selectableDesktopItems = new[] { "a", "b", "c", "d" }
    .Select(name => new DesktopHostItem(name, Path.Combine(desktopHostTestRoot, name), false))
    .ToArray();
selectableDesktopItems[0].IsSelected = true;
selectableDesktopItems[1].IsSelected = true;
Check("a,b", string.Join(',', DesktopHostOpenPolicy.SelectItems(selectableDesktopItems).Select(item => item.Name)),
    "open every selected replacement desktop item together");
selectableDesktopItems[0].IsSelected = false;
selectableDesktopItems[1].IsSelected = false;
Check("c", string.Join(',', DesktopHostOpenPolicy.SelectItems(selectableDesktopItems, selectableDesktopItems[2]).Select(item => item.Name)),
    "fall back to the focused replacement desktop item when selection is empty");
Check(false, DesktopHostContextMenuPolicy.CanInvokeNativeCommand(selectableDesktopItems),
    "disable direct native desktop item commands when the selected paths do not exist");
var nativeDesktopItemPath = Path.Combine(desktopHostTestRoot, "native-item.txt");
File.WriteAllText(nativeDesktopItemPath, "native item");
var nativeDesktopItem = new DesktopHostItem("native-item.txt", nativeDesktopItemPath, false) { IsSelected = true };
Check(true, DesktopHostContextMenuPolicy.CanInvokeNativeCommand([nativeDesktopItem]),
    "enable direct Delete and Properties commands for native desktop items");
nativeDesktopItem.IsSelected = false;
Check(false, DesktopHostContextMenuPolicy.CanInvokeNativeCommand([nativeDesktopItem]),
    "disable direct native desktop item commands when the selection is empty");
Check(true, DesktopHostKeyboardPolicy.ShouldShowProperties(Key.Enter, ModifierKeys.Alt, hasSelection: true),
    "open native properties for selected replacement desktop items with Alt+Enter");
Check(true, DesktopHostKeyboardPolicy.ShouldShowProperties(Key.System, ModifierKeys.Alt, hasSelection: true, systemKey: Key.Enter),
    "recognize WPF system-key events for replacement desktop Alt+Enter");
Check(false, DesktopHostKeyboardPolicy.ShouldShowProperties(Key.Enter, ModifierKeys.Alt, hasSelection: false),
    "leave Alt+Enter unhandled when the replacement desktop selection is empty");
Check(false, DesktopHostKeyboardPolicy.ShouldShowProperties(Key.Enter, ModifierKeys.Alt, hasSelection: true, isEditingName: true),
    "preserve Alt+Enter while editing a replacement desktop item name");
Check(true, DesktopHostKeyboardPolicy.ShouldDeleteSelection(Key.Delete, ModifierKeys.None, hasSelection: true),
    "delete selected replacement desktop items with the Delete key");
Check(true, DesktopHostKeyboardPolicy.ShouldDeleteSelection(Key.Delete, ModifierKeys.Shift, hasSelection: true),
    "request permanent deletion for selected replacement desktop items with Shift+Delete");
Check(false, DesktopHostKeyboardPolicy.ShouldDeleteSelection(Key.Delete, ModifierKeys.None, hasSelection: false),
    "leave Delete unhandled when no replacement desktop items are selected");
Check(false, DesktopHostKeyboardPolicy.ShouldDeleteSelection(Key.Delete, ModifierKeys.None, hasSelection: true, isEditingName: true),
    "preserve Delete while editing a replacement desktop item name");
CheckTrue(DesktopHostKeyboardPolicy.ShouldCreateFolder(Key.N, ModifierKeys.Control | ModifierKeys.Shift),
    "create a replacement desktop folder with Ctrl+Shift+N");
CheckTrue(!DesktopHostKeyboardPolicy.ShouldCreateFolder(Key.N, ModifierKeys.Control | ModifierKeys.Shift, isEditingName: true),
    "preserve Ctrl+Shift+N while editing a replacement desktop item name");
CheckTrue(DesktopHostKeyboardPolicy.ShouldShowContextMenu(Key.F10, ModifierKeys.Shift),
    "open the replacement desktop context menu with Shift+F10");
CheckTrue(DesktopHostKeyboardPolicy.ShouldShowContextMenu(Key.Apps, ModifierKeys.None),
    "open the replacement desktop context menu with the Menu key");
CheckTrue(!DesktopHostKeyboardPolicy.ShouldShowContextMenu(Key.F10, ModifierKeys.Shift, isEditingName: true),
    "preserve Shift+F10 while editing a replacement desktop item name");
Check(DesktopHostClipboardAction.Copy, DesktopHostKeyboardPolicy.ResolveClipboardAction(Key.C, ModifierKeys.Control, hasSelection: true),
    "copy selected replacement desktop items with Ctrl+C");
Check(DesktopHostClipboardAction.Cut, DesktopHostKeyboardPolicy.ResolveClipboardAction(Key.X, ModifierKeys.Control, hasSelection: true),
    "cut selected replacement desktop items with Ctrl+X");
Check(DesktopHostClipboardAction.Paste, DesktopHostKeyboardPolicy.ResolveClipboardAction(Key.V, ModifierKeys.Control, hasSelection: false),
    "paste Shell clipboard items onto the replacement desktop with Ctrl+V");
Check(DesktopHostClipboardAction.None, DesktopHostKeyboardPolicy.ResolveClipboardAction(Key.C, ModifierKeys.Control, hasSelection: false),
    "leave Ctrl+C unhandled when the replacement desktop selection is empty");
Check(DesktopHostClipboardAction.None, DesktopHostKeyboardPolicy.ResolveClipboardAction(Key.X, ModifierKeys.Control, hasSelection: true, isEditingName: true),
    "preserve Ctrl+X text editing while renaming a desktop item");
var cutDesktopItem = new DesktopHostItem("cut.txt", Path.Combine(desktopHostTestRoot, "cut.txt"), false) { IsCut = true };
Check(true, cutDesktopItem.IsCut, "dim a replacement desktop item while its Shell clipboard operation is a cut");
var desktopSelection = DesktopHostSelectionPolicy.Select(selectableDesktopItems, selectableDesktopItems[0].FullPath, false, false, null);
foreach (var item in selectableDesktopItems) item.IsSelected = desktopSelection.Paths.Contains(item.FullPath);
desktopSelection = DesktopHostSelectionPolicy.Select(selectableDesktopItems, selectableDesktopItems[2].FullPath, true, false, desktopSelection.AnchorPath);
foreach (var item in selectableDesktopItems) item.IsSelected = desktopSelection.Paths.Contains(item.FullPath);
Check("a,c", string.Join(',', selectableDesktopItems.Where(item => desktopSelection.Paths.Contains(item.FullPath)).Select(item => item.Name)), "add an item to desktop selection with Ctrl-click");
desktopSelection = DesktopHostSelectionPolicy.Select(selectableDesktopItems, selectableDesktopItems[3].FullPath, false, true, desktopSelection.AnchorPath);
Check("c,d", string.Join(',', selectableDesktopItems.Where(item => desktopSelection.Paths.Contains(item.FullPath)).Select(item => item.Name)), "select an inclusive desktop range with Shift-click");
Check(4, DesktopHostSelectionPolicy.SelectAll(selectableDesktopItems).Count, "select every desktop item with Ctrl+A");
var navigableDesktopItems = new[]
{
    new DesktopHostItem("center", "center", false),
    new DesktopHostItem("right", "right", false),
    new DesktopHostItem("below", "below", false),
    new DesktopHostItem("diagonal", "diagonal", false)
};
navigableDesktopItems[0].SetPosition(new DesktopHostPosition(0, 0));
navigableDesktopItems[1].SetPosition(new DesktopHostPosition(112, 0));
navigableDesktopItems[2].SetPosition(new DesktopHostPosition(0, 112));
navigableDesktopItems[3].SetPosition(new DesktopHostPosition(112, 112));
Check("right", DesktopHostSelectionPolicy.FindAdjacentItem(navigableDesktopItems, "center", DesktopHostNavigationDirection.Right)!.FullPath,
    "move desktop keyboard focus to the nearest item on the right");
Check("below", DesktopHostSelectionPolicy.FindAdjacentItem(navigableDesktopItems, "center", DesktopHostNavigationDirection.Down)!.FullPath,
    "move desktop keyboard focus to the nearest item below");
Check("center", DesktopHostSelectionPolicy.FindAdjacentItem(navigableDesktopItems, "right", DesktopHostNavigationDirection.Left)!.FullPath,
    "move desktop keyboard focus back to the left");
Check<DesktopHostItem?>(null, DesktopHostSelectionPolicy.FindAdjacentItem(navigableDesktopItems, "missing", DesktopHostNavigationDirection.Left),
    "leave desktop keyboard focus unchanged when its item disappeared");
var marqueeRectangle = DesktopHostMarqueePolicy.CreateRectangle(new Point(20, 20), new Point(0, 0));
Check(new Rect(0, 0, 20, 20), marqueeRectangle, "normalize a desktop marquee dragged from bottom-right to top-left");
var marqueeItems = new[]
{
    new DesktopHostMarqueeItem("a", new Rect(2, 2, 8, 8)),
    new DesktopHostMarqueeItem("b", new Rect(12, 12, 8, 8)),
    new DesktopHostMarqueeItem("c", new Rect(30, 30, 8, 8))
};
var marqueeSelection = DesktopHostMarqueePolicy.ResolveSelection(marqueeItems, marqueeRectangle, [], toggleIntersectedItems: false);
Check("a,b", string.Join(',', marqueeSelection.OrderBy(path => path)), "select only desktop icons intersecting a marquee rectangle");
marqueeSelection = DesktopHostMarqueePolicy.ResolveSelection(marqueeItems, marqueeRectangle, ["a", "c"], toggleIntersectedItems: true);
Check("b,c", string.Join(',', marqueeSelection.OrderBy(path => path)), "toggle intersected desktop icons with Ctrl-marquee while preserving outside items");
Check("a,c", string.Join(',', DesktopHostMarqueePolicy.ResolveSelection(marqueeItems, Rect.Empty, ["a", "c"], toggleIntersectedItems: true).OrderBy(path => path)),
    "preserve the selection when the marquee rectangle has no area");
selectableDesktopItems[0].IsSelected = true;
selectableDesktopItems[2].IsSelected = true;
desktopSelection = DesktopHostSelectionPolicy.PreserveSelectionForContextMenu(selectableDesktopItems, selectableDesktopItems[2].FullPath);
Check("a,c", string.Join(',', selectableDesktopItems.Where(item => desktopSelection.Paths.Contains(item.FullPath)).Select(item => item.Name)), "preserve a multi-item selection when opening a selected desktop icon's context menu");
desktopSelection = DesktopHostSelectionPolicy.PreserveSelectionForContextMenu(selectableDesktopItems, selectableDesktopItems[1].FullPath);
Check("b", string.Join(',', selectableDesktopItems.Where(item => desktopSelection.Paths.Contains(item.FullPath)).Select(item => item.Name)), "select only an unselected desktop icon for its context menu");
var draggedFilePath = Path.Combine(userDesktopRoot, "dragged.txt");
File.WriteAllText(draggedFilePath, "drag me");
var draggedFileItem = new DesktopHostItem("dragged.txt", draggedFilePath, false) { IsSelected = true };
var draggedNamespaceItem = new DesktopHostItem("Libraries", "::{031E4825-7B94-4DC3-B131-E946B44C8DD5}", true, isShellNamespace: true) { IsSelected = true };
var namespaceDrag = DesktopHostDragPolicy.Resolve([draggedNamespaceItem], draggedNamespaceItem);
Check(draggedNamespaceItem.FullPath, string.Join(',', namespaceDrag.ItemPaths), "allow an individual virtual desktop item to start an internal position drag");
Check(0, namespaceDrag.FileDropPaths.Count, "do not expose virtual namespace objects as filesystem drag-out paths");
var mixedDesktopDrag = DesktopHostDragPolicy.Resolve([draggedFileItem, draggedNamespaceItem], draggedNamespaceItem);
Check($"{draggedNamespaceItem.FullPath},{draggedFileItem.FullPath}", string.Join(',', mixedDesktopDrag.ItemPaths), "move mixed selected desktop items with the dragged item as the group anchor");
Check(draggedFilePath, string.Join(',', mixedDesktopDrag.FileDropPaths), "include only real filesystem entries in a mixed desktop file-drop payload");
Check(0, DesktopHostDragPolicy.Resolve(selectableDesktopItems, new DesktopHostItem("missing", "missing", false)).ItemPaths.Count, "ignore a stale desktop drag anchor");
selectableDesktopItems[0].SetPosition(new DesktopHostPosition(10, 20));
selectableDesktopItems[1].SetPosition(new DesktopHostPosition(110, 20));
var translatedDesktopSelection = DesktopHostLayoutStore.TranslateSelection(selectableDesktopItems.Take(2), selectableDesktopItems[0].FullPath,
    new DesktopHostPosition(210, 160), 600, 400);
Check(new DesktopHostPosition(210, 160), translatedDesktopSelection[selectableDesktopItems[0].FullPath], "move a multi-selected desktop group to the dragged anchor position");
Check(new DesktopHostPosition(310, 160), translatedDesktopSelection[selectableDesktopItems[1].FullPath], "preserve spacing while moving selected desktop items together");
Check(new DesktopHostPosition(500, 288), DesktopHostLayoutStore.TranslateSelection(selectableDesktopItems.Take(2), selectableDesktopItems[0].FullPath,
    new DesktopHostPosition(900, 900), 600, 400)[selectableDesktopItems[0].FullPath], "clamp a dragged selection anchor to the display bounds");
Check(new TaskbarBounds(-1920, -200, 3840, 1280), DesktopHostDisplayLayoutPolicy.CalculateVirtualBounds([
    new TaskbarDisplay("DISPLAY1", 0, 0, 1920, 1080, true),
    new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false)
]), "span the full Windows virtual desktop, including displays left and above the primary monitor");
Check(new TaskbarBounds(-1920, 0, 3840, 1080), DesktopHostDisplayLayoutPolicy.CalculateVirtualBounds([
    new TaskbarDisplay("DISPLAY1", 0, 0, 1920, 1080, true),
    new TaskbarDisplay("DISPLAY2", -1920, 0, 1920, 1080, false)
]), "span horizontally arranged monitors for desktop icon placement");
var desktopViewports = DesktopHostDisplayLayoutPolicy.CreateMonitorViewports([
    new TaskbarDisplay("DISPLAY1", 0, 0, 1920, 1080, true),
    new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false)
], new TaskbarBounds(-1920, -200, 3840, 1280), 3820, 1260, 1, 1);
Check(2, desktopViewports.Count, "create independent desktop layout regions for every display");
Check("DISPLAY2", DesktopHostDisplayLayoutPolicy.FindNearestMonitor(desktopViewports,
    new DesktopHostPosition(desktopViewports.Single(viewport => viewport.DeviceName == "DISPLAY2").Left + 20,
        desktopViewports.Single(viewport => viewport.DeviceName == "DISPLAY2").Top + 20)).DeviceName,
    "assign an icon position to its monitor viewport");
Throws<ArgumentException>(() => DesktopHostDisplayLayoutPolicy.CalculateVirtualBounds([]), "reject an empty connected-display set for the desktop host");
var primaryWallpaperDisplay = new TaskbarDisplay("DISPLAY1", 0, 0, 1920, 1080, true);
var secondaryWallpaperDisplay = new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false);
Check("DISPLAY2", DesktopWallpaperPresentationPolicy.FindDisplay([primaryWallpaperDisplay, secondaryWallpaperDisplay], -1920, -200, 0, 880)?.DeviceName,
    "match Windows wallpaper monitor rectangles to secondary displays with negative origins");
Check<TaskbarDisplay?>(null, DesktopWallpaperPresentationPolicy.FindDisplay([primaryWallpaperDisplay], 1920, 0, 3840, 1080),
    "skip wallpaper monitors that are no longer connected");
Check(new DesktopWallpaperPresentation(Stretch.None, false, false), DesktopWallpaperPresentationPolicy.Resolve(DesktopWallpaperPosition.Center),
    "center desktop wallpaper without resizing it");
Check(new DesktopWallpaperPresentation(Stretch.None, true, false), DesktopWallpaperPresentationPolicy.Resolve(DesktopWallpaperPosition.Tile),
    "tile desktop wallpaper across each monitor");
Check(new DesktopWallpaperPresentation(Stretch.Fill, false, false), DesktopWallpaperPresentationPolicy.Resolve(DesktopWallpaperPosition.Stretch),
    "stretch desktop wallpaper to each monitor's bounds");
Check(new DesktopWallpaperPresentation(Stretch.Uniform, false, false), DesktopWallpaperPresentationPolicy.Resolve(DesktopWallpaperPosition.Fit),
    "fit desktop wallpaper without cropping its aspect ratio");
Check(new DesktopWallpaperPresentation(Stretch.UniformToFill, false, false), DesktopWallpaperPresentationPolicy.Resolve(DesktopWallpaperPosition.Fill),
    "fill each monitor with its assigned wallpaper while preserving aspect ratio");
Check(new DesktopWallpaperPresentation(Stretch.Fill, false, true), DesktopWallpaperPresentationPolicy.Resolve(DesktopWallpaperPosition.Span),
    "span a single wallpaper image across the virtual desktop");
var wallpaperTestDisplays = TaskbarDisplayService.Enumerate();
var wallpaperSnapshot = DesktopWallpaperService.LoadCurrent(wallpaperTestDisplays);
if (wallpaperSnapshot is not null)
{
    CheckTrue(wallpaperSnapshot.Monitors.Count > 0, "read at least one current wallpaper assignment from Windows");
    CheckTrue(wallpaperSnapshot.Monitors.All(wallpaper => wallpaperTestDisplays.Any(display => display.DeviceName == wallpaper.DeviceName)),
        "map every Windows wallpaper assignment to a connected display");
}
Check(true, DesktopHostRefreshPolicy.ShouldRefresh(WatcherChangeTypes.Created), "refresh the desktop when a new item is created");
Check(true, DesktopHostRefreshPolicy.ShouldRefresh(WatcherChangeTypes.Renamed), "refresh the desktop when an item is renamed");
Check(false, DesktopHostRefreshPolicy.ShouldRefresh(WatcherChangeTypes.All), "ignore unknown desktop watcher event types");
Check(true, DesktopHostRefreshPolicy.ShouldRefreshShellEvent(0x00000002), "refresh desktop namespace items on Shell create notifications");
Check(true, DesktopHostRefreshPolicy.ShouldRefreshShellEvent(0x00002000), "refresh desktop namespace items when Shell items update");
Check(true, DesktopHostRefreshPolicy.ShouldRefreshShellEvent(0x08000000), "refresh desktop namespace icons after Shell associations change");
Check(true, DesktopHostRefreshPolicy.ShouldRefreshShellEvent(0x00000080), "refresh desktop items when a drive leaves the Shell namespace");
Check(false, DesktopHostRefreshPolicy.ShouldRefreshShellEvent(0x04000000), "ignore unrelated extended Shell notification events");
File.SetAttributes(Path.Combine(userDesktopRoot, "hidden.txt"), FileAttributes.Normal);
Directory.Delete(desktopHostTestRoot, recursive: true);
var navigationTestRoot = Path.Combine(Path.GetTempPath(), $"desktop-tuner-navigation-{Guid.NewGuid():N}");
try
{
    var nestedPath = Path.Combine(navigationTestRoot, "Alpha");
    var nestedBreadcrumbChild = Path.Combine(nestedPath, "Child");
    var hiddenPath = Path.Combine(navigationTestRoot, "Hidden");
    Directory.CreateDirectory(nestedPath);
    Directory.CreateDirectory(nestedBreadcrumbChild);
    Directory.CreateDirectory(hiddenPath);
    File.SetAttributes(hiddenPath, FileAttributes.Hidden);
    Check("Alpha", string.Join(',', ExplorerNavigationService.ReadDirectories(navigationTestRoot, showHiddenItems: false).Directories.Select(directory => directory.Name)), "hide hidden folders in Explorer navigation when Windows hidden items are off");
    Check("Alpha,Hidden", string.Join(',', ExplorerNavigationService.ReadDirectories(navigationTestRoot, showHiddenItems: true).Directories.Select(directory => directory.Name)), "include hidden folders in Explorer navigation when enabled");
    Check("Child", string.Join(',', ExplorerNavigationService.ReadDirectories(nestedPath, showHiddenItems: false).Directories.Select(directory => directory.Name)), "load breadcrumb dropdown children from the selected path segment");
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
Check(false, SystemBackdropService.TryClearSystemBackdrop(IntPtr.Zero), "leave unsupported window handles unchanged when clearing a backdrop");
Check(false, SystemBackdropService.TryApplySmallRoundedCorners(IntPtr.Zero), "leave unsupported menu handles with system-default corners");
Check(false, new DesktopPreferences(TaskbarEdge.Bottom).TaskbarOnAllDisplays, "preserve the primary-display behavior for older preference data");
Check(false, new DesktopPreferences(TaskbarEdge.Bottom).ReplaceNativeTaskbar, "leave native taskbar replacement disabled by default");
Check(TaskbarSearchStyle.Button, new DesktopPreferences(TaskbarEdge.Bottom).TaskbarSearchStyle, "default the taskbar search control to a button");
Check(0, (int)TaskbarSearchStyle.Button, "preserve the saved numeric value for the taskbar search button");
Check(1, (int)TaskbarSearchStyle.Box, "preserve the saved numeric value for the taskbar search box");
Check(true, Enum.IsDefined(TaskbarSearchStyle.None), "support hiding the custom taskbar search control");
Check(true, Enum.IsDefined(TaskbarSearchStyle.IconAndLabel), "support labeling the custom taskbar search control");
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
Check("Details,List,SmallIcons,MediumIcons,LargeIcons,Tiles", string.Join(',', ShellNamespaceViewModeCatalog.Options.Select(option => option.Mode)), "offer the six familiar Explorer layouts in the Shell namespace browser");
Check("ShellNamespaceMediumIconTemplate", ShellNamespaceViewModeCatalog.Get(ExplorerViewMode.MediumIcons).ItemTemplateKey, "map medium Shell namespace icons to their tile template");
CheckTrue(ShellNamespaceViewModeCatalog.Get(ExplorerViewMode.LargeIcons).WrapItems, "wrap large Shell namespace icons to the available viewport");
CheckTrue(ShellNamespaceViewModeCatalog.Get(ExplorerViewMode.Tiles).WrapItems, "wrap Shell namespace tiles to the available viewport");
CheckTrue(NativeTaskbarWatchdog.IsWatchdogInvocation(["--taskbar-watchdog", "123", "snapshot.json"]), "recognize the taskbar recovery process entry point");
CheckTrue(ShellHostLaunchPolicy.IsShellHostInvocation(["--SHELL-HOST"]), "recognize Shell Launcher mode without depending on argument casing");
CheckTrue(ShellHostLaunchPolicy.IsShellHostWorkerInvocation(["--SHELL-HOST-WORKER"]), "recognize the internal shell-host worker without depending on argument casing");
Check(true, ShellHostLaunchPolicy.ShouldRunShellHostSupervisor(["--shell-host"], false), "supervise an explicit Shell Launcher entry point");
Check(false, ShellHostLaunchPolicy.ShouldRunShellHostSupervisor(["--shell-host-worker"], true), "avoid recursively supervising the shell-host worker");
Check(true, ShellHostLaunchPolicy.ShouldRunShellHostSupervisor([], true), "supervise the per-user alternate-shell policy entry point");
Check(false, ShellHostLaunchPolicy.ShouldRunShellHostSupervisor(["--shell-overlay"], false), "keep shell overlay startup outside the logon-shell supervisor");
CheckTrue(CustomShellPolicy.TargetsExecutable(@"C:\Users\test\Desktop Tuner\DesktopTuner.exe", @"C:\Users\test\Desktop Tuner\DesktopTuner.exe"), "recognize a per-user custom-shell policy that targets the current executable");
CheckTrue(CustomShellPolicy.TargetsExecutable(@"""C:\Users\test\Desktop Tuner\DesktopTuner.exe"" --shell-host", @"C:\Users\test\Desktop Tuner\DesktopTuner.exe"), "recognize a quoted custom-shell command with arguments");
Check(false, CustomShellPolicy.TargetsExecutable(@"C:\Windows\explorer.exe", @"C:\Users\test\DesktopTuner.exe"), "keep Explorer's custom-shell fallback from activating Desktop Tuner shell-host mode");
Check(false, CustomShellPolicy.TargetsExecutable(null, @"C:\Users\test\DesktopTuner.exe"), "leave shell-host mode disabled when the per-user custom-shell policy is absent");
Check(true, CustomShellPolicy.CanConfigure(null, @"C:\Users\test\DesktopTuner.exe"), "allow custom-shell setup when no per-user shell is configured");
Check(false, CustomShellPolicy.CanConfigure(null, null), "require a current executable path before custom-shell setup");
Check(false, CustomShellPolicy.CanConfigure(@"C:\Windows\explorer.exe", @"C:\Users\test\DesktopTuner.exe"), "preserve another custom shell rather than overwrite its setting");
Check(true, CustomShellPolicy.IsSupportedEdition("Professional"), "allow custom-shell policy controls on Pro editions");
Check(true, CustomShellPolicy.IsSupportedEdition("Enterprise"), "allow custom-shell policy controls on Enterprise editions");
Check(true, CustomShellPolicy.IsSupportedEdition("Education"), "allow custom-shell policy controls on Education editions");
Check(true, CustomShellPolicy.IsSupportedEdition("IoTEnterpriseS"), "allow custom-shell policy controls on IoT Enterprise editions");
Check(false, CustomShellPolicy.IsSupportedEdition("Core"), "leave custom-shell policy controls unavailable on Home editions");
Check(true, ShellLauncherService.IsSupportedEdition("EnterpriseS"), "allow Shell Launcher controls on Enterprise editions");
Check(true, ShellLauncherService.IsSupportedEdition("Education"), "allow Shell Launcher controls on Education editions");
Check(true, ShellLauncherService.IsSupportedEdition("IoTEnterpriseS"), "allow Shell Launcher controls on IoT Enterprise editions");
Check(false, ShellLauncherService.IsSupportedEdition("Professional"), "keep Shell Launcher controls unavailable on Pro editions");
Check(false, ShellLauncherService.IsSupportedEdition("Core"), "keep Shell Launcher controls unavailable on Home editions");
Check(@"""C:\Program Files\Desktop Tuner\DesktopTuner.exe"" --shell-host", ShellLauncherService.BuildShellCommand(@"C:\Program Files\Desktop Tuner\DesktopTuner.exe"), "quote the executable in the current-user Shell Launcher command");
Check(true, CustomShellPolicy.ShouldRestartHost(-1, 0), "retry the custom shell once after a failed process exit");
Check(false, CustomShellPolicy.ShouldRestartHost(0, 0), "return to Explorer after a normal custom-shell exit");
Check(false, CustomShellPolicy.ShouldRestartHost(-1, CustomShellPolicy.MaximumHostRestarts), "stop retrying and recover to Explorer after the restart limit");
Check(true, CustomShellPolicy.ShouldRestartHost(ShellHostLaunchPolicy.RequestedRestartExitCode, CustomShellPolicy.MaximumHostRestarts), "honor a manual shell restart after automatic retries are exhausted");
Check(TimeSpan.FromSeconds(60), CustomShellPolicy.HostStartupReadinessTimeout, "bound alternate-shell startup while waiting for the desktop readiness handshake");
Check(TimeSpan.FromSeconds(10), CustomShellPolicy.HostHeartbeatInterval, "check custom-shell UI health on a bounded interval");
Check(TimeSpan.FromSeconds(45), CustomShellPolicy.HostHeartbeatTimeout, "detect a custom-shell UI hang after missed dispatcher heartbeats");
Check(true, CustomShellPolicy.HostHeartbeatTimeout > CustomShellPolicy.HostHeartbeatInterval * 2, "allow delayed UI heartbeats before recovering the custom shell");
Check(TimeSpan.FromSeconds(65), ShellHostLaunchPolicy.PendingInvocationForwardTimeout, "hold a second shell invocation long enough for the replacement worker to become ready");
Check(true, CustomShellPolicy.ShouldRestartHostAfterStartupTimeout(0), "retry the alternate shell once after a startup readiness timeout");
Check(false, CustomShellPolicy.ShouldRestartHostAfterStartupTimeout(CustomShellPolicy.MaximumHostRestarts), "recover to Explorer after the alternate shell startup timeout retry is exhausted");
Check(false, CustomShellPolicy.ShouldDisablePolicyAfterHostFailure(-1, 0), "keep the custom-shell policy during its one recovery retry");
Check(true, CustomShellPolicy.ShouldDisablePolicyAfterHostFailure(-1, CustomShellPolicy.MaximumHostRestarts), "disable the failing per-user custom-shell policy after recovery retries are exhausted");
Check(false, CustomShellPolicy.ShouldDisablePolicyAfterHostFailure(0, CustomShellPolicy.MaximumHostRestarts), "preserve the custom-shell policy after a normal user exit");
Check(false, CustomShellPolicy.ShouldDisablePolicyAfterHostFailure(ShellHostLaunchPolicy.RequestedRestartExitCode, CustomShellPolicy.MaximumHostRestarts), "preserve the custom-shell policy during a manual shell restart");
Check(@"""C:\Program Files\Desktop Tuner\DesktopTuner.exe""", CustomShellPolicy.FormatExecutableCommand(@"C:\Program Files\Desktop Tuner\DesktopTuner.exe"), "quote custom-shell executable paths so spaces are handled by Winlogon");
CheckTrue(ShellHostLaunchPolicy.IsShellOverlayInvocation(["--SHELL-OVERLAY"]), "recognize all-edition shell overlay mode without depending on argument casing");
Check(true, ShellHostLaunchPolicy.ShouldStartTaskbar(true, false), "start the companion taskbar in shell-host mode regardless of sign-in preferences");
Check(true, ShellHostLaunchPolicy.ShouldStartTaskbar(false, true, false), "start the companion taskbar in shell overlay mode regardless of sign-in preferences");
Check(true, ShellHostLaunchPolicy.ShouldProvidePowerUserMenu(false, true), "route Win+X to the custom Power User menu in all-edition shell overlay mode");
Check(true, ShellHostLaunchPolicy.ShouldProvidePowerUserMenu(true, false), "route Win+X to the custom Power User menu when Explorer is absent");
Check(false, ShellHostLaunchPolicy.ShouldProvidePowerUserMenu(false, false), "preserve the native Power User menu outside replacement modes");
Check(true, ShellHostLaunchPolicy.ShouldProvideReplacementRunDialog(false, true), "route Win+R to the companion Run dialog in all-edition shell overlay mode");
Check(true, ShellHostLaunchPolicy.ShouldProvideReplacementRunDialog(true, false), "route Win+R to the companion Run dialog when Explorer is absent");
Check(false, ShellHostLaunchPolicy.ShouldProvideReplacementRunDialog(false, false), "preserve the native Run dialog outside replacement modes");
Check(true, ShellHostLaunchPolicy.ShouldManageReplacementDesktop(false, true), "manage replacement desktop windows in all-edition shell overlay mode");
Check(true, ShellHostLaunchPolicy.ShouldManageReplacementDesktop(true, false), "manage replacement desktop windows when Explorer is absent");
Check(false, ShellHostLaunchPolicy.ShouldManageReplacementDesktop(false, false), "preserve native desktop window management outside replacement modes");
Check(true, ShellHostLaunchPolicy.ShouldCoverAllDisplays(true, false), "cover every display in shell-host mode regardless of overlay preferences");
Check(true, ShellHostLaunchPolicy.ShouldCoverAllDisplays(false, false, true), "cover every display in shell overlay mode regardless of display preferences");
Check(false, ShellHostLaunchPolicy.ShouldHideNativeTaskbar(true, true), "avoid trying to hide an Explorer taskbar when running as the logon shell");
Check(false, ShellHostLaunchPolicy.ShouldHideNativeTaskbar(false, true, false), "keep Explorer taskbars active behind all-edition shell overlay mode");
Check(false, ShellHostLaunchPolicy.ShouldHideNativeTaskbar(false, true, true), "keep the native notification area active regardless of the saved replacement preference in shell overlay mode");
Check(true, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(true, true), "integrate the native notification area in shell overlay mode without changing the saved replacement preference");
Check(false, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(false, true), "leave native tray integration disabled in normal replacement mode");
Check(false, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(true, false, false), "disable native tray integration in shell-host mode so the custom taskbar can reserve its own work area");
Check(true, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(true, false, false, nativeTrayAvailable: true), "reuse an already-running native notification area in shell-host mode");
Check(true, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(true, false, true, nativeTrayAvailable: true), "reuse an existing native notification area in shell-host mode regardless of overlay preference");
Check(false, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(true, false, false, nativeTrayAvailable: false), "keep custom system controls when Explorer provides no notification area");
Check(true, ShellHostLaunchPolicy.ShouldReconcileNativeTrayIntegration(true, false, nativeTrayAvailable: true), "detect an Explorer notification area appearing during shell-host mode");
Check(true, ShellHostLaunchPolicy.ShouldReconcileNativeTrayIntegration(true, true, nativeTrayAvailable: false), "detect an Explorer notification area disappearing during shell-host mode");
Check(false, ShellHostLaunchPolicy.ShouldReconcileNativeTrayIntegration(false, false, nativeTrayAvailable: true), "limit live notification-area reconciliation to shell-host mode");
Check(false, ShellHostLaunchPolicy.ShouldReserveShellHostWorkArea(true, nativeTrayIntegrated: true), "avoid reserving the work area twice when the native taskbar supplies it");
Check(true, ShellHostLaunchPolicy.ShouldReserveShellHostWorkArea(true, nativeTrayIntegrated: false), "reserve the work area when shell-host mode has no native taskbar");
Check(false, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(false, false, true), "keep native tray integration disabled in regular taskbar replacement mode");
Check(true, ShellHostLaunchPolicy.ShouldUseNativeTrayIntegration(false, false), "integrate the native notification area in normal overlay mode");
Check(true, ShellHostLaunchPolicy.ShouldReplaceExplorerShortcut(true, false), "route Win+E to companion Explorer in shell replacement mode regardless of the saved preference");
Check(true, ShellHostLaunchPolicy.ShouldReplaceExplorerShortcut(false, true), "honor opt-in Win+E routing outside shell replacement mode");
Check(false, ShellHostLaunchPolicy.ShouldReplaceExplorerShortcut(false, false), "preserve native Win+E behavior when companion Explorer routing is disabled");
Check(true, ShellHostLaunchPolicy.ShouldReplaceWindowsKey(true, false), "route the bare Windows key to the companion Start menu in shell replacement mode");
Check(true, StartMenuOpenModePolicy.ShouldShowOverview(string.Empty, false), "show the Start overview by default");
Check(false, StartMenuOpenModePolicy.ShouldShowOverview(string.Empty, true), "open Start directly to All apps when configured");
Check(false, StartMenuOpenModePolicy.ShouldShowOverview("query", false), "hide the Start overview while searching");
Check(true, ShellHostLaunchPolicy.ShouldReplaceWindowsKey(false, true), "honor opt-in bare Windows-key routing outside shell replacement mode");
Check(false, ShellHostLaunchPolicy.ShouldReplaceWindowsKey(false, false), "preserve native bare Windows-key behavior outside shell replacement mode");
Check("Exit shell replacement and start Explorer", ShellHostLaunchPolicy.GetExitLabel(true), "label shell-host exit as Explorer recovery");
Check("Exit Desktop Tuner", ShellHostLaunchPolicy.GetExitLabel(false), "keep the normal app exit label");
Check("Restart shell replacement", ShellHostLaunchPolicy.GetRestartLabel(true), "label the bounded shell-host restart command");
Check(0xD7A, ShellHostLaunchPolicy.RequestedRestartExitCode, "use a distinct supervisor retry code for a requested shell restart");
Check(false, ShellHostLaunchPolicy.ShouldAllowTaskbarClose(true), "keep replacement taskbars available throughout shell-host mode");
Check(true, ShellHostLaunchPolicy.ShouldAllowTaskbarClose(false), "allow taskbar closing outside shell-host mode");
Check(false, ShellHostLaunchPolicy.ShouldShowRestartExplorerCommand(true), "hide the Explorer restart command while Explorer is absent in shell-host mode");
Check(true, ShellHostLaunchPolicy.ShouldShowRestartExplorerCommand(false), "keep the Explorer restart command in normal taskbar modes");
Check(true, ShellHostLaunchPolicy.ShouldRouteDesktopFoldersToCompanionExplorer(true), "keep desktop folder navigation inside the replacement shell");
Check(false, ShellHostLaunchPolicy.ShouldRouteDesktopFoldersToCompanionExplorer(false), "preserve normal desktop-host folder activation outside replacement shell mode");
Check(true, ShellHostLaunchPolicy.ShouldRouteStartMenuLocationToCompanionExplorer(true, true, false), "route filesystem-backed Start places through companion Explorer in replacement mode");
Check(true, ShellHostLaunchPolicy.ShouldRouteStartMenuLocationToCompanionExplorer(true, false, true), "route supported namespace Start places through companion Explorer in replacement mode");
Check(false, ShellHostLaunchPolicy.ShouldRouteStartMenuLocationToCompanionExplorer(false, true, true), "preserve native Start place handlers outside replacement mode");
Check(false, ShellHostLaunchPolicy.ShouldRouteStartMenuLocationToCompanionExplorer(true, false, false), "leave unsupported Start locations on their existing handler");
Check(true, ShellHostLaunchPolicy.ShouldRoutePinnedShellLocationToCompanionExplorer(true, true), "route pinned Shell namespace locations through companion Explorer in shell-host mode");
Check(false, ShellHostLaunchPolicy.ShouldRoutePinnedShellLocationToCompanionExplorer(false, true), "preserve native pinned Shell location launch outside shell-host mode");
Check(false, ShellHostLaunchPolicy.ShouldRoutePinnedShellLocationToCompanionExplorer(true, false), "leave ordinary pinned app launch outside Shell namespace routing");
Check(true, ShellHostLaunchPolicy.ShouldRoutePinnedDirectoryToCompanionExplorer(true, true), "route pinned folders through companion Explorer in shell-host mode");
Check(false, ShellHostLaunchPolicy.ShouldRoutePinnedDirectoryToCompanionExplorer(false, true), "preserve native pinned folder launch outside shell-host mode");
Check(true, DesktopShellNamespaceCatalog.IsCompanionExplorerLocation("shell:MyComputerFolder"), "route the This PC desktop namespace item to companion Explorer");
Check(true, DesktopShellNamespaceCatalog.IsCompanionExplorerLocation("shell:RecycleBinFolder"), "route the Recycle Bin desktop namespace item to companion Explorer");
Check(false, DesktopShellNamespaceCatalog.IsCompanionExplorerLocation("shell:PrintersFolder"), "keep This PC and Recycle Bin on their specialized companion Explorer views");
Check(true, DesktopShellNamespaceCatalog.IsShellNamespaceLocation("shell:NetworkPlacesFolder"), "recognize the Network namespace for the replacement-shell browser");
Check(true, DesktopShellNamespaceCatalog.IsShellNamespaceLocation("shell:ControlPanelFolder"), "recognize Control Panel for the replacement-shell browser");
Check(true, DesktopShellNamespaceCatalog.IsShellNamespaceLocation("::{645FF040-5081-101B-9F08-00AA002F954E}"), "recognize GUID-parsing-name shell locations");
Check(false, DesktopShellNamespaceCatalog.IsShellNamespaceLocation("control.exe"), "reject executable commands as Shell namespace locations");
Check(false, ShellHostLaunchPolicy.ShouldHideNativeTaskbar(true, false, true), "avoid hiding taskbars when Explorer is not the logon shell");
Check(true, ShellHostLaunchPolicy.ShouldHideNativeTaskbar(false, true), "preserve the user's native taskbar replacement setting in normal mode");
Check(true, ShellHostLaunchPolicy.ShouldLaunchExplorerOnShellHostExit(true, false), "start Explorer when a Shell Launcher host exits normally");
Check(false, ShellHostLaunchPolicy.ShouldLaunchExplorerOnShellHostExit(true, true), "let the per-user custom-shell supervisor restore Explorer itself");
Check(false, ShellHostLaunchPolicy.ShouldLaunchExplorerOnShellHostExit(false, false), "keep normal overlay shutdown separate from Shell Launcher recovery");
Check(true, ShellHostLaunchPolicy.ShouldRestoreExplorerAfterShellHostExit(true, false, 0), "restore Explorer after a successful Shell Launcher exit");
Check(false, ShellHostLaunchPolicy.ShouldRestoreExplorerAfterShellHostExit(true, true, 0), "avoid launching Explorer while Windows is signing out");
Check(false, ShellHostLaunchPolicy.ShouldRestoreExplorerAfterShellHostExit(true, false, 1), "let Shell Launcher restart the host after an abnormal exit");
CheckTrue(NativeTaskbarWatchdog.TryReadInvocation(["--taskbar-watchdog", "123", "snapshot.json"], out var watchdogOwner, out var watchdogSnapshot), "parse taskbar recovery process arguments");
Check((123, "snapshot.json"), (watchdogOwner, watchdogSnapshot), "recover the watchdog owner and snapshot path");
CheckTrue(TaskbarSnapshotOwnerPolicy.IsSnapshotOwner(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow), "match a live process start to its newer taskbar snapshot");
Check(false, TaskbarSnapshotOwnerPolicy.IsSnapshotOwner(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5)), "reject a recycled process id started after the taskbar snapshot");
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom), false), "bottom, standard");
Check(new TaskbarBounds(0, 0, 1920, 46), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Top, TaskbarSize.Small), false), "top, small");
Check(new TaskbarBounds(0, 0, 204, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Large), false), "left, large");
Check(new TaskbarBounds(1744, 0, 176, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Right), false), "right, standard");
Check(new TaskbarBounds(1916, 0, 4, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Right), true), "right, collapsed");
Check(new TaskbarBounds(0, 1076, 1920, 4), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom), true), "bottom, collapsed");
CheckTrue(TaskbarAppBarPolicy.ShouldRegister(TaskbarStyle.EdgeToEdge), "reserve Windows work area for an edge-docked replacement taskbar");
CheckTrue(!TaskbarAppBarPolicy.ShouldRegister(TaskbarStyle.Floating), "keep floating replacement taskbars out of edge-docked appbar registration");
Check(true, TaskbarAppBarPolicy.CanUseAsReplacement(TaskbarStyle.Floating, false, false), "allow floating taskbar replacement without an edge work-area reservation");
Check(true, TaskbarAppBarPolicy.CanUseAsReplacement(TaskbarStyle.EdgeToEdge, true, true), "allow edge taskbar replacement after Windows approves its work-area reservation");
Check(false, TaskbarAppBarPolicy.CanUseAsReplacement(TaskbarStyle.EdgeToEdge, false, false), "reject edge taskbar replacement when Windows refuses AppBar registration");
Check(false, TaskbarAppBarPolicy.CanUseAsReplacement(TaskbarStyle.EdgeToEdge, true, false), "reject edge taskbar replacement when Windows does not approve its work-area position");
CheckTrue(TaskbarAppBarPolicy.ShouldRegisterAutoHide(true, true), "register Windows auto-hide for an enabled replacement appbar");
Check(false, TaskbarAppBarPolicy.ShouldRegisterAutoHide(false, true), "avoid registering Windows auto-hide for an unregistered overlay taskbar");
Check(false, TaskbarAppBarPolicy.ShouldRegisterAutoHide(true, false), "release Windows auto-hide when the replacement preference is disabled");
var appBarDisplay = new TaskbarDisplay("APPBAR", 0, 0, 1920, 1080, true);
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarAppBarPolicy.ProposeBounds(appBarDisplay, TaskbarEdge.Bottom, new TaskbarBounds(0, 1026, 1920, 54)), "propose the full physical monitor edge for a bottom appbar");
Check(new TaskbarBounds(0, 0, 176, 1080), TaskbarAppBarPolicy.ProposeBounds(appBarDisplay, TaskbarEdge.Left, new TaskbarBounds(0, 0, 176, 1080)), "propose the full physical monitor edge for a left appbar");
Check(new TaskbarBounds(-1920, -200, 176, 1080), TaskbarAppBarPolicy.ProposeBounds(new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false), TaskbarEdge.Left, new(-1920, -200, 176, 1080)), "keep a left appbar on a secondary monitor with negative screen coordinates");
Check(new TaskbarBounds(1500, 972, 420, 54), TaskbarAppBarPolicy.PreserveThickness(TaskbarEdge.Bottom, new(1500, 976, 420, 50), new(0, 1026, 1920, 54)), "preserve replacement taskbar thickness after Windows approves a bottom appbar slot");
Check(new TaskbarBounds(1744, 200, 176, 600), TaskbarAppBarPolicy.PreserveThickness(TaskbarEdge.Right, new(1700, 200, 220, 600), new(1744, 0, 176, 1080)), "preserve replacement taskbar thickness after Windows approves a right appbar slot");
Check(new TaskbarBounds(0, 120, 1920, 46), TaskbarAppBarPolicy.PreserveThickness(TaskbarEdge.Top, new(0, 120, 1920, 42), new(0, 0, 1920, 46)), "preserve replacement taskbar thickness after Windows approves a top appbar slot");
Check(new TaskbarBounds(160, 0, 176, 1080), TaskbarAppBarPolicy.PreserveThickness(TaskbarEdge.Left, new(160, 0, 150, 1080), new(0, 0, 176, 1080)), "preserve replacement taskbar thickness after Windows approves a left appbar slot");
Check(new TaskbarBounds(288, 1014, 1344, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Floating), false), "center a floating bar with a bottom screen inset");
Check(new TaskbarBounds(12, 162, 176, 756), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Floating), false), "shorten and inset a floating vertical bar");
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Segmented), false), "keep segmented taskbar regions across the selected screen edge");
Check(new TaskbarBounds(0, 1026, 1920, 54), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Bottom, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.DockLike), false), "keep the dock-style taskbar host aligned to the selected screen edge");
Check(new TaskbarBounds(0, 0, 176, 1080), TaskbarLayoutCalculator.Calculate(1920, 1080, new(TaskbarEdge.Left, TaskbarSize.Standard, TaskbarLayout: TaskbarStyle.Segmented), false), "retain full-height geometry for vertical segmented regions");
var trayDisplay = new TaskbarDisplay("DISPLAY1", 0, 0, 1920, 1080, true);
var nativeTray = new TaskbarBounds(1500, 1030, 420, 50);
var secondaryDisplay = new TaskbarDisplay("DISPLAY2", -1920, -200, 1920, 1080, false, 1.5, 1.5);
Check(new TaskbarBounds(0, 1026, 1500, 54), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), nativeTray), "leave the native notification area uncovered on an edge-to-edge bar");
Check(new TaskbarBounds(0, 1030, 1920, 50), NativeTaskbarTrayService.SelectBestTrayBounds([new TaskbarBounds(0, 1030, 1920, 50), nativeTray], trayDisplay), "choose the native notification area with the greatest display overlap");
var nativeTrayCandidates = new NativeTaskbarTrayService.TrayCandidate[]
{
    new(new TaskbarBounds(0, 1030, 100, 50), (nint)101, (nint)201),
    new(nativeTray, (nint)102, (nint)202)
};
Check((nint)102, NativeTaskbarTrayService.SelectBestTrayCandidate(nativeTrayCandidates, trayDisplay)?.TaskbarWindow, "focus the taskbar window paired with the best-overlap native tray");
Check((nint)202, NativeTaskbarTrayService.SelectBestTrayCandidate(nativeTrayCandidates, trayDisplay)?.TrayWindow, "focus the tray child paired with the best-overlap native tray");
Check<TaskbarBounds?>(null, NativeTaskbarTrayService.SelectBestTrayBounds([new TaskbarBounds(0, 1030, 1920, 50)], secondaryDisplay), "ignore notification areas that belong to another display");
Check(new TaskbarBounds(0, 1026, 1500, 54), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom, TaskbarLayout: TaskbarStyle.Segmented), nativeTray), "leave the native notification area uncovered on a segmented bar");
Check(new TaskbarBounds(0, 1026, 1500, 54), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom, TaskbarLayout: TaskbarStyle.DockLike), nativeTray), "preserve the native notification area beside the dock-style bar");
Check(new TaskbarBounds(0, 0, 1500, 46), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Top, TaskbarSize.Small), new(1500, 0, 420, 50)), "leave a top-edge native notification area uncovered");
Check(new TaskbarBounds(0, 0, 176, 800), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Left), new(0, 800, 176, 280)), "leave a bottom-corner native notification area uncovered on a left bar");
Check(new TaskbarBounds(1744, 0, 176, 800), TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Right), new(1744, 800, 176, 280)), "leave a bottom-corner native notification area uncovered on a right bar");
Check(false, TaskbarTrayIntegrationPolicy.ShouldReserveShellHostWorkArea(trayDisplay, new(TaskbarEdge.Bottom), nativeTray), "avoid reserving a second work area when this display exposes an integrated native tray");
Check(true, TaskbarTrayIntegrationPolicy.ShouldReserveShellHostWorkArea(secondaryDisplay, new DesktopPreferences(TaskbarEdge.Bottom), null), "reserve a work area on a shell-host display without a native tray");
Check(true, TaskbarTrayIntegrationPolicy.ShouldReserveShellHostWorkArea(trayDisplay, new DesktopPreferences(TaskbarEdge.Bottom) with { ReplaceNativeTaskbar = true }, nativeTray), "reserve a work area when replacement mode explicitly hides the native taskbar");
Check(true, TaskbarTrayIntegrationPolicy.ShouldUseNativeTray(true, false, nativeTray), "use the native tray on a shell-host display that exposes Explorer's notification area");
Check(false, TaskbarTrayIntegrationPolicy.ShouldUseNativeTray(true, false, null), "do not use native tray integration on a shell-host display without Explorer's notification area");
Check(false, TaskbarTrayIntegrationPolicy.ShouldUseReplacementTaskbar(true, false, nativeTray), "keep replacement system controls off on a shell-host display with a native tray");
Check(true, TaskbarTrayIntegrationPolicy.ShouldUseReplacementTaskbar(true, false, null), "use replacement system controls on a shell-host display without a native tray");
Check(false, TaskbarTrayIntegrationPolicy.ShouldUseReplacementWorkArea(true, false, new(0, 1026, 1500, 54)), "keep the native tray layout when the shell-host bar shares its edge safely");
Check(true, TaskbarTrayIntegrationPolicy.ShouldUseReplacementWorkArea(true, false, null), "reserve a replacement work area when the shell-host tray layout cannot be integrated");
Check(false, TaskbarTrayIntegrationPolicy.ShouldReconcileTraySignature("display-a:1,2,3,4", "display-a:1,2,3,4"), "avoid rebuilding shell-host taskbars when tray geometry is unchanged");
Check(true, TaskbarTrayIntegrationPolicy.ShouldReconcileTraySignature("display-a:1,2,3,4", "display-a:1,2,4,4"), "reconcile shell-host taskbars when tray geometry changes");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom, TaskbarLayout: TaskbarStyle.Floating), nativeTray), "keep shortcut fallback on floating bars");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Top), nativeTray), "keep shortcut fallback on top bars");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Left), new(900, 800, 176, 280)), "keep shortcut fallback when a vertical bar does not share the tray edge");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), null), "keep shortcut fallback when the native tray is unavailable");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), new(100, 1030, 100, 50)), "keep shortcut fallback when the tray boundary leaves too little taskbar space");
Check<TaskbarBounds?>(null, TaskbarTrayIntegrationPolicy.CalculateOverlayBounds(trayDisplay, new(TaskbarEdge.Bottom), new(1500, 700, 420, 50)), "keep shortcut fallback when tray is not at the bottom edge");
var changedPrimaryDisplay = new TaskbarDisplay("DISPLAY1", 0, 0, 2560, 1440, true, 1.25, 1.25);
var connectedDisplay = new TaskbarDisplay("DISPLAY3", 2560, 0, 1920, 1080, false);
var displayChangePlan = TaskbarDisplayService.PlanTopologyChange([trayDisplay, secondaryDisplay], [changedPrimaryDisplay, connectedDisplay]);
Check("DISPLAY1", string.Join(',', displayChangePlan.Retained.Select(display => display.DeviceName)), "retain taskbar windows for displays that remain connected");
Check("DISPLAY3", string.Join(',', displayChangePlan.Added.Select(display => display.DeviceName)), "add taskbar windows only for newly connected displays");
Check("DISPLAY2", string.Join(',', displayChangePlan.RemovedDeviceNames), "remove taskbar windows only for disconnected displays");
var primaryOnlyPlan = TaskbarDisplayService.PlanTopologyChange([trayDisplay, secondaryDisplay], [trayDisplay]);
Check(0, primaryOnlyPlan.Added.Count, "avoid adding bars outside the selected primary-display coverage");
Check("DISPLAY2", string.Join(',', primaryOnlyPlan.RemovedDeviceNames), "drop secondary taskbar bars when display coverage narrows");
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
RunningWindow[] windowsAcrossDisplays =
[
    new RunningWindow((nint)201, "Primary", "Editor", @"C:\Apps\editor.exe", false) { Bounds = new(100, 100, 600, 500) },
    new RunningWindow((nint)202, "Secondary", "Browser", @"C:\Apps\browser.exe", false) { Bounds = new(-1800, -100, 900, 700) },
    new RunningWindow((nint)203, "Spanning", "Mail", @"C:\Apps\mail.exe", false) { Bounds = new(-200, 200, 700, 500) }
];
Check("201,202,203", string.Join(',', TaskbarWindowDisplayPolicy.Filter(windowsAcrossDisplays, primaryDisplay, [secondaryDisplay, primaryDisplay], TaskbarWindowDisplayMode.AllTaskbars).Select(window => window.Handle)), "show all app windows on every taskbar in the default mode");
RunningWindow[] windowsAcrossVirtualDesktops =
[
    windowsAcrossDisplays[0] with { IsOnCurrentVirtualDesktop = true },
    windowsAcrossDisplays[1] with { IsOnCurrentVirtualDesktop = false },
    windowsAcrossDisplays[2]
];
Check("201,203", string.Join(',', TaskbarVirtualDesktopPolicy.Filter(windowsAcrossVirtualDesktops, showAllVirtualDesktops: false).Select(window => window.Handle)), "show current-desktop windows and keep unknown desktop assignments visible");
Check("201,202,203", string.Join(',', TaskbarVirtualDesktopPolicy.Filter(windowsAcrossVirtualDesktops, showAllVirtualDesktops: true).Select(window => window.Handle)), "show windows from every virtual desktop when requested");
Check("201,203", string.Join(',', TaskbarWindowDisplayPolicy.Filter(windowsAcrossDisplays, primaryDisplay, [secondaryDisplay, primaryDisplay], TaskbarWindowDisplayMode.TaskbarOnWhichWindowIsOpen).Select(window => window.Handle)), "show app windows on the display with the largest window overlap");
Check("202", string.Join(',', TaskbarWindowDisplayPolicy.Filter(windowsAcrossDisplays, secondaryDisplay, [secondaryDisplay, primaryDisplay], TaskbarWindowDisplayMode.TaskbarOnWhichWindowIsOpen).Select(window => window.Handle)), "show a secondary app only on its own taskbar");
Check("201,202,203", string.Join(',', TaskbarWindowDisplayPolicy.Filter(windowsAcrossDisplays, primaryDisplay, [secondaryDisplay, primaryDisplay], TaskbarWindowDisplayMode.PrimaryAndTaskbarOnWhichWindowIsOpen).Select(window => window.Handle)), "also show all app windows on the primary taskbar");
var nativeAssignedWindow = windowsAcrossDisplays[0] with { DisplayDeviceName = secondaryDisplay.DeviceName };
Check("201", string.Join(',', TaskbarWindowDisplayPolicy.Filter([nativeAssignedWindow], secondaryDisplay, [secondaryDisplay, primaryDisplay], TaskbarWindowDisplayMode.TaskbarOnWhichWindowIsOpen).Select(window => window.Handle)), "prefer Windows' assigned monitor for a spanning window");
var tallSecondary = new TaskbarDisplay("TALL", 1000, -1000, 1000, 3000, false);
var flatPrimary = new TaskbarDisplay("FLAT", 0, 0, 1000, 1000, true);
var spanningDisplayWindow = new RunningWindow((nint)204, "Spanning", "Editor", @"C:\Apps\editor.exe", false) { Bounds = new(500, -500, 980, 1000) };
Check("204", string.Join(',', TaskbarWindowDisplayPolicy.Filter([spanningDisplayWindow], tallSecondary, [flatPrimary, tallSecondary], TaskbarWindowDisplayMode.TaskbarOnWhichWindowIsOpen).Select(window => window.Handle)), "use largest display intersection when Windows has no monitor assignment");
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
var centeredBottomStart = TaskbarLayoutCalculator.CalculateStartMenu(primaryDisplay, 470, 650, new(TaskbarEdge.Bottom), centered: true);
Check(986.25d, centeredBottomStart.Left, "center Start menu horizontally along the bottom taskbar on a scaled display");
Check(545d, centeredBottomStart.Top, "keep a centered Start menu adjacent to the bottom taskbar");
var centeredTopStart = TaskbarLayoutCalculator.CalculateStartMenu(primaryDisplay, 470, 650, new(TaskbarEdge.Top), centered: true);
Check(986.25d, centeredTopStart.Left, "center Start menu horizontally along the top taskbar on a scaled display");
Check(82.5d, centeredTopStart.Top, "keep a centered Start menu adjacent to the top taskbar");
var centeredLeftStart = TaskbarLayoutCalculator.CalculateStartMenu(secondaryDisplay, 470, 650, new(TaskbarEdge.Left), centered: true);
Check(-1638d, centeredLeftStart.Left, "keep a centered Start menu beside a left taskbar");
Check(-147.5d, centeredLeftStart.Top, "center Start menu vertically along a left taskbar on a scaled secondary display");
var centeredRightStart = TaskbarLayoutCalculator.CalculateStartMenu(secondaryDisplay, 470, 650, new(TaskbarEdge.Right), centered: true);
Check(-987d, centeredRightStart.Left, "keep a centered Start menu beside a right taskbar");
Check(-147.5d, centeredRightStart.Top, "center Start menu vertically along a right taskbar on a scaled secondary display");
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
Check("Microphone · 42%", AudioVolumePolicy.GetMicrophoneLabel(0.42f, false), "format active microphone level for its taskbar control");
Check("Microphone muted · 42%", AudioVolumePolicy.GetMicrophoneLabel(0.42f, true), "format muted microphone level for its taskbar control");
CheckTrue(AudioVolumePolicy.IsDefaultEndpoint("input-a", "INPUT-A"), "identify the selected capture endpoint without case-sensitive matching");
Check(false, AudioVolumePolicy.IsDefaultEndpoint("input-a", null), "leave capture endpoints unselected when Windows has no default input");
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
CheckTrue(TaskbarPinCatalog.IsSupportedTarget(@"C:\Apps\Folder", isDirectory: true), "allow folders as taskbar pin targets from Explorer");
Check(false, TaskbarPinCatalog.IsSupportedTarget(@"C:\Apps\readme.txt"), "reject non-launchable Explorer files as taskbar pin targets");
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
var wheelWindows = new[]
{
    new RunningWindow((nint)301, "First", "Editor", @"C:\Apps\editor.exe", false) { IsForeground = true },
    new RunningWindow((nint)302, "Second", "Editor", @"C:\Apps\editor.exe", false),
    new RunningWindow((nint)303, "Third", "Editor", @"C:\Apps\editor.exe", false)
};
Check((nint)302, TaskbarWindowWheelPolicy.SelectTarget(wheelWindows, -120)?.Handle,
    "scroll down to the next running window from a taskbar button");
Check((nint)303, TaskbarWindowWheelPolicy.SelectTarget(wheelWindows, 120)?.Handle,
    "scroll up to the previous running window from a taskbar button");
Check((nint)303, TaskbarWindowWheelPolicy.SelectTarget(wheelWindows.Skip(1), 120)?.Handle,
    "start taskbar wheel navigation at the last available window when no window is foreground");
Check(null, TaskbarWindowWheelPolicy.SelectTarget(wheelWindows.Take(1), -120),
    "leave the mouse wheel available when a taskbar target has only one window");
var alwaysGroupedWindows = TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Always, 10);
CheckTrue(TaskbarWindowActivationPolicy.ShouldMinimize(isForeground: true, isMinimized: false), "minimize an already active taskbar window on a second click");
CheckTrue(!TaskbarWindowActivationPolicy.ShouldMinimize(isForeground: true, isMinimized: true), "restore rather than minimize an already minimized taskbar window");
CheckTrue(!TaskbarWindowActivationPolicy.ShouldMinimize(isForeground: false, isMinimized: false), "activate a taskbar window when another app is foreground");
Check(2, alwaysGroupedWindows.Count, "always group windows from the same executable");
Check("Editor (2)", alwaysGroupedWindows[0].Label, "show the app name and window count for a grouped button");
CheckTrue(alwaysGroupedWindows[0].IsActive, "mark an app group active when one of its windows is foreground");
Check("Document one" + Environment.NewLine + "Document two", alwaysGroupedWindows[0].ToolTip, "list window titles in a grouped button tooltip");
Check((nint)1, TaskbarWindowGrouping.SelectCloseTarget(alwaysGroupedWindows[0])!.Handle, "middle-click closes the foreground window in a taskbar group");
var minimizedWindow = new RunningWindow((nint)304, "Minimized", "Editor", @"C:\Apps\editor.exe", true);
var maximizedWindow = minimizedWindow with { Handle = (nint)305, IsMinimized = false, IsMaximized = true };
CheckTrue(TaskbarWindowGrouping.CanRestore(minimizedWindow), "offer restore for minimized taskbar windows");
CheckTrue(TaskbarWindowGrouping.CanRestore(maximizedWindow), "offer restore for maximized taskbar windows");
CheckTrue(TaskbarWindowGrouping.CanMaximize(minimizedWindow), "offer maximize for non-maximized taskbar windows");
CheckTrue(!TaskbarWindowGrouping.CanMaximize(maximizedWindow), "disable maximize for already maximized taskbar windows");
var inactiveWindowGroup = new TaskbarWindowGroup("Editor", "Editor", [runningWindows[1]]);
Check((nint)2, TaskbarWindowGrouping.SelectCloseTarget(inactiveWindowGroup)!.Handle, "middle-click closes the only window when no group member is foreground");
CheckTrue(TaskbarWindowGrouping.SelectCloseTarget(new TaskbarWindowGroup("Empty", "Empty", [])) is null, "ignore middle-click when a taskbar group has no live windows");
Check(3, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Never, 1).Count, "never group taskbar windows");
Check(3, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.WhenFull, 3).Count, "keep windows separate while the taskbar has capacity");
Check(2, TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.WhenFull, 2).Count, "group windows when the taskbar is full");
CheckTrue(TaskbarLabelVisibilityPolicy.ShouldShow(TaskbarLabelVisibility.Always, 20, 1), "always show taskbar labels");
CheckTrue(TaskbarLabelVisibilityPolicy.ShouldShow(TaskbarLabelVisibility.WhenFull, 4, 4), "show taskbar labels while buttons fit");
CheckTrue(!TaskbarLabelVisibilityPolicy.ShouldShow(TaskbarLabelVisibility.WhenFull, 5, 4), "hide taskbar labels when buttons exceed capacity");
CheckTrue(!TaskbarLabelVisibilityPolicy.ShouldShow(TaskbarLabelVisibility.Never, 1, 4), "hide taskbar labels in never mode");
CheckTrue(TaskbarOverflowPolicy.ShouldShow(402, 400), "show a taskbar overflow command when app buttons exceed the viewport");
CheckTrue(!TaskbarOverflowPolicy.ShouldShow(400, 400), "hide taskbar overflow when buttons fit exactly");
var overflowPin = new PinnedTaskbarApp("Editor", @"C:\Apps\editor.exe");
CheckTrue(TaskbarOverflowPolicy.IsPinnedAppActive(overflowPin, [
    new RunningWindow((nint)11, "Editor document", "Editor", @"C:\Apps\editor.exe", false) { IsForeground = true }
]), "mark a pinned app active in the taskbar overflow menu when its window is foreground");
Check(false, TaskbarOverflowPolicy.IsPinnedAppActive(overflowPin, [
    new RunningWindow((nint)12, "Editor document", "Editor", @"C:\Apps\editor.exe", false)
]), "leave a pinned app inactive in overflow when none of its windows is foreground");
var launchableGroup = new TaskbarWindowGroup("Desktop Tuner", "Desktop Tuner", [new RunningWindow((nint)7, "Desktop Tuner", "Desktop Tuner", Environment.ProcessPath!, false)]);
Check(Environment.ProcessPath, TaskbarWindowGrouping.GetLaunchPath(launchableGroup), "find an executable path for a running taskbar group's new-instance command");
var packagedRunningGroup = new TaskbarWindowGroup("Store app", "Store app", [new RunningWindow((nint)10, "Store app", "ApplicationFrameHost", @"C:\\Windows\\System32\\ApplicationFrameHost.exe", false)
{
    ApplicationUserModelId = "Microsoft.WindowsCalculator_11.0.0.0_x64__8wekyb3d8bbwe!App"
}]);
Check("explorer.exe", TaskbarWindowGrouping.GetLaunchInfo(packagedRunningGroup)!.FileName, "launch packaged running apps through the AppsFolder shell namespace");
Check("shell:AppsFolder\\Microsoft.WindowsCalculator_11.0.0.0_x64__8wekyb3d8bbwe!App", TaskbarWindowGrouping.GetLaunchInfo(packagedRunningGroup)!.ArgumentList[0], "use the packaged app identity instead of ApplicationFrameHost");
CheckTrue(launchableGroup.CanLaunchNewInstance, "enable new-instance launch for a running taskbar group with a live executable");
CheckTrue(launchableGroup.CanOpenLocation, "enable file-location navigation for a running taskbar group with a live executable");
CheckTrue(!new TaskbarWindowGroup("No PID", "No PID", [new RunningWindow((nint)8, "No PID", "No PID", string.Empty, false)]).CanEndTask, "disable End task when a window process is unavailable");
CheckTrue(new TaskbarWindowGroup("With PID", "With PID", [new RunningWindow((nint)9, "With PID", "With PID", string.Empty, false) { ProcessId = 42 }]).CanEndTask, "enable End task when a window process is known");
var editorPin = new PinnedTaskbarApp("Editor", @"C:\Apps\editor.exe");
var shellPin = new PinnedTaskbarApp("This PC", "shell:MyComputerFolder", IsShellNamespace: true);
CheckTrue(TaskbarPinCatalog.IsSupportedShellNamespaceTarget(shellPin.ExecutablePath), "accept supported Shell namespace locations as taskbar pin targets");
Check(false, shellPin.CanRunElevated, "disable elevation for Shell namespace taskbar pins");
Check(false, shellPin.CanOpenLocation, "disable file-location navigation for Shell namespace taskbar pins");
Check(true, shellPin.CanPinToStart, "allow supported Shell namespace taskbar pins to round-trip to Start");
var pinnedDestinationPins = TaskbarPinCatalog.AddJumpListDestination([editorPin], editorPin.ExecutablePath, "DesktopTuner", Environment.CurrentDirectory);
Check("DesktopTuner", pinnedDestinationPins.Single().PinnedDestinations!.Single().Name, "add a folder to an app's pinned Jump List destinations");
Check(1, TaskbarPinCatalog.AddJumpListDestination(pinnedDestinationPins, editorPin.ExecutablePath, "DesktopTuner", Environment.CurrentDirectory).Single().PinnedDestinations!.Count, "avoid duplicate pinned Jump List destinations");
Check("explorer.exe", TaskbarPinCatalog.BuildLaunchInfo(shellPin).FileName, "launch Shell namespace taskbar pins through Explorer");
Check("shell:MyComputerFolder", TaskbarPinCatalog.BuildLaunchInfo(shellPin).ArgumentList.Single(), "preserve the Shell parsing name when launching a taskbar pin");
var shellPins = TaskbarPinCatalog.AddShellNamespace([], "This PC", "shell:MyComputerFolder");
Check(true, shellPins.Single().IsShellNamespace, "persist the Shell namespace identity on a taskbar pin");
Check(1, TaskbarPinCatalog.AddShellNamespace(shellPins, "This PC", "shell:MyComputerFolder").Count, "avoid duplicate Shell namespace taskbar pins");
Check("This PC", DesktopShellNamespaceCatalog.GetFriendlyName("shell:MyComputerFolder"), "label the This PC Shell namespace pin");
CheckTrue(TaskbarWindowGrouping.MatchesPinnedApp(editorPin, runningWindows[1]), "match a running window to its pinned app without case-sensitive path differences");
Check((nint)1, TaskbarWindowGrouping.SelectPinnedRepresentative(editorPin, [runningWindows[1], runningWindows[0]])!.Handle, "prefer the foreground matching window for a pinned taskbar button");
Check((nint)2, TaskbarWindowGrouping.SelectLastActivePinnedWindow(editorPin, runningWindows, [(nint)3, (nint)2, (nint)1])!.Handle, "select the most recently active matching window for Win+Ctrl+number");
Check<RunningWindow?>(null, TaskbarWindowGrouping.SelectLastActivePinnedWindow(editorPin, runningWindows, [(nint)3]), "do not select a pinned app window absent from foreground history");
var jumpListEntries = TaskbarJumpListPolicy.NormalizeDestinations([
    new("Report.docx", @"C:\Docs\Report.docx"),
    new("Duplicate casing", @"c:\docs\report.docx"),
    new("Plan.xlsx", @"C:\Docs\Plan.xlsx"),
    new(" ", @"C:\Docs\Ignored.docx")
]);
Check(2, jumpListEntries.Count, "filter duplicate and unnamed taskbar Jump List destinations");
Check(1, TaskbarJumpListPolicy.NormalizeDestinations(jumpListEntries, maximumCount: 1).Count, "bound the number of taskbar Jump List destinations");
Check(0, TaskbarJumpListPolicy.NormalizeDestinations(jumpListEntries, maximumCount: 0).Count, "allow an empty taskbar Jump List destination limit");
Check(true, new PinnedTaskbarApp("Current app", Environment.ProcessPath!).CanShowJumpList, "allow Jump List menus for existing executable pins");
Check(false, new PinnedTaskbarApp("Folder", Environment.CurrentDirectory, IsDirectory: true).CanShowJumpList, "hide app Jump List menus for folder pins");
var editorShortcutPin = new PinnedTaskbarApp("Editor shortcut", @"C:\Apps\Editor.lnk");
CheckTrue(TaskbarPinIdentityService.Matches(editorShortcutPin, runningWindows[0], _ => @"C:\Apps\Editor.exe"), "match a running app to the executable target of its pinned shortcut");
CheckTrue(!TaskbarPinIdentityService.Matches(editorShortcutPin, runningWindows[0], _ => @"C:\Apps\Other.exe"), "keep a shortcut separate when its target is a different executable");
var neverGroupPinnedWindows = TaskbarWindowGrouping.Create(runningWindows, TaskbarGroupingMode.Never, 10, [editorPin]);
Check(2, neverGroupPinnedWindows.Count, "show additional windows for pinned apps as separate buttons when grouping is off");
Check("Document two,Inbox", string.Join(',', neverGroupPinnedWindows.Select(group => group.Windows.Single().Title)), "attach one pinned-app window to its pin and leave each other window separate");
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
var clockSample = new DateTime(2026, 9, 13, 17, 5, 0);
var usCulture = CultureInfo.GetCultureInfo("en-US");
var ukCulture = CultureInfo.GetCultureInfo("en-GB");
Check("5:05 PM", TaskbarClockPolicy.FormatTime(clockSample, usCulture), "format the taskbar clock using a 12-hour locale");
Check("17:05", TaskbarClockPolicy.FormatTime(clockSample, ukCulture), "format the taskbar clock using a 24-hour locale");
Check("5:05:00 PM", TaskbarClockPolicy.FormatTime(clockSample, usCulture, showSeconds: true), "include seconds using the 12-hour locale's long-time pattern");
Check("17:05:00", TaskbarClockPolicy.FormatTime(clockSample, ukCulture, showSeconds: true), "include seconds using the 24-hour locale's long-time pattern");
Check("9/13/2026", TaskbarClockPolicy.FormatDate(clockSample, usCulture), "format the taskbar date using the US regional order");
Check("13/09/2026", TaskbarClockPolicy.FormatDate(clockSample, ukCulture), "format the taskbar date using the UK regional order");
Check(new Thickness(1, 0, 1, 0), TaskbarButtonSpacingPolicy.GetButtonMargin(TaskbarButtonSpacing.Compact, false), "apply compact spacing on a horizontal taskbar");
Check(new Thickness(0, 4, 0, 4), TaskbarButtonSpacingPolicy.GetButtonMargin(TaskbarButtonSpacing.Relaxed, true), "apply relaxed spacing on a vertical taskbar");
Throws<ArgumentOutOfRangeException>(() => TaskbarButtonSpacingPolicy.GetGap((TaskbarButtonSpacing)99), "reject unknown taskbar spacing values");
CheckTrue(TaskbarIconService.LoadIcon(Environment.ProcessPath!) is not null, "extract a taskbar icon from an executable file");
CheckTrue(new AppEntry("Desktop Tuner", Environment.ProcessPath!).Icon is not null, "expose extracted app icons to Start menu entries");
var shellFolderPath = Path.GetFullPath(Environment.CurrentDirectory);
CheckTrue(FolderShellIntegrationService.TryReadInvocation(["--OPEN-FOLDER", shellFolderPath], out var parsedShellFolder), "recognize case-insensitive folder context-menu launch arguments");
Check(shellFolderPath, parsedShellFolder, "normalize the folder passed by a context-menu command");
Check(false, FolderShellIntegrationService.TryReadInvocation(["--open-folder", "relative\\folder"], out _), "reject a relative folder context-menu target");
CheckTrue(FolderShellIntegrationService.TryReadFileLocationInvocation(["--OPEN-FILE-LOCATION", Environment.ProcessPath!], out var parsedFileLocation), "recognize file location context-menu launch arguments");
Check(Path.GetFullPath(Environment.ProcessPath!), parsedFileLocation, "normalize the file passed by a context-menu command");
Check(false, FolderShellIntegrationService.TryReadFileLocationInvocation(["--open-file-location", shellFolderPath], out _), "reject a directory as a file location context-menu target");
var shellNamespace = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
CheckTrue(FolderShellIntegrationService.TryReadShellLocationInvocation(["--OPEN-SHELL-LOCATION", shellNamespace], out var parsedShellNamespace), "recognize namespace context-menu launch arguments");
Check(shellNamespace, parsedShellNamespace, "preserve the shell namespace parsing name");
CheckTrue(FolderShellIntegrationService.TryReadShellLocationInvocation(["--open-shell-location", shellFolderPath], out var parsedFolderFromVirtualVerb), "accept filesystem folders passed through the virtual-folder shell verb");
Check(shellFolderPath, parsedFolderFromVirtualVerb, "normalize filesystem folders passed through the virtual-folder shell verb");
Check($"\"{Path.GetFullPath(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe")}\" --open-folder \"%1\"", FolderShellIntegrationService.BuildCommand(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe", "%1"), "quote executable and selected-folder arguments in the directory context command");
Check($"\"{Path.GetFullPath(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe")}\" --open-folder \"%V\"", FolderShellIntegrationService.BuildCommand(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe", "%V"), "quote the current-folder argument for empty-space context menus");
Check($"\"{Path.GetFullPath(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe")}\" --open-file-location \"%1\"", FolderShellIntegrationService.BuildFileLocationCommand(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe", "%1"), "quote selected-file arguments in the modern Explorer context command");
Throws<ArgumentOutOfRangeException>(() => FolderShellIntegrationService.BuildFileLocationCommand(Environment.ProcessPath!, "%*"), "reject unrecognized file location substitutions");
Throws<ArgumentOutOfRangeException>(() => FolderShellIntegrationService.BuildCommand(Environment.ProcessPath!, "%*"), "reject unrecognized shell path substitutions");
Check($"\"{Path.GetFullPath(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe")}\" --open-shell-location \"%1\"", FolderShellIntegrationService.BuildShellLocationCommand(@"C:\\Program Files\\Desktop Tuner\\DesktopTuner.exe", "%1"), "quote executable and namespace arguments in the virtual-folder context command");
Throws<ArgumentOutOfRangeException>(() => FolderShellIntegrationService.BuildShellLocationCommand(Environment.ProcessPath!, "%*"), "reject unrecognized namespace path substitutions");
Check(5, FolderShellIntegrationService.VerbPaths.Count, "register directory, virtual-folder, and drive context menu commands");
Check(3, FolderShellIntegrationService.DefaultHandlerPaths.Count, "temporarily own filesystem, virtual-folder, and drive default handlers in shell-host mode");
Check("Software\\Classes\\Directory\\shell\\DesktopTuner.OpenWith", FolderShellIntegrationService.DefaultVerbPath, "use the registered Desktop Tuner folder verb as the shell-host default");
Check("DesktopTuner.OpenWith", FolderShellIntegrationService.DefaultVerb, "identify the owned shell-host folder verb");
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
Check(true, StartMenuAppNavigationPolicy.ShouldShowProgramFolders(StartMenuStyle.Windows7, ""), "show categorized Start folders in the Windows 7-inspired layout");
Check(false, StartMenuAppNavigationPolicy.ShouldShowProgramFolders(StartMenuStyle.Windows7, "paint"), "show ranked search results instead of folders in the Windows 7-inspired layout");
Check(true, StartMenuAppNavigationPolicy.ShouldShowProgramFolders(StartMenuStyle.Classic, " "), "preserve classic Start folder browsing when the search query is blank");
Check(false, StartMenuAppNavigationPolicy.ShouldShowProgramFolders(StartMenuStyle.Modern, ""), "keep the modern Start layout on its app list");
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
var typoTolerantSearchResults = AppCatalogService.Search(
[
    new AppEntry("Calculator", "calculator.lnk"),
    new AppEntry("Calendar", "calendar.lnk"),
    new AppEntry("Firefox", "firefox.lnk", CategoryPath: "Internet Tools")
], "calculatr");
Check("Calculator", typoTolerantSearchResults.Single().Name, "find a Start app after a one-character search typo");
Check("Firefox", AppCatalogService.Search([new AppEntry("Firefox", "firefox.lnk")], "fireofx").Single().Name, "match adjacent transposed letters in Start search");
Check("Calendar", AppCatalogService.Search([new AppEntry("Calendar", "calendar.lnk")], "calender").Single().Name, "include fuzzy search matches after a single substitution");
Check(0, AppCatalogService.Search([new AppEntry("Cut", "cut.lnk")], "cat").Count, "avoid typo expansion for short Start search terms");
Check("Calculatr,Calculator", string.Join(',', AppCatalogService.Search(
[
    new AppEntry("Calculatr", "calculatr.lnk"),
    new AppEntry("Calculator", "calculator.lnk")
], "calculatr").Select(app => app.Name)), "keep exact Start search matches ahead of fuzzy matches");
Check("SJ", StartMenuIdentityService.GetInitials("Sam Jones"), "build an account avatar from the user's first and last names");
Check("SJ", StartMenuIdentityService.GetInitials("sam.jones"), "split account names on common username separators for initials");
Check("?", StartMenuIdentityService.GetInitials("  "), "provide a safe avatar fallback for a missing account name");
var startPins = StartPinCatalog.Pin([], new AppEntry("Editor", @"C:\Apps\Editor.lnk", CategoryPath: "Tools"));
Check("Editor", startPins.Single().Name, "pin a Start menu shortcut to the Start favorites list");
Check(1, StartPinCatalog.Pin(startPins, new AppEntry("Editor", @"c:\apps\editor.LNK")).Count, "avoid duplicate Start pins regardless of path casing");
var shellStartPin = StartPinCatalog.AddShellNamespace(startPins, "This PC", "shell:MyComputerFolder");
Check(true, shellStartPin.Single(pin => pin.IsShellNamespace).IsDirectory, "treat a Shell namespace Start pin as a navigable location");
Check(true, StartPinCatalog.IsSupported(new AppEntry("Recycle Bin", "shell:RecycleBinFolder", IsDirectory: true, IsShellNamespace: true)), "accept supported Shell namespace locations as Start pins");
Check(true, StartPinCatalog.IsSupported(new AppEntry("Recent document", @"C:\Users\test\Recent document.lnk")), "accept recent document shortcuts as Start pins");
Check(false, new AppEntry("Recycle Bin", "shell:RecycleBinFolder", IsDirectory: true, IsShellNamespace: true).CanRunElevated, "disable elevation for Shell namespace Start pins");
Check(false, new AppEntry("Recycle Bin", "shell:RecycleBinFolder", IsDirectory: true, IsShellNamespace: true).CanOpenFileLocation, "disable file locations for Shell namespace Start pins");
var recentHistoryPath = Path.Combine(Path.GetTempPath(), $"desktop-tuner-recent-start-{Guid.NewGuid():N}.json");
try
{
    var recentApps = new StartRecentAppsStore(recentHistoryPath);
    var editorApp = new AppEntry("Editor", @"C:\Apps\Editor.lnk");
    var browserApp = new AppEntry("Browser", @"C:\Apps\Browser.lnk");
    CheckTrue(recentApps.TryRecordLaunch(editorApp), "save a locally launched Start app to recent history");
    CheckTrue(recentApps.TryRecordLaunch(browserApp), "save a second locally launched Start app to recent history");
    CheckTrue(recentApps.TryRecordLaunch(new AppEntry("Editor", @"c:\apps\editor.LNK")), "update recent app history without duplicating a path with different casing");
    Check(@"c:\apps\editor.LNK,C:\Apps\Browser.lnk", string.Join(',', recentApps.LoadPaths()), "put the latest app launch first in Start recent history");
    Check("Editor,Browser", string.Join(',', recentApps.Resolve([browserApp, editorApp]).Select(app => app.Name)), "resolve recent history against currently installed apps");
    Check("Browser", string.Join(',', recentApps.Resolve([browserApp]).Select(app => app.Name)), "skip Start history entries for apps no longer in the catalog");
    foreach (var index in Enumerable.Range(0, StartRecentAppsStore.MaximumEntries + 3))
        CheckTrue(recentApps.TryRecordLaunch(new AppEntry($"Recent {index}", $@"C:\Apps\recent{index}.lnk")), "record bounded Start recent-app history entries");
    Check(StartRecentAppsStore.MaximumEntries, recentApps.LoadPaths().Count, "bound the locally stored Start recent-app history");
    Check($@"C:\Apps\recent{StartRecentAppsStore.MaximumEntries + 2}.lnk", recentApps.LoadPaths()[0], "retain the newest apps at the top of bounded Start history");

    var recentFilesDirectory = Path.Combine(Path.GetTempPath(), $"desktop-tuner-recent-files-{Guid.NewGuid():N}");
    Directory.CreateDirectory(recentFilesDirectory);
    var recentDocument = Path.Combine(recentFilesDirectory, "Quarterly report.lnk");
    File.WriteAllText(recentDocument, "placeholder");
    File.SetLastWriteTimeUtc(recentDocument, DateTime.UtcNow);
    var recentFiles = new StartRecentFilesStore(recentFilesDirectory).ReadRecentFiles([recentDocument]);
    Check(0, recentFiles.Count, "exclude app-history shortcuts from recent document results");
    recentFiles = new StartRecentFilesStore(recentFilesDirectory).ReadRecentFiles();
    Check(1, recentFiles.Count, "read recent document shortcuts from the Windows Recent Items folder");
    Check("Quarterly report", recentFiles[0].Name, "derive recent document names from shortcut filenames");
    var recentTarget = Path.Combine(recentFilesDirectory, "Annual budget.xlsx");
    File.WriteAllText(recentTarget, "placeholder");
    var validRecentShortcut = Path.Combine(recentFilesDirectory, "Annual budget.lnk");
    object? recentShellObject = null;
    object? recentShortcutObject = null;
    try
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable for recent shortcut tests.");
        recentShellObject = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows Script Host automation object.");
        dynamic recentShell = recentShellObject;
        recentShortcutObject = recentShell.CreateShortcut(validRecentShortcut);
        dynamic shortcut = recentShortcutObject;
        shortcut.TargetPath = recentTarget;
        shortcut.Save();
    }
    finally
    {
        if (recentShortcutObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(recentShortcutObject)) System.Runtime.InteropServices.Marshal.ReleaseComObject(recentShortcutObject);
        if (recentShellObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(recentShellObject)) System.Runtime.InteropServices.Marshal.ReleaseComObject(recentShellObject);
    }
    File.SetLastWriteTimeUtc(validRecentShortcut, DateTime.UtcNow.AddSeconds(1));
    var recentStore = new StartRecentFilesStore(recentFilesDirectory);
    recentFiles = recentStore.ReadRecentFiles();
    Check("Annual budget.xlsx", recentFiles[0].Name, "resolve valid recent shortcuts to their target document names");
    Check(recentTarget, recentStore.TryResolveTargetPath(validRecentShortcut, out var resolvedRecentTarget) ? resolvedRecentTarget : string.Empty, "resolve recent shortcut targets for file-location actions");
    Check(false, recentStore.TryResolveTargetPath(recentDocument, out _), "fall back safely when a recent shortcut target cannot be resolved");
    Check(1, new StartRecentFilesStore(recentFilesDirectory).ReadRecentFiles(maximumEntries: 1).Count, "bound recent document results to the requested count");
    Check(true, new StartRecentFilesStore(recentFilesDirectory).IsRecentShortcut(recentDocument), "recognize shortcuts inside the Recent Items folder");
    CheckTrue(new StartRecentFilesStore(recentFilesDirectory).TryRemove(recentDocument), "remove an individual recent document shortcut");
    CheckTrue(new StartRecentFilesStore(recentFilesDirectory).TryRemove(validRecentShortcut), "remove a resolved recent document shortcut");
    Check(0, new StartRecentFilesStore(recentFilesDirectory).ReadRecentFiles().Count, "omit a removed recent document shortcut");
    Check("Quarterly report", string.Join(',', AppCatalogService.Search(
        [new AppEntry("Quarterly report", recentDocument), new AppEntry("Editor", @"C:\Apps\Editor.lnk")], "quarterly")
        .Select(app => app.Name)), "find recent document shortcuts by name in Start search");
    CheckTrue(recentApps.TryClear(), "clear the locally stored Start recent-app history");
    Check(0, recentApps.LoadPaths().Count, "remove all entries when Start recent history is cleared");
}
finally
{
    if (File.Exists(recentHistoryPath)) File.Delete(recentHistoryPath);
}
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
Check(StartTileSize.Wide, StartPinCatalog.SetTileSize(orderedStartPins, @"C:\Apps\Browser.lnk", StartTileSize.Wide).Single(app => app.Name == "Browser").TileSize, "resize a pinned Start tile");
Check(StartTileSize.Medium, StartPinCatalog.Normalize([new AppEntry("Invalid size", @"C:\Apps\invalid.lnk", TileSize: (StartTileSize)99)]).Single().TileSize, "normalize an unknown saved Start tile size");
Throws<ArgumentOutOfRangeException>(() => StartPinCatalog.SetTileSize(orderedStartPins, @"C:\Apps\Browser.lnk", (StartTileSize)99), "reject an unknown Start tile size");
var groupedStartPins = StartPinCatalog.SetGroup(orderedStartPins, @"C:\Apps\Browser.lnk", " Games ");
Check("Games", groupedStartPins.Single(app => app.Name == "Browser").GroupName, "move a pinned Start app into a named tile group");
Check(StartTileSize.Medium, groupedStartPins.Single(app => app.Name == "Browser").TileSize, "preserve tile size when moving an app between groups");
Check("Pinned,Games", string.Join(',', StartPinCatalog.Group(groupedStartPins).Select(group => group.Name)), "keep Start tile groups in their first pinned order");
var renamedStartGroups = StartPinCatalog.RenameGroup(StartPinCatalog.SetGroup(groupedStartPins, @"C:\Apps\Editor.lnk", "games"), "Games", "Play");
Check("Play", renamedStartGroups.Single(app => app.Name == "Browser").GroupName, "rename all apps in a Start tile group");
Check(1, StartPinCatalog.Group(renamedStartGroups).Count(group => group.Name == "Play"), "merge case-insensitive duplicate Start group names");
Check(StartPinCatalog.DefaultGroupName, StartPinCatalog.NormalizeGroupName(" \n "), "replace blank Start group names with the default group");
Check(StartPinCatalog.MaximumGroupNameLength, StartPinCatalog.NormalizeGroupName(new string('x', 40)).Length, "bound Start tile group names");
Check("Editor,Browser", string.Join(',', StartPinCatalog.Reorder(orderedStartPins, @"C:\Apps\Missing.lnk", 0).Select(app => app.Name)), "ignore a Start favorite drop with an unknown source");
var explorerTabOrder = new List<string> { "Home", "Documents", "Downloads" };
CheckTrue(ExplorerTabOrdering.Move(explorerTabOrder, 0, 3), "move an Explorer tab after the final tab");
Check("Documents,Downloads,Home", string.Join(',', explorerTabOrder), "preserve Explorer tab order when dragging a tab to the end");
CheckTrue(ExplorerTabOrdering.Move(explorerTabOrder, 2, 0), "move an Explorer tab before the first tab");
Check("Home,Documents,Downloads", string.Join(',', explorerTabOrder), "preserve Explorer tab order when dragging a tab to the beginning");
CheckTrue(!ExplorerTabOrdering.Move(explorerTabOrder, 1, 2), "ignore an Explorer tab drop that keeps it in the same position");
CheckTrue(!ExplorerTabOrdering.Move(explorerTabOrder, -1, 0), "ignore an invalid Explorer tab drag source");
var transferSourceTabs = new List<string> { "Home", "Documents" };
var transferTargetTabs = new List<string> { "Downloads" };
CheckTrue(ExplorerTabOrdering.Transfer(transferSourceTabs, transferTargetTabs, 1, 0), "transfer an Explorer tab between windows");
Check("Home", string.Join(',', transferSourceTabs), "remove a transferred tab from its source window");
Check("Documents,Downloads", string.Join(',', transferTargetTabs), "insert a transferred tab at its target position");
CheckTrue(!ExplorerTabOrdering.Transfer(transferSourceTabs, transferTargetTabs, 1, 1), "ignore a transfer with an invalid source index");
var snapshotTime = 0L;
var snapshotRefreshes = 0;
var sharedSnapshotCache = new SharedSnapshotCache<string>(TimeSpan.FromMilliseconds(200), () => snapshotTime);
var firstSharedSnapshot = sharedSnapshotCache.GetOrRefresh(() => { snapshotRefreshes++; return ["window-a"]; });
var reusedSharedSnapshot = sharedSnapshotCache.GetOrRefresh(() => { snapshotRefreshes++; return ["window-b"]; });
Check(1, snapshotRefreshes, "reuse the taskbar window snapshot across displays inside its lifetime");
CheckTrue(ReferenceEquals(firstSharedSnapshot, reusedSharedSnapshot), "reuse the same taskbar snapshot object across displays");
Check("window-a", reusedSharedSnapshot.Single(), "share one consistent taskbar window snapshot across displays");
snapshotTime = 200;
Check("window-b", sharedSnapshotCache.GetOrRefresh(() => { snapshotRefreshes++; return ["window-b"]; }).Single(), "refresh the taskbar window snapshot after its lifetime expires");
Check(2, snapshotRefreshes, "refresh shared taskbar metadata only once after the cache expires");
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
var droppedFolderPath = Path.Combine(Path.GetTempPath(), $"desktop-tuner-start-pin-{Guid.NewGuid():N}");
Directory.CreateDirectory(droppedFolderPath);
try
{
    var droppedFolderPin = StartPinCatalog.AddDroppedFiles([], [droppedFolderPath]).Single();
    Check(Path.GetFileName(droppedFolderPath), droppedFolderPin.Name, "name a Start folder pin from its directory name");
    Check(Path.GetFullPath(droppedFolderPath), droppedFolderPin.ShortcutPath, "store a normalized absolute path for a Start folder pin");
    Check(true, droppedFolderPin.IsDirectory, "mark dropped Start folders for folder-specific launching");
    Check(false, droppedFolderPin.CanRunElevated, "do not offer elevation for Start folder pins");
    Check(false, droppedFolderPin.CanOpenFileLocation, "do not treat a Start folder pin as a shortcut file");
    Check(1, StartPinCatalog.AddDroppedFiles([droppedFolderPin], [droppedFolderPath]).Count, "avoid duplicate Start folder pins");
}
finally
{
    Directory.Delete(droppedFolderPath, recursive: true);
}
Throws<ArgumentException>(() => StartPinCatalog.Pin([], new AppEntry("Unsupported", @"C:\Apps\unsupported.txt")), "reject unsupported Start pin targets");
Check("shell:MyComputerFolder", StartMenuPlaceCatalog.ResolveTarget("computer"), "open This PC from the Start places menu");
Check("shell:RecycleBinFolder", StartMenuPlaceCatalog.ResolveTarget("recycle-bin"), "open the Windows Recycle Bin from Start");
Check("control.exe", StartMenuPlaceCatalog.ResolveTarget("control-panel"), "open Control Panel from the Start places menu");
var programsApplet = ControlPanelAppletCatalog.CreateStartInfo("programs");
Check("control.exe", programsApplet.FileName, "launch Control Panel applets through control.exe");
Check("appwiz.cpl", string.Join(' ', programsApplet.ArgumentList), "open Programs and Features from the Control Panel applet flyout");
Check(true, programsApplet.UseShellExecute, "launch Control Panel applets through the Windows shell");
var keyboardApplet = ControlPanelAppletCatalog.CreateStartInfo("keyboard");
Check("main.cpl keyboard", string.Join(' ', keyboardApplet.ArgumentList), "pass the Keyboard applet selector as a separate argument");
Check("/name Microsoft.Personalization", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("personalization").ArgumentList), "launch canonical Control Panel items with structured arguments");
Check("/name Microsoft.CredentialManager", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("credential-manager").ArgumentList), "launch Credential Manager from the Control Panel applet flyout");
Check("inetcpl.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("internet-options").ArgumentList), "launch Internet Options from the Control Panel applet flyout");
Check("bthprops.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("bluetooth").ArgumentList), "launch Bluetooth Devices from the Control Panel applet flyout");
Check("hdwwiz.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("hardware").ArgumentList), "launch Add Hardware from the Control Panel applet flyout");
Check("irprops.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("infrared").ArgumentList), "launch Infrared settings from the Control Panel applet flyout");
Check("joy.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("game-controllers").ArgumentList), "launch Game Controllers from the Control Panel applet flyout");
Check("TabletPC.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("tablet-pc").ArgumentList), "launch Tablet PC Settings from the Control Panel applet flyout");
Check("telephon.cpl", string.Join(' ', ControlPanelAppletCatalog.CreateStartInfo("phone-modem").ArgumentList), "launch Phone and Modem from the Control Panel applet flyout");
var availableControlPanelApplets = ControlPanelAppletCatalog.GetAvailableApplets(@"C:\Windows\System32",
    path => Path.GetFileName(path) is "appwiz.cpl" or "main.cpl");
CheckTrue(availableControlPanelApplets.Any(applet => applet.Id == "programs"), "show a direct Control Panel applet when its module is installed");
CheckTrue(availableControlPanelApplets.Any(applet => applet.Id == "keyboard"), "retain applet variants that share an installed Control Panel module");
Check(false, availableControlPanelApplets.Any(applet => applet.Id == "power"), "hide a direct Control Panel applet when its module is absent");
Check(false, availableControlPanelApplets.Any(applet => applet.Id == "bluetooth"), "hide newly cataloged applets when their module is absent");
CheckTrue(availableControlPanelApplets.Any(applet => applet.Id == "personalization"), "retain canonical Control Panel entries without a direct cpl file");
var probedCanonicalApplets = ControlPanelAppletCatalog.GetAvailableApplets(
    @"C:\Windows\System32",
    _ => true,
    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Microsoft.Personalization" });
CheckTrue(probedCanonicalApplets.Any(applet => applet.Id == "personalization"), "keep canonical Control Panel entries exposed by the Shell namespace");
Check(false, probedCanonicalApplets.Any(applet => applet.Id == "credential-manager"), "hide canonical Control Panel entries absent from the Shell namespace probe");
var availableLegacyApplets = ControlPanelAppletCatalog.GetAvailableApplets(@"C:\Windows\System32",
    path => Path.GetFileName(path) is "bthprops.cpl" or "joy.cpl");
CheckTrue(availableLegacyApplets.Any(applet => applet.Id == "bluetooth"), "show a newly cataloged applet when its module is installed");
CheckTrue(availableLegacyApplets.Any(applet => applet.Id == "game-controllers"), "show each applet that shares the installed-module availability path");
Check(43, ControlPanelAppletCatalog.Applets.Count, "offer the supported Control Panel applets in the Start flyout");
var normalizedPartialControlPanelApplets = ControlPanelAppletCatalog.Normalize(new ControlPanelAppletPreferences(
    ["credential-manager", "programs", "credential-manager", "unsupported"], ["programs", "unsupported"]));
Check("credential-manager,programs", string.Join(',', normalizedPartialControlPanelApplets.Order!.Take(2)), "normalize and deduplicate a custom Control Panel applet order");
Check(42, normalizedPartialControlPanelApplets.Visible!.Count, "default newly added Control Panel applets to visible for older saved preferences");
var completeAppletOrder = ControlPanelAppletCatalog.Normalize(null).Order!;
completeAppletOrder.Remove("credential-manager");
completeAppletOrder.Insert(0, "credential-manager");
var customControlPanelApplets = ControlPanelAppletCatalog.Normalize(new ControlPanelAppletPreferences(completeAppletOrder, ["programs"]));
Check("programs", string.Join(',', customControlPanelApplets.Visible!), "keep only selected Control Panel applets visible");
Check("programs,credential-manager", string.Join(',', ControlPanelAppletCatalog.Move(customControlPanelApplets, "credential-manager", 1).Order!.Take(2)), "move Control Panel applets within the configured order");
Check(43, ControlPanelAppletCatalog.Normalize(null).Visible!.Count, "show all Control Panel applets for older preference files");
Throws<ArgumentOutOfRangeException>(() => ControlPanelAppletCatalog.CreateStartInfo("unknown"), "reject unknown Control Panel applets");
Check(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), StartMenuPlaceCatalog.ResolveTarget("music"), "open the user's Music folder from the Start places menu");
Check(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StartMenuPlaceCatalog.ResolveTarget("documents"), "resolve Documents from the Start dropdown places");
Check(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StartMenuPlaceCatalog.ResolveTarget("desktop"), "resolve Desktop from the Start dropdown places");
Check(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), StartMenuPlaceCatalog.ResolveTarget("downloads"), "resolve Downloads from the Start dropdown places");
Check(13, StartMenuPlaceCatalog.DropdownPlaces.Count, "show the supported Start places with dropdown navigation");
Check(11, StartMenuPlaceCatalog.AdditionalPlaces.Count, "show the remaining additional Start system places");
Check(true, StartMenuPlaceCatalog.DropdownPlaces.Any(place => place.Id == "devices-printers"), "offer Devices and Printers as a nested Start namespace flyout");
Check(true, StartMenuPlaceCatalog.DropdownPlaces.Any(place => place.Id == "network"), "offer Network as a nested Start namespace flyout");
Check(true, StartMenuPlaceCatalog.DropdownPlaces.Any(place => place.Id == "public"), "offer the shared Public folder as a nested Start filesystem flyout");
Check(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StartMenuPlaceCatalog.ResolveTarget("user-profile"), "open the current user's profile from the Start places menu");
Check(Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments))?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), StartMenuPlaceCatalog.ResolveTarget("public"), "resolve the shared Public folder for replacement-shell routing");
Check("shell:Libraries", StartMenuPlaceCatalog.ResolveTarget("libraries"), "open Libraries from the Start places menu");
Check("shell:PrintersFolder", StartMenuPlaceCatalog.ResolveTarget("devices-printers"), "open Devices and Printers from the Start places menu");
Check("ms-settings:defaultapps", StartMenuPlaceCatalog.ResolveTarget("default-programs"), "open Default Programs from the Start places menu");
Check("ncpa.cpl", StartMenuPlaceCatalog.ResolveTarget("network-connections"), "open Network Connections from the Start places menu");
Check("ms-settings:troubleshoot", StartMenuPlaceCatalog.ResolveTarget("troubleshooting"), "open Troubleshooting from the Start places menu");
Check("shell:Tools", StartMenuPlaceCatalog.ResolveTarget("windows-tools"), "open Windows Tools from the Start places menu");
Check("ms-contact-support:", StartMenuPlaceCatalog.ResolveTarget("help-support"), "open Help and Support from the Start places menu");
Check("shell:Favorites", StartMenuPlaceCatalog.ResolveTarget("favorites"), "open Favorites from the Start places menu");
Check("shell:Games", StartMenuPlaceCatalog.ResolveTarget("games"), "open Games from the Start places menu");
Check("shell:Home", StartMenuPlaceCatalog.ResolveTarget("home"), "open Home from the Start places menu");
Check(true, StartMenuPlaceCatalog.CanExpand(new StartMenuPlaceEntry("Folder", @"C:\Folder", IsDirectory: true, IsReparsePoint: false)), "allow a normal directory to expose another Start place dropdown level");
Check(true, StartMenuPlaceCatalog.CanExpand(new StartMenuPlaceEntry("Libraries", "shell:Libraries", IsDirectory: true, IsReparsePoint: false)), "allow a virtual Shell folder to expose another Start place dropdown level");
Check(0, (await StartMenuPlaceCatalog.ReadChildrenAsync("shell:Libraries", 0)).Count, "bound asynchronous Shell place enumeration before querying the provider");
Check(false, StartMenuPlaceCatalog.CanExpand(new StartMenuPlaceEntry("File.txt", @"C:\File.txt", IsDirectory: false, IsReparsePoint: false)), "keep files as direct Start place entries without submenus");
Check(false, StartMenuPlaceCatalog.CanExpand(new StartMenuPlaceEntry("Linked folder", @"C:\Linked folder", IsDirectory: true, IsReparsePoint: true)), "prevent Start place dropdown traversal through linked folders");
Check("Run...", StartMenuPlaceCatalog.AdditionalPlaces.Single(place => place.Id == "run").Label, "offer the classic Run dialog from the Start places menu");
var defaultStartPlaces = StartMenuPlaceCatalog.Normalize(null);
Check(24, defaultStartPlaces.Order!.Count, "include every supported system place in the default order");
Check(24, defaultStartPlaces.Visible!.Count, "show every system place by default");
var customStartPlaces = StartMenuPlaceCatalog.Normalize(new StartMenuPlacePreferences(["run", "documents", "run", "unsupported"], ["documents", "run", "unsupported"]));
Check("run,documents,desktop,public,downloads,music,pictures,videos,libraries,favorites,games,home,devices-printers,network,user-profile,computer,recycle-bin,control-panel,default-programs,network-connections,troubleshooting,windows-tools,help-support,recent", string.Join(',', customStartPlaces.Order!), "retain a valid custom place order and append missing choices");
Check("run,documents,recycle-bin", string.Join(',', customStartPlaces.Visible!), "preserve visible choices and enable newly added places for legacy preferences");
Check("documents,run,recycle-bin", string.Join(',', StartMenuPlaceCatalog.Move(customStartPlaces, "documents", -1).Visible!), "reorder system places without changing visibility");
Check("run,documents,desktop,public,downloads,music,pictures,videos,libraries,favorites,games,home,devices-printers,network,user-profile,computer,recycle-bin,control-panel,default-programs,network-connections,troubleshooting,windows-tools,help-support,recent", string.Join(',', StartMenuPlaceCatalog.Move(customStartPlaces, "run", -1).Order!), "keep the first system place at the top when moved upward");
var hiddenRecycleBin = StartMenuPlaceCatalog.Normalize(customStartPlaces with { Visible = customStartPlaces.Visible!.Where(id => id != "recycle-bin").ToList() });
Check(false, hiddenRecycleBin.Visible!.Contains("recycle-bin"), "preserve an explicit choice to hide the Recycle Bin place");
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
Check(ExplorerKeyboardAction.FocusAddress, ExplorerKeyboardPolicy.Resolve(Key.F4, ModifierKeys.None), "focus the Explorer address field with F4");
Check(ExplorerKeyboardAction.OpenNewWindow, ExplorerKeyboardPolicy.Resolve(Key.N, ModifierKeys.Control), "open the current Explorer location in a new window with Ctrl+N");
Check(ExplorerKeyboardAction.CreateFolder, ExplorerKeyboardPolicy.Resolve(Key.N, ModifierKeys.Control | ModifierKeys.Shift), "create a folder with Ctrl+Shift+N");
Check(ShellNamespaceBrowserKeyboardAction.CreateFolder,
    ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.N, ModifierKeys.Control | ModifierKeys.Shift, itemListFocused: true, hasSelection: false),
    "create a folder in a virtual Shell location with Ctrl+Shift+N");
Check(ExplorerKeyboardAction.FocusAddress, ExplorerKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, Key.D), "focus the Explorer address field with Alt+D system-key events");
Check(ExplorerKeyboardAction.NavigateBack, ExplorerKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, Key.Left), "navigate back through Explorer history with Alt+Left system-key events");
Check(ExplorerKeyboardAction.NavigateForward, ExplorerKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, Key.Right), "navigate forward through Explorer history with Alt+Right system-key events");
Check(ExplorerKeyboardAction.NavigateParent, ExplorerKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, Key.Up), "navigate to the parent folder with Alt+Up system-key events");
Check(ExplorerKeyboardAction.FocusSearch, ExplorerKeyboardPolicy.Resolve(Key.F, ModifierKeys.Control), "focus Explorer search with Ctrl+F");
Check(ExplorerKeyboardAction.FocusSearch, ExplorerKeyboardPolicy.Resolve(Key.F3, ModifierKeys.None), "focus Explorer search with F3");
Check(ExplorerKeyboardAction.FocusNavigationTree, ExplorerKeyboardPolicy.Resolve(Key.E, ModifierKeys.Control | ModifierKeys.Shift), "focus the Explorer navigation tree with Ctrl+Shift+E");
Check(ExplorerKeyboardAction.ShowProperties, ExplorerKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, Key.Enter), "open Properties for the current Explorer selection with Alt+Enter");
Check(ExplorerKeyboardAction.ReopenClosedTab, ExplorerKeyboardPolicy.Resolve(Key.T, ModifierKeys.Control | ModifierKeys.Shift), "reopen the last closed Explorer tab with Ctrl+Shift+T");
Check(ExplorerKeyboardAction.NextPane, ExplorerKeyboardPolicy.Resolve(Key.F6, ModifierKeys.None), "cycle Explorer navigation panes with F6");
Check(ExplorerKeyboardAction.PreviousPane, ExplorerKeyboardPolicy.Resolve(Key.F6, ModifierKeys.Shift), "cycle Explorer navigation panes in reverse with Shift+F6");
Check(ExplorerKeyboardAction.None, ExplorerKeyboardPolicy.Resolve(Key.L, ModifierKeys.Control | ModifierKeys.Shift), "leave modified Ctrl+L combinations untouched");
Check(ExplorerMouseNavigationAction.Back, ExplorerMouseNavigationPolicy.Resolve(MouseButton.XButton1), "navigate back in Explorer history with the first mouse side button");
Check(ExplorerMouseNavigationAction.Forward, ExplorerMouseNavigationPolicy.Resolve(MouseButton.XButton2), "navigate forward in Explorer history with the second mouse side button");
Check(ExplorerMouseNavigationAction.None, ExplorerMouseNavigationPolicy.Resolve(MouseButton.Left), "keep ordinary Explorer clicks out of history navigation");
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
var shellControlEscapeGesture = new WindowsKeyGesture(replaceControlEscape: true);
Check(WindowsKeyAction.Suppress, shellControlEscapeGesture.KeyDown(0x1b, controlPressed: true), "suppress Ctrl+Esc in replacement shell mode");
Check(WindowsKeyAction.Suppress, shellControlEscapeGesture.KeyDown(0x1b, controlPressed: true), "suppress repeated Ctrl+Esc keydown events");
Check(WindowsKeyAction.OpenStartMenu, shellControlEscapeGesture.KeyUp(0x1b), "route Ctrl+Esc to Desktop Tuner Start");
var nativeControlEscapeGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.PassThrough, nativeControlEscapeGesture.KeyDown(0x1b, controlPressed: true), "preserve Ctrl+Esc outside replacement shell mode");
Check(WindowsKeyAction.PassThrough, nativeControlEscapeGesture.KeyUp(0x1b), "preserve Ctrl+Esc release outside replacement shell mode");
var modifiedControlEscapeGesture = new WindowsKeyGesture(replaceControlEscape: true);
Check(WindowsKeyAction.PassThrough, modifiedControlEscapeGesture.KeyDown(0x1b, controlPressed: true, shiftPressed: true), "preserve Ctrl+Shift+Esc for Task Manager");
var explorerShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, explorerShortcutGesture.KeyDown(0x5b), "capture Windows while only Explorer shortcut replacement is enabled");
Check(WindowsKeyAction.OpenExplorer, explorerShortcutGesture.KeyDown((uint)'E', canOpenExplorer: () => true), "route Win+E to the companion Explorer when enabled");
Check(WindowsKeyAction.Suppress, explorerShortcutGesture.KeyUp((uint)'E'), "suppress Win+E release after opening the companion Explorer");
Check(WindowsKeyAction.Suppress, explorerShortcutGesture.KeyUp(0x5b), "avoid opening Start after routing Win+E");
var shellDesktopShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, shellDesktopShortcutGesture.KeyDown(0x5b), "capture Windows before routing shell-host Show Desktop");
Check(WindowsKeyAction.ToggleDesktop, shellDesktopShortcutGesture.KeyDown((uint)'D', canToggleDesktop: () => true), "route Win+D to the replacement desktop when it is available");
Check(WindowsKeyAction.Suppress, shellDesktopShortcutGesture.KeyUp((uint)'D'), "suppress Win+D release after toggling the replacement desktop");
Check(WindowsKeyAction.Suppress, shellDesktopShortcutGesture.KeyUp(0x5b), "avoid opening Start after toggling the replacement desktop");
var shellMinimizeShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
shellMinimizeShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.MinimizeAllWindows, shellMinimizeShortcutGesture.KeyDown((uint)'M', canMinimizeAllWindows: () => true), "route Win+M to minimize eligible windows in shell-host mode");
Check(WindowsKeyAction.Suppress, shellMinimizeShortcutGesture.KeyUp((uint)'M'), "suppress Win+M release after minimizing windows");
Check(WindowsKeyAction.Suppress, shellMinimizeShortcutGesture.KeyUp(0x5b), "avoid opening Start after routing Win+M");
var shellMinimizeOtherShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: true);
Check(WindowsKeyAction.Suppress, shellMinimizeOtherShortcutGesture.KeyDown(0x5b), "capture Windows before routing shell-host Win+Home");
Check(WindowsKeyAction.MinimizeOtherWindows, shellMinimizeOtherShortcutGesture.KeyDown(0x24, canMinimizeOtherWindows: () => true), "route Win+Home to minimize every eligible window except the foreground window");
Check(WindowsKeyAction.Suppress, shellMinimizeOtherShortcutGesture.KeyUp(0x24), "suppress Win+Home release after minimizing other windows");
Check(WindowsKeyAction.Suppress, shellMinimizeOtherShortcutGesture.KeyUp(0x5b), "avoid opening Start after routing Win+Home");
var shellRestoreShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
shellRestoreShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.PassThrough, shellRestoreShortcutGesture.KeyDown(0xa0), "allow Shift to pass while tracking shell-host Win+Shift+M");
Check(WindowsKeyAction.RestoreMinimizedWindows, shellRestoreShortcutGesture.KeyDown((uint)'M', shiftPressed: true, canRestoreMinimizedWindows: () => true), "route Win+Shift+M to restore windows minimized by Win+M");
Check(WindowsKeyAction.Suppress, shellRestoreShortcutGesture.KeyUp((uint)'M'), "suppress Win+Shift+M release after restoring windows");
Check(WindowsKeyAction.Suppress, shellRestoreShortcutGesture.KeyUp(0x5b), "avoid opening Start after routing Win+Shift+M");
foreach (var (key, surfaceName) in new[]
{
    ((uint)'A', "Quick Settings"), ((uint)'F', "Feedback Hub"), ((uint)'G', "Game Bar"), ((uint)'I', "Settings"), ((uint)'K', "Connect"), ((uint)'N', "Notification Center"),
    ((uint)'H', "Voice typing"), ((uint)'P', "Project"), ((uint)'Q', "Windows Search"), ((uint)'S', "Windows Search"),
    ((uint)'L', "Lock workstation"), ((uint)'U', "Accessibility settings"), ((uint)'V', "Clipboard history"), ((uint)'W', "Widgets"), ((uint)'Z', "Snap layouts"), (0x13u, "About settings"),
    (0x09u, "Task View"), (0x20u, "keyboard layout picker")
})
{
    var surfaceGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
    surfaceGesture.KeyDown(0x5b);
    Check(WindowsKeyAction.OpenShellSystemSurface, surfaceGesture.KeyDown(key, canOpenShellSystemSurface: _ => true), $"route Win+{(key == 0x09 ? "Tab" : ((char)key).ToString())} to {surfaceName} in shell-host mode");
    Check(key, surfaceGesture.ShellSystemSurfaceKey, $"retain the Win+{(key == 0x09 ? "Tab" : ((char)key).ToString())} target until the Windows key is released");
    Check(WindowsKeyAction.Suppress, surfaceGesture.KeyUp(key), $"suppress Win+{(key == 0x09 ? "Tab" : ((char)key).ToString())} release after routing to {surfaceName}");
    Check(WindowsKeyAction.Suppress, surfaceGesture.KeyUp(0x5b), $"consume Windows release after routing to {surfaceName}");
    Check<uint?>(null, surfaceGesture.ShellSystemSurfaceKey, $"clear the {surfaceName} shortcut state after release");
}
var modifiedShellSurfaceGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
modifiedShellSurfaceGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, modifiedShellSurfaceGesture.KeyDown((uint)'N', shiftPressed: true, canOpenShellSystemSurface: _ => true), "preserve modified Win+N instead of opening the unmodified shell surface");
modifiedShellSurfaceGesture.KeyUp((uint)'N');
modifiedShellSurfaceGesture.KeyUp(0x5b);
foreach (var key in new[] { (uint)'A', (uint)'F', (uint)'G', (uint)'H', (uint)'I', (uint)'L', (uint)'N', (uint)'Q', (uint)'S', (uint)'U', (uint)'V', (uint)'W', (uint)'Z', 0x09u, 0x20u })
{
    var nativeSurfaceGesture = new WindowsKeyGesture();
    nativeSurfaceGesture.KeyDown(0x5b);
    Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeSurfaceGesture.KeyDown(key, canOpenShellSystemSurface: _ => false), $"preserve native Windows system surface shortcut {key}");
    nativeSurfaceGesture.KeyUp(key);
    Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeSurfaceGesture.KeyUp(0x5b), $"release Windows after native system surface shortcut {key}");
}
var shiftFirstShellRestoreShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.PassThrough, shiftFirstShellRestoreShortcutGesture.KeyDown(0xa0), "pass a prior Shift press before shell-host Win+Shift+M");
shiftFirstShellRestoreShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.RestoreMinimizedWindows, shiftFirstShellRestoreShortcutGesture.KeyDown((uint)'M', shiftPressed: true, canRestoreMinimizedWindows: () => true), "route Win+Shift+M when Shift was pressed before Windows");
shiftFirstShellRestoreShortcutGesture.KeyUp((uint)'M');
Check(WindowsKeyAction.Suppress, shiftFirstShellRestoreShortcutGesture.KeyUp(0x5b), "avoid opening Start after Shift-first Win+Shift+M");
var nativeMinimizeShortcutGesture = new WindowsKeyGesture();
nativeMinimizeShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeMinimizeShortcutGesture.KeyDown((uint)'M', canMinimizeAllWindows: () => false), "preserve native Win+M outside replacement-shell mode");
Check(WindowsKeyAction.PassThrough, nativeMinimizeShortcutGesture.KeyUp((uint)'M'), "pass native Win+M release through outside replacement-shell mode");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeMinimizeShortcutGesture.KeyUp(0x5b), "release Windows after passing native Win+M through");
var nativeRestoreShortcutGesture = new WindowsKeyGesture();
nativeRestoreShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeRestoreShortcutGesture.KeyDown((uint)'M', shiftPressed: true, canRestoreMinimizedWindows: () => false), "preserve native Win+Shift+M outside replacement-shell mode");
Check(WindowsKeyAction.PassThrough, nativeRestoreShortcutGesture.KeyUp((uint)'M'), "pass native Win+Shift+M release through outside replacement-shell mode");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeRestoreShortcutGesture.KeyUp(0x5b), "release Windows after passing native Win+Shift+M through");
var shellSystemAreaShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, shellSystemAreaShortcutGesture.KeyDown(0x5b), "capture Windows before routing shell-host notification-area focus");
Check(WindowsKeyAction.FocusTaskbarSystem, shellSystemAreaShortcutGesture.KeyDown((uint)'B', canFocusTaskbarSystem: () => true), "route Win+B to the custom taskbar system area when Explorer is absent");
Check(WindowsKeyAction.Suppress, shellSystemAreaShortcutGesture.KeyUp((uint)'B'), "suppress Win+B release after focusing the custom taskbar system area");
Check(WindowsKeyAction.Suppress, shellSystemAreaShortcutGesture.KeyUp(0x5b), "avoid opening Start after focusing the custom taskbar system area");
var shellPowerMenuShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, shellPowerMenuShortcutGesture.KeyDown(0x5b), "capture Windows before routing shell-host Power User menu");
Check(WindowsKeyAction.OpenPowerUserMenu, shellPowerMenuShortcutGesture.KeyDown((uint)'X', canOpenPowerUserMenu: () => true), "route Win+X to the custom Power User menu when Explorer is absent");
Check(WindowsKeyAction.Suppress, shellPowerMenuShortcutGesture.KeyUp((uint)'X'), "suppress Win+X release after opening the custom Power User menu");
Check(WindowsKeyAction.Suppress, shellPowerMenuShortcutGesture.KeyUp(0x5b), "avoid opening Start after opening the custom Power User menu");
var shellRunDialogShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
shellRunDialogShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.OpenRunDialog, shellRunDialogShortcutGesture.KeyDown((uint)'R', canOpenRunDialog: () => true), "route Win+R to the companion Run dialog when Explorer is absent");
Check(WindowsKeyAction.Suppress, shellRunDialogShortcutGesture.KeyUp((uint)'R'), "suppress Win+R release after opening the companion Run dialog");
Check(WindowsKeyAction.Suppress, shellRunDialogShortcutGesture.KeyUp(0x5b), "avoid opening Start after opening the companion Run dialog");
Check("apps,control-panel,power-options,event-viewer,system,device-manager,network-connections,disk-management,computer-management,terminal,task-manager,restart-explorer,taskbar-settings,settings",
    string.Join(',', ShellHostPowerMenuCatalog.SystemCommands.Select(command => command.Id)), "provide the standard shell-host Power User system commands");
Check("ms-settings:appsfeatures", ShellHostPowerMenuCatalog.SystemCommand("apps").Target, "open Installed apps from the Power User menu");
Check("control.exe", ShellHostPowerMenuCatalog.SystemCommand("control-panel").Target, "open Control Panel from the Power User menu");
CheckTrue(ShellHostPowerMenuCatalog.OpensCompanionShellLocation("CONTROL-PANEL"), "keep Control Panel inside the companion shell browser");
Check(false, ShellHostPowerMenuCatalog.OpensCompanionShellLocation("settings"), "keep regular Power User targets on their native handlers");
Check(SystemFlyoutService.TaskbarSettingsUri, ShellHostPowerMenuCatalog.SystemCommand("taskbar-settings").Target, "open Taskbar settings from the Power User menu");
Check<ShellHostPowerMenuCommand>(new("restart-explorer", "Restart Windows Explorer"), ShellHostPowerMenuCatalog.SystemCommand("restart-explorer"), "keep Explorer restart as a shell-host recovery command");
Throws<ArgumentOutOfRangeException>(() => ShellHostPowerMenuCatalog.SystemCommand("missing"), "reject unknown Power User menu commands");
var pendingShellInvocations = new ShellHostPendingInvocationQueue();
pendingShellInvocations.EnqueueFolder("C:\\Users\\Example");
pendingShellInvocations.EnqueueShellLocation("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
pendingShellInvocations.EnqueueFileLocation("C:\\Users\\Example\\report.docx");
var drainedShellInvocations = pendingShellInvocations.Drain();
Check(0, pendingShellInvocations.Count, "drain all pending shell-host startup invocations");
Check("C:\\Users\\Example|False|False|::{20D04FE0-3AEA-1069-A2D8-08002B30309D}|True|False|C:\\Users\\Example\\report.docx|False|True",
    string.Join('|', drainedShellInvocations.Select(invocation => $"{invocation.Value}|{invocation.IsShellLocation}|{invocation.IsFileLocation}")),
    "preserve ordered filesystem and virtual shell-host startup invocations");
Check("C:\\Program Files\\Example App\\tool.exe|--safe|two words",
    string.Join('|', RunCommandService.ParseCommandLine("\"C:\\Program Files\\Example App\\tool.exe\" --safe \"two words\"")), "parse quoted executable paths and arguments for the Run dialog");
var runStartInfo = RunCommandService.CreateStartInfo("\"C:\\Program Files\\Example App\\tool.exe\" --safe \"two words\"");
Check("C:\\Program Files\\Example App\\tool.exe", runStartInfo.FileName, "launch the parsed Run command with its executable path intact");
Check("--safe|two words", string.Join('|', runStartInfo.ArgumentList), "preserve parsed Run command arguments without re-quoting");
Check(true, runStartInfo.UseShellExecute, "allow the Run dialog to open documents and registered Internet locations");
Check(true, string.Equals(Path.Combine(Environment.SystemDirectory, "notepad.exe"), RunCommandService.CreateStartInfo(@"%SystemRoot%\System32\notepad.exe").FileName, StringComparison.OrdinalIgnoreCase), "expand environment variables in Run dialog commands");
Check("runas", RunCommandService.CreateStartInfo("taskmgr.exe", runAsAdministrator: true).Verb, "request UAC elevation only when Run as administrator is selected");
Throws<ArgumentException>(() => RunCommandService.CreateStartInfo("  "), "reject empty Run dialog commands");
var runHistoryPath = Path.Combine(Path.GetTempPath(), $"desktop-tuner-run-history-{Guid.NewGuid():N}.json");
try
{
    var runHistory = new RunCommandHistoryStore(runHistoryPath);
    Check(true, runHistory.TryRecord("notepad.exe"), "save successful Run commands to the recent-command history");
    Check(true, runHistory.TryRecord("calc.exe"), "save later Run commands to history");
    Check("calc.exe|notepad.exe", string.Join('|', runHistory.Load()), "show Run history in most-recent-first order");
    Check(true, runHistory.TryRecord("NOTEPAD.EXE"), "move a repeated Run command to the top without duplicating it");
    Check("NOTEPAD.EXE|calc.exe", string.Join('|', new RunCommandHistoryStore(runHistoryPath).Load()), "persist a normalized bounded history across dialog instances");
    Check(false, runHistory.TryRecord(" "), "do not save blank Run commands");
    for (var index = 0; index < RunCommandHistoryStore.MaximumEntries + 3; index++)
        Check(true, runHistory.TryRecord($"tool-{index}.exe"), "record a Run history entry");
    Check(RunCommandHistoryStore.MaximumEntries, runHistory.Load().Count, "bound saved Run history to twelve commands");
    Check(true, runHistory.TryClear(), "clear Run history from the dialog's history menu");
    Check(0, runHistory.Load().Count, "remove saved Run history entries when cleared");
}
finally
{
    if (File.Exists(runHistoryPath)) File.Delete(runHistoryPath);
}
var runCommandProcessInfo = RunCommandService.CreateStartInfo($"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /c exit 17");
runCommandProcessInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
using (var runCommandProcess = System.Diagnostics.Process.Start(runCommandProcessInfo)
    ?? throw new InvalidOperationException("Windows did not start the Run command integration test."))
{
    if (!runCommandProcess.WaitForExit(5000))
    {
        runCommandProcess.Kill(entireProcessTree: true);
        throw new InvalidOperationException("The Run command integration test did not exit within five seconds.");
    }
    Check(17, runCommandProcess.ExitCode, "launch Run commands with their parsed arguments through Windows ShellExecute");
}
Check("lock,sleep,hibernate,sign-out,shutdown,restart",
    string.Join(',', ShellHostPowerMenuCatalog.PowerActions.Select(action => action.Id)), "reuse all existing confirmed power actions in the Power User menu");
var nativePowerMenuShortcutGesture = new WindowsKeyGesture();
nativePowerMenuShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativePowerMenuShortcutGesture.KeyDown((uint)'X', canOpenPowerUserMenu: () => false), "preserve native Win+X outside replacement-shell mode");
Check(WindowsKeyAction.PassThrough, nativePowerMenuShortcutGesture.KeyUp((uint)'X'), "pass through native Win+X release outside replacement-shell mode");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativePowerMenuShortcutGesture.KeyUp(0x5b), "release native Windows key after passing through Win+X");
var nativeRunDialogShortcutGesture = new WindowsKeyGesture();
nativeRunDialogShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeRunDialogShortcutGesture.KeyDown((uint)'R', canOpenRunDialog: () => false), "preserve native Win+R outside replacement-shell mode");
Check(WindowsKeyAction.PassThrough, nativeRunDialogShortcutGesture.KeyUp((uint)'R'), "pass through native Win+R release outside replacement-shell mode");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeRunDialogShortcutGesture.KeyUp(0x5b), "release native Windows key after passing through Win+R");
var nativeSystemAreaShortcutGesture = new WindowsKeyGesture();
nativeSystemAreaShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeSystemAreaShortcutGesture.KeyDown((uint)'B', canFocusTaskbarSystem: () => false), "preserve native Win+B outside replacement-shell mode");
Check(WindowsKeyAction.PassThrough, nativeSystemAreaShortcutGesture.KeyUp((uint)'B'), "pass through native Win+B release outside replacement-shell mode");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeSystemAreaShortcutGesture.KeyUp(0x5b), "release native Windows key after passing through Win+B");
var nativeDesktopShortcutGesture = new WindowsKeyGesture();
nativeDesktopShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeDesktopShortcutGesture.KeyDown((uint)'D', canToggleDesktop: () => false), "preserve native Win+D outside replacement-shell mode");
Check(WindowsKeyAction.PassThrough, nativeDesktopShortcutGesture.KeyUp((uint)'D'), "pass through native Win+D release outside replacement-shell mode");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeDesktopShortcutGesture.KeyUp(0x5b), "release native Windows key after passing through Win+D");
var showDesktopWindows = ShowDesktopWindowPolicy.SelectWindowsToMinimize([
    new(1, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: false),
    new(2, IsVisible: false, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: false),
    new(3, IsVisible: true, IsMinimized: true, HasOwner: false, IsShellSurface: false, IsCloaked: false),
    new(4, IsVisible: true, IsMinimized: false, HasOwner: true, IsShellSurface: false, IsCloaked: false),
    new(5, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: true, IsCloaked: false),
    new(6, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: true),
    new(0, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: false),
    new(1, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: false)
]);
Check("1", string.Join(',', showDesktopWindows), "minimize only unique visible, unowned, uncloaked non-shell windows");
var showOtherWindows = ShowDesktopWindowPolicy.SelectWindowsToMinimize([
    new(1, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: false),
    new(2, IsVisible: true, IsMinimized: false, HasOwner: false, IsShellSurface: false, IsCloaked: false)
], foregroundWindow: 1);
Check("2", string.Join(',', showOtherWindows), "leave the foreground window visible for Win+Home");
var explorerOnlyBareKeyGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, explorerOnlyBareKeyGesture.KeyDown(0x5b), "capture a Windows-key tap while Explorer routing is enabled");
Check(WindowsKeyAction.ForwardWindowsTapThenSuppress, explorerOnlyBareKeyGesture.KeyUp(0x5b), "forward bare Windows-key taps to native Start when Start replacement is disabled");
var nativeExplorerShortcutGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
nativeExplorerShortcutGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeExplorerShortcutGesture.KeyDown((uint)'E', canOpenExplorer: () => false), "preserve native Win+E when companion Explorer routing is disabled");
Check(WindowsKeyAction.PassThrough, nativeExplorerShortcutGesture.KeyUp((uint)'E'), "pass through native Win+E release");
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeExplorerShortcutGesture.KeyUp(0x5b), "release Windows after passing native Win+E through");
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
var launchPinnedAppInstanceGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
launchPinnedAppInstanceGesture.KeyDown(0x5b);
Check(WindowsKeyAction.PassThrough, launchPinnedAppInstanceGesture.KeyDown(0xa0), "pass Shift while tracking shell-host Win+Shift+number");
Check(WindowsKeyAction.LaunchPinnedAppInstance, launchPinnedAppInstanceGesture.KeyDown((uint)'4', shiftPressed: true, canLaunchPinnedAppInstance: _ => true), "route Win+Shift+4 to a new instance of the fourth pinned app");
Check(4, launchPinnedAppInstanceGesture.TaskbarPinIndex, "retain the selected new-instance pin index");
Check(WindowsKeyAction.Suppress, launchPinnedAppInstanceGesture.KeyUp((uint)'4'), "suppress Win+Shift+4 release after launching the pinned app");
Check(WindowsKeyAction.Suppress, launchPinnedAppInstanceGesture.KeyUp(0x5b), "avoid opening Start after launching a pinned app instance");
var shiftFirstLaunchPinnedAppInstanceGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.PassThrough, shiftFirstLaunchPinnedAppInstanceGesture.KeyDown(0xa0), "pass a prior Shift press before Win+Shift+number");
shiftFirstLaunchPinnedAppInstanceGesture.KeyDown(0x5b);
Check(WindowsKeyAction.LaunchPinnedAppInstance, shiftFirstLaunchPinnedAppInstanceGesture.KeyDown((uint)'2', shiftPressed: true, canLaunchPinnedAppInstance: _ => true), "route Win+Shift+2 when Shift was pressed before Windows");
shiftFirstLaunchPinnedAppInstanceGesture.KeyUp((uint)'2');
Check(WindowsKeyAction.Suppress, shiftFirstLaunchPinnedAppInstanceGesture.KeyUp(0x5b), "consume Windows release after Shift-first Win+Shift+2");
var nativeNewInstanceGesture = new WindowsKeyGesture();
nativeNewInstanceGesture.KeyDown(0x5b);
nativeNewInstanceGesture.KeyDown(0xa0);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, nativeNewInstanceGesture.KeyDown((uint)'4', shiftPressed: true, canLaunchPinnedAppInstance: _ => false), "preserve native Win+Shift+number when no matching Desktop Tuner pin exists");
nativeNewInstanceGesture.KeyUp((uint)'4');
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, nativeNewInstanceGesture.KeyUp(0x5b), "release Windows after passing native Win+Shift+number through");
var modifiedNewInstanceGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
modifiedNewInstanceGesture.KeyDown(0x5b);
modifiedNewInstanceGesture.KeyDown(0xa0);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, modifiedNewInstanceGesture.KeyDown((uint)'4', controlPressed: true, shiftPressed: true, canLaunchPinnedAppInstance: _ => true), "preserve Ctrl+Win+Shift+number instead of launching an ordinary new instance");
var elevatedPinnedInstanceGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
elevatedPinnedInstanceGesture.KeyDown(0x5b);
elevatedPinnedInstanceGesture.KeyDown(0xa0);
Check(WindowsKeyAction.LaunchPinnedAppInstanceAsAdministrator, elevatedPinnedInstanceGesture.KeyDown((uint)'5', controlPressed: true, shiftPressed: true, canLaunchPinnedAppInstanceAsAdministrator: _ => true), "route Ctrl+Win+Shift+5 to launch the fifth pinned app elevated");
Check(5, elevatedPinnedInstanceGesture.TaskbarPinIndex, "retain the pin index for the elevated launch");
Check(WindowsKeyAction.Suppress, elevatedPinnedInstanceGesture.KeyUp((uint)'5'), "suppress the elevated pin shortcut key release");
Check(WindowsKeyAction.Suppress, elevatedPinnedInstanceGesture.KeyUp(0x5b), "avoid opening Start after the elevated pin shortcut");
var unsupportedElevatedPinGesture = new WindowsKeyGesture();
unsupportedElevatedPinGesture.KeyDown(0x5b);
unsupportedElevatedPinGesture.KeyDown(0xa0);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, unsupportedElevatedPinGesture.KeyDown((uint)'5', controlPressed: true, shiftPressed: true, canLaunchPinnedAppInstanceAsAdministrator: _ => false), "preserve Ctrl+Win+Shift+number when the pin cannot be elevated");
unsupportedElevatedPinGesture.KeyUp((uint)'5');
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, unsupportedElevatedPinGesture.KeyUp(0x5b), "release Windows after passing unsupported elevated pin shortcut through");
var lastActivePinnedAppGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
lastActivePinnedAppGesture.KeyDown(0x5b);
Check(WindowsKeyAction.PassThrough, lastActivePinnedAppGesture.KeyDown(0x11), "pass Control while tracking shell-host Win+Ctrl+number");
Check(WindowsKeyAction.ActivateLastActivePinnedApp, lastActivePinnedAppGesture.KeyDown((uint)'6', controlPressed: true, canActivateLastActivePinnedApp: _ => true), "route Win+Ctrl+6 to the last active window of the sixth pinned app");
Check(6, lastActivePinnedAppGesture.TaskbarPinIndex, "retain the selected last-active pin index");
Check(WindowsKeyAction.Suppress, lastActivePinnedAppGesture.KeyUp((uint)'6'), "suppress Win+Ctrl+number release after activation");
Check(WindowsKeyAction.PassThrough, lastActivePinnedAppGesture.KeyUp(0x11), "pass Control release through after Win+Ctrl+number");
Check(WindowsKeyAction.Suppress, lastActivePinnedAppGesture.KeyUp(0x5b), "avoid opening Start after Win+Ctrl+number activation");
var controlFirstLastActivePinGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
controlFirstLastActivePinGesture.KeyDown(0x11);
controlFirstLastActivePinGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ActivateLastActivePinnedApp, controlFirstLastActivePinGesture.KeyDown((uint)'2', controlPressed: true, canActivateLastActivePinnedApp: _ => true), "route Win+Ctrl+2 when Control was pressed before Windows");
controlFirstLastActivePinGesture.KeyUp((uint)'2');
controlFirstLastActivePinGesture.KeyUp(0x11);
Check(WindowsKeyAction.Suppress, controlFirstLastActivePinGesture.KeyUp(0x5b), "consume Windows release after Control-first Win+Ctrl+number");
var onScreenKeyboardGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, onScreenKeyboardGesture.KeyDown(0x5b), "capture Windows before shell-host Win+Ctrl+O");
Check(WindowsKeyAction.PassThrough, onScreenKeyboardGesture.KeyDown(0x11), "pass Control while tracking shell-host Win+Ctrl+O");
Check(WindowsKeyAction.OpenOnScreenKeyboard, onScreenKeyboardGesture.KeyDown((uint)'O', controlPressed: true, canOpenOnScreenKeyboard: () => true), "route Win+Ctrl+O to the On-Screen Keyboard");
Check(WindowsKeyAction.Suppress, onScreenKeyboardGesture.KeyUp((uint)'O'), "suppress Win+Ctrl+O release after opening the On-Screen Keyboard");
Check(WindowsKeyAction.PassThrough, onScreenKeyboardGesture.KeyUp(0x11), "pass Control release through after Win+Ctrl+O");
Check(WindowsKeyAction.Suppress, onScreenKeyboardGesture.KeyUp(0x5b), "avoid opening Start after Win+Ctrl+O");
var unavailableOnScreenKeyboardGesture = new WindowsKeyGesture();
unavailableOnScreenKeyboardGesture.KeyDown(0x5b);
Check(WindowsKeyAction.ForwardWindowsDownThenPass, unavailableOnScreenKeyboardGesture.KeyDown((uint)'O', controlPressed: true, canOpenOnScreenKeyboard: () => false), "preserve Win+Ctrl+O when shell-host routing is unavailable");
unavailableOnScreenKeyboardGesture.KeyUp((uint)'O');
Check(WindowsKeyAction.ForwardWindowsUpThenSuppress, unavailableOnScreenKeyboardGesture.KeyUp(0x5b), "release Windows after passing unsupported Win+Ctrl+O through");
var narratorGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, narratorGesture.KeyDown(0x5b), "capture Windows before shell-host Win+Ctrl+Enter");
Check(WindowsKeyAction.PassThrough, narratorGesture.KeyDown(0x11), "pass Control while tracking shell-host Win+Ctrl+Enter");
Check(WindowsKeyAction.OpenNarrator, narratorGesture.KeyDown(0x0D, controlPressed: true, canOpenNarrator: () => true), "route Win+Ctrl+Enter to Narrator in shell-host mode");
Check(WindowsKeyAction.Suppress, narratorGesture.KeyUp(0x0D), "suppress Narrator shortcut release");
Check(WindowsKeyAction.PassThrough, narratorGesture.KeyUp(0x11), "pass Control release through after Win+Ctrl+Enter");
Check(WindowsKeyAction.Suppress, narratorGesture.KeyUp(0x5b), "avoid opening Start after routing Narrator");
var snippingToolGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, snippingToolGesture.KeyDown(0x5b), "capture Windows before shell-host Win+Shift+S");
Check(WindowsKeyAction.PassThrough, snippingToolGesture.KeyDown(0x10), "pass Shift while tracking shell-host Win+Shift+S");
Check(WindowsKeyAction.OpenSnippingTool, snippingToolGesture.KeyDown((uint)'S', shiftPressed: true, canOpenSnippingTool: () => true), "route Win+Shift+S to Snipping Tool in shell-host mode");
Check(WindowsKeyAction.Suppress, snippingToolGesture.KeyUp((uint)'S'), "suppress Snipping Tool shortcut release");
Check(WindowsKeyAction.PassThrough, snippingToolGesture.KeyUp(0x10), "pass Shift release through after Win+Shift+S");
Check(WindowsKeyAction.Suppress, snippingToolGesture.KeyUp(0x5b), "avoid opening Start after routing Snipping Tool");
var copilotGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, copilotGesture.KeyDown(0x5b), "capture Windows before shell-host Win+C");
Check(WindowsKeyAction.OpenCopilot, copilotGesture.KeyDown((uint)'C', canOpenCopilot: () => true), "route Win+C to the native Copilot or Chat provider in shell-host mode");
Check(WindowsKeyAction.Suppress, copilotGesture.KeyUp((uint)'C'), "suppress Copilot shortcut release");
Check(WindowsKeyAction.Suppress, copilotGesture.KeyUp(0x5b), "avoid opening Start after routing Copilot");
var emojiGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, emojiGesture.KeyDown(0x5b), "capture Windows before shell-host Win+period");
Check(WindowsKeyAction.OpenEmojiPanel, emojiGesture.KeyDown(0xBE, canOpenEmojiPanel: () => true), "route Win+period to the native emoji panel in shell-host mode");
Check(WindowsKeyAction.Suppress, emojiGesture.KeyUp(0xBE), "suppress emoji panel shortcut release");
Check(WindowsKeyAction.Suppress, emojiGesture.KeyUp(0x5b), "avoid opening Start after routing the emoji panel");
var semicolonEmojiGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, semicolonEmojiGesture.KeyDown(0x5b), "capture Windows before shell-host Win+semicolon");
Check(WindowsKeyAction.OpenEmojiPanel, semicolonEmojiGesture.KeyDown(0xBA, canOpenEmojiPanel: () => true), "route Win+semicolon to the native emoji panel in shell-host mode");
Check(WindowsKeyAction.Suppress, semicolonEmojiGesture.KeyUp(0xBA), "suppress Win+semicolon shortcut release");
Check(WindowsKeyAction.Suppress, semicolonEmojiGesture.KeyUp(0x5b), "avoid opening Start after routing Win+semicolon");
var windowsTipGesture = new WindowsKeyGesture(replaceBareWindowsKey: false);
Check(WindowsKeyAction.Suppress, windowsTipGesture.KeyDown(0x5b), "capture Windows before shell-host Win+J");
Check(WindowsKeyAction.OpenWindowsTip, windowsTipGesture.KeyDown((uint)'J', canOpenWindowsTip: () => true), "route Win+J to the native Windows tip in shell-host mode");
Check(WindowsKeyAction.Suppress, windowsTipGesture.KeyUp((uint)'J'), "suppress Windows tip shortcut release");
Check(WindowsKeyAction.Suppress, windowsTipGesture.KeyUp(0x5b), "avoid opening Start after routing Windows tips");
var taskbarFocusGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyDown(0x5b), "capture Windows before focusing the custom taskbar");
Check(WindowsKeyAction.FocusTaskbar, taskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => true), "route Win+T to the custom taskbar when it is available");
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => true), "suppress Win+T repeat events while the shortcut is held");
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyUp((uint)'T'), "suppress the custom taskbar focus shortcut release");
Check(WindowsKeyAction.Suppress, taskbarFocusGesture.KeyUp(0x5b), "avoid opening Start after focusing the custom taskbar");
var reverseTaskbarFocusGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.Suppress, reverseTaskbarFocusGesture.KeyDown(0x5b), "capture Windows before reverse taskbar navigation");
Check(WindowsKeyAction.PassThrough, reverseTaskbarFocusGesture.KeyDown(0xa0), "preserve Shift state while waiting to distinguish reverse taskbar navigation");
Check(WindowsKeyAction.FocusTaskbarPrevious, reverseTaskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => true, shiftPressed: true), "route Win+Shift+T backward through custom taskbar buttons");
Check(WindowsKeyAction.Suppress, reverseTaskbarFocusGesture.KeyUp((uint)'T'), "suppress Win+Shift+T release after custom taskbar navigation");
Check(WindowsKeyAction.PassThrough, reverseTaskbarFocusGesture.KeyUp(0xa0), "restore Shift state after reverse taskbar navigation");
Check(WindowsKeyAction.Suppress, reverseTaskbarFocusGesture.KeyUp(0x5b), "avoid opening Start after reverse taskbar navigation");
var shiftFirstTaskbarFocusGesture = new WindowsKeyGesture();
Check(WindowsKeyAction.PassThrough, shiftFirstTaskbarFocusGesture.KeyDown(0xa1), "pass Shift through when it precedes the Windows key");
Check(WindowsKeyAction.Suppress, shiftFirstTaskbarFocusGesture.KeyDown(0x5c), "capture the right Windows key after Shift");
Check(WindowsKeyAction.FocusTaskbarPrevious, shiftFirstTaskbarFocusGesture.KeyDown((uint)'T', canFocusTaskbar: () => true, shiftPressed: true), "route reverse taskbar navigation when Shift is pressed first");
Check(WindowsKeyAction.Suppress, shiftFirstTaskbarFocusGesture.KeyUp((uint)'T'), "suppress the reverse taskbar shortcut key release");
Check(WindowsKeyAction.Suppress, shiftFirstTaskbarFocusGesture.KeyUp(0x5c), "suppress the Windows key release after reverse taskbar navigation");
Check(WindowsKeyAction.PassThrough, shiftFirstTaskbarFocusGesture.KeyUp(0xa1), "pass Shift release through after reverse taskbar navigation");
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
var taskbarNavigationDisplays = TaskbarKeyboardNavigationPolicy.OrderDisplays([
    new TaskbarDisplay("DISPLAY-LEFT", -1600, 0, 1600, 900, false),
    new TaskbarDisplay("DISPLAY-PRIMARY", 0, 0, 1920, 1080, true),
    new TaskbarDisplay("DISPLAY-TOP", 320, -1200, 1600, 1200, false)]);
Check("DISPLAY-PRIMARY,DISPLAY-TOP,DISPLAY-LEFT", string.Join(',', taskbarNavigationDisplays.Select(display => display.DeviceName)),
    "cycle taskbars with primary display first, then by physical position");
Check("DISPLAY-TOP", TaskbarKeyboardNavigationPolicy.SelectForForeground(taskbarNavigationDisplays, "DISPLAY-TOP")?.DeviceName,
    "focus Win+B on the taskbar that contains the foreground window");
Check("DISPLAY-PRIMARY", TaskbarKeyboardNavigationPolicy.SelectForForeground(taskbarNavigationDisplays, "MISSING")?.DeviceName,
    "fall back to the primary taskbar when the foreground display is unavailable");
Check("DISPLAY-PRIMARY", TaskbarKeyboardNavigationPolicy.SelectForForeground(taskbarNavigationDisplays, null)?.DeviceName,
    "start system-area focus on the primary taskbar when no foreground display is known");
Check<int?>(1, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(0, 3, forward: true), "advance Win+T to the next display's taskbar");
Check<int?>(2, TaskbarKeyboardNavigationPolicy.GetAdjacentIndex(0, 3, forward: false), "advance Win+Shift+T to the previous display's taskbar");
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
CheckTrue(TaskbarInteractionPolicy.ShouldLaunchNewPinnedInstance(System.Windows.Input.MouseButton.Middle), "use middle-click to launch a new pinned taskbar instance");
Check(false, TaskbarInteractionPolicy.ShouldLaunchNewPinnedInstance(System.Windows.Input.MouseButton.Left), "keep primary click on the existing pinned taskbar activation path");
CheckTrue(TaskbarInteractionPolicy.ShouldLaunchNewRunningInstance(System.Windows.Input.MouseButton.Middle, true), "launch a new running-app instance on middle-click when its executable is available");
Check(false, TaskbarInteractionPolicy.ShouldLaunchNewRunningInstance(System.Windows.Input.MouseButton.Middle, false), "keep middle-click close behavior when a running app has no launchable executable");
CheckTrue(SystemFlyoutService.GetEmojiPanelSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent(0xBE, false),
    new KeyboardKeyEvent(0xBE, true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+period emoji-panel shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetCopilotSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'C', false),
    new KeyboardKeyEvent((ushort)'C', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+C Copilot or Chat shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetWindowsTipSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'J', false),
    new KeyboardKeyEvent((ushort)'J', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+J Windows tip shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetNotificationAreaSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'B', false),
    new KeyboardKeyEvent((ushort)'B', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+B notification-area shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetInputMethodSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent(0x20, false),
    new KeyboardKeyEvent(0x20, true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+Space keyboard-layout picker shortcut in balanced key order");
Check("ms-settings:regionlanguage", SystemFlyoutService.LanguageSettingsUri, "keep the replacement keyboard-layout settings link on Windows' Language & region page");
Check("EN", SystemFlyoutService.FormatKeyboardLayoutLabel("00000409"), "show the active keyboard layout's language code");
Check("ABCD", SystemFlyoutService.FormatKeyboardLayoutLabel("ABCD"), "keep an unknown keyboard layout identifier readable");
Check<string?>(null, SystemFlyoutService.FormatKeyboardLayoutLabel(null), "leave the keyboard layout label empty when Windows provides no layout");
Check("IME on", SystemFlyoutService.FormatInputMethodStatus(0x1), "identify an active IME conversion mode");
Check("IME off", SystemFlyoutService.FormatInputMethodStatus(0), "identify an inactive IME conversion mode");
Check(TaskbarSearchAction.OpenWindowsSearch, TaskbarSearchPolicy.ResolveEnterAction(""), "open Windows Search when the replacement taskbar search box is empty");
Check(TaskbarSearchAction.SearchStartMenu, TaskbarSearchPolicy.ResolveEnterAction("control panel"), "send non-empty replacement taskbar searches to the companion Start search");
Check(true, TaskbarSearchPolicy.ShouldClearOnEscape("query"), "clear a replacement taskbar search query with Escape");
Check(false, TaskbarSearchPolicy.ShouldClearOnEscape(""), "leave an empty replacement taskbar search query available for native handling");
CheckTrue(SystemFlyoutService.GetOnScreenKeyboardSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent(0x11, false),
    new KeyboardKeyEvent((ushort)'O', false),
    new KeyboardKeyEvent((ushort)'O', true),
    new KeyboardKeyEvent(0x11, true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+Ctrl+O On-Screen Keyboard shortcut in balanced key order");
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
CheckTrue(SystemFlyoutService.GetWindowsSearchQuestionSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'Q', false),
    new KeyboardKeyEvent((ushort)'Q', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+Q search shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetClipboardHistorySequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'V', false),
    new KeyboardKeyEvent((ushort)'V', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+V clipboard history shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetVoiceTypingSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'H', false),
    new KeyboardKeyEvent((ushort)'H', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+H voice typing shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetSnapLayoutsSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'Z', false),
    new KeyboardKeyEvent((ushort)'Z', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+Z snap layouts shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetTaskViewSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent(0x09, false),
    new KeyboardKeyEvent(0x09, true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+Tab Task View shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetConnectSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'K', false),
    new KeyboardKeyEvent((ushort)'K', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+K Connect shortcut in balanced key order");
CheckTrue(SystemFlyoutService.GetProjectSequence().SequenceEqual(
[
    new KeyboardKeyEvent(0x5B, false),
    new KeyboardKeyEvent((ushort)'P', false),
    new KeyboardKeyEvent((ushort)'P', true),
    new KeyboardKeyEvent(0x5B, true)
]), "send the native Windows+P Project shortcut in balanced key order");
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
var appHostShellOverlayStartup = StartupShortcutService.BuildCommand(
    @"C:\Program Files\Desktop Tuner\DesktopTuner.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll",
    shellOverlayMode: true);
Check("--shell-overlay", appHostShellOverlayStartup.Arguments, "start the all-edition shell overlay from the packaged app host");
var dotnetStartup = StartupShortcutService.BuildCommand(
    @"C:\Program Files\dotnet\dotnet.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll");
Check(@"C:\Program Files\dotnet\dotnet.exe", dotnetStartup.TargetPath, "support development launches through dotnet at sign-in");
Check("\"C:\\Program Files\\Desktop Tuner\\DesktopTuner.dll\" --startup", dotnetStartup.Arguments, "quote an assembly path with spaces in the Startup shortcut");
var dotnetShellOverlayStartup = StartupShortcutService.BuildCommand(
    @"C:\Program Files\dotnet\dotnet.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll",
    shellOverlayMode: true);
Check("\"C:\\Program Files\\Desktop Tuner\\DesktopTuner.dll\" --shell-overlay", dotnetShellOverlayStartup.Arguments, "quote an assembly path for a development shell overlay Startup shortcut");
var shellOverlayStart = StartupShortcutService.BuildProcessStartInfo(
    @"C:\Program Files\dotnet\dotnet.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll",
    shellOverlayMode: true);
Check(@"C:\Program Files\dotnet\dotnet.exe", shellOverlayStart.FileName, "launch the all-edition shell overlay through the active app host");
Check("\"C:\\Program Files\\Desktop Tuner\\DesktopTuner.dll\" --shell-overlay", shellOverlayStart.Arguments, "preserve quoted development assembly arguments when transitioning into shell overlay mode");
Check("--shell-overlay", StartupShortcutService.BuildProcessStartInfo(
    @"C:\Program Files\Desktop Tuner\DesktopTuner.exe",
    @"C:\Program Files\Desktop Tuner\DesktopTuner.dll",
    shellOverlayMode: true).Arguments, "launch the installed executable directly into shell overlay mode");
Throws<ArgumentOutOfRangeException>(() => TaskbarLayoutCalculator.Calculate(0, 1080, new(TaskbarEdge.Bottom), false), "rejects invalid screen bounds");
var appModeSetting = SettingsCatalog.ById("explorer-app-mode");
var systemModeSetting = SettingsCatalog.ById("explorer-system-mode");
var transparencySetting = SettingsCatalog.ById("explorer-transparency");
var scrollbarSetting = SettingsCatalog.ById("explorer-scrollbars");
var trayIconsSetting = SettingsCatalog.ById("taskbar-tray-icons");
var taskbarSizeSetting = SettingsCatalog.ById("taskbar-size");
var taskbarClockSecondsSetting = SettingsCatalog.ById("taskbar-clock-seconds");
var taskbarShowDesktopSetting = SettingsCatalog.ById("taskbar-show-desktop");
var taskbarTaskViewSetting = SettingsCatalog.ById("taskbar-task-view");
var taskbarWidgetsSetting = SettingsCatalog.ById("taskbar-widgets");
var taskbarSearchModeSetting = SettingsCatalog.ById("taskbar-search-mode");
var fullPathSetting = SettingsCatalog.ById("explorer-full-path");
var separateExplorerProcessesSetting = SettingsCatalog.ById("explorer-separate-process");
var alignmentSetting = SettingsCatalog.ById("taskbar-alignment");
Check(SettingsCatalog.Personalize, appModeSetting.RegistryPath, "use shared Windows personalization registry location for app color mode");
Check("AppsUseLightTheme", appModeSetting.ValueName, "target Windows app color mode value");
Check("SystemUsesLightTheme", systemModeSetting.ValueName, "target Windows system color mode value");
Check("#1B2434", TaskbarTheme.Resolve(dark: false).Foreground, "use dark taskbar text in Windows light system mode");
Check("#F4F6FA", TaskbarTheme.Resolve(dark: true).Foreground, "use light taskbar text in Windows dark system mode");
Check("#202020", TaskbarTheme.Resolve(dark: true, TaskbarVisualStyle.Windows10).Background, "use a flat Windows 10 taskbar surface in dark mode");
Check("#20384C", TaskbarTheme.Resolve(dark: false, TaskbarVisualStyle.Windows7).Background, "use the classic Windows 7 taskbar palette independently of the system mode");
Check("#FFFFFF", TaskbarTheme.Resolve(dark: true, TaskbarVisualStyle.Windows7).Foreground, "keep readable text on the Windows 7 Aero taskbar");
Check(false, TaskbarTheme.UsesBackdrop(TaskbarVisualStyle.Windows7), "leave the Windows 7 Aero taskbar free of the Windows 11 DWM material");
Check(true, TaskbarTheme.UsesBackdrop(TaskbarVisualStyle.Windows11), "keep the DWM material for the Windows 11 taskbar");
Check(TaskbarSystemIconCatalog.FluentFontFamily, TaskbarSystemIconCatalog.GetFontFamily(TaskbarVisualStyle.Windows11), "use Windows 11's Fluent symbol font for the Windows 11 taskbar style");
Check(TaskbarSystemIconCatalog.LegacyFontFamily, TaskbarSystemIconCatalog.GetFontFamily(TaskbarVisualStyle.Windows10), "use the legacy Windows symbol font for the Windows 10 taskbar style");
Check("\uE74F", TaskbarSystemIconCatalog.GetVolumeGlyph(0.8f, muted: true), "show the muted speaker glyph when the default output is muted");
Check("\uE992", TaskbarSystemIconCatalog.GetVolumeGlyph(0, muted: false), "show the silent speaker glyph at zero output volume");
Check("\uE993", TaskbarSystemIconCatalog.GetVolumeGlyph(0.2f, muted: false), "show the low speaker glyph at low output volume");
Check("\uE994", TaskbarSystemIconCatalog.GetVolumeGlyph(0.5f, muted: false), "show the medium speaker glyph at medium output volume");
Check("\uE995", TaskbarSystemIconCatalog.GetVolumeGlyph(0.8f, muted: false), "show the high speaker glyph at high output volume");
var windows7TaskbarBackground = (LinearGradientBrush)TaskbarTheme.CreateBackground(dark: false, 70, TaskbarVisualStyle.Windows7);
Check(3, windows7TaskbarBackground.GradientStops.Count, "draw the Windows 7 Aero surface as a three-stop glass gradient");
Check((byte)77, windows7TaskbarBackground.GradientStops[0].Color.A, "apply taskbar transparency consistently to the Windows 7 Aero gradient");
Check((byte)77, ((SolidColorBrush)TaskbarTheme.CreateBackground(dark: false, 70)).Color.A, "apply the selected transparency to the light taskbar surface");
Check("EnableTransparency", transparencySetting.ValueName, "target Windows transparency setting");
Check(SettingsCatalog.Accessibility, scrollbarSetting.RegistryPath, "use the Windows accessibility registry location for scrollbar visibility");
Check("DynamicScrollbars", scrollbarSetting.ValueName, "target the Windows always-show-scrollbars preference");
Check(SettingsCatalog.ExplorerRoot, trayIconsSetting.RegistryPath, "use the Windows Explorer registry location for notification-area visibility");
Check("EnableAutoTray", trayIconsSetting.ValueName, "target the Windows notification-area collapse preference");
Check(SettingsCatalog.ExplorerCabinetState, fullPathSetting.RegistryPath, "use Windows Explorer's cabinet-state registry location");
Check("FullPath", fullPathSetting.ValueName, "target the documented full-path title-bar preference");
Check(1, fullPathSetting.Choices.Single(choice => choice.Label == "Show full path").Value, "map full-path title bars to the Explorer option");
Check("SeparateProcess", separateExplorerProcessesSetting.ValueName, "target separate Explorer folder processes");
Check(1, separateExplorerProcessesSetting.Choices.Single(choice => choice.Label == "Enabled").Value, "map process isolation to the Explorer option");
Check(0, appModeSetting.Choices.Single(choice => choice.Label == "Dark").Value, "map dark app mode to the Windows registry value");
Check(1, transparencySetting.Choices.Single(choice => choice.Label == "On").Value, "map enabled transparency to the Windows registry value");
Check(1, scrollbarSetting.Choices.Single(choice => choice.Label == "Always show").Value, "map always-visible scrollbars to the Windows registry value");
Check(0, trayIconsSetting.Choices.Single(choice => choice.Label == "Show all icons").Value, "map all-visible notification icons to the Windows registry value");
Check(SettingsCatalog.ExplorerAdvanced, taskbarSizeSetting.RegistryPath, "use Explorer advanced settings for taskbar size");
Check("TaskbarSi", taskbarSizeSetting.ValueName, "target the Windows taskbar size preference");
Check(0, taskbarSizeSetting.Choices.Single(choice => choice.Label == "Small").Value, "map compact taskbar size to the Windows registry value");
Check(1, taskbarSizeSetting.Choices.Single(choice => choice.Label == "Medium").Value, "map default taskbar size to the Windows registry value");
Check(2, taskbarSizeSetting.Choices.Single(choice => choice.Label == "Large").Value, "map large taskbar size to the Windows registry value");
Check(SettingsCatalog.ExplorerAdvanced, taskbarClockSecondsSetting.RegistryPath, "use Explorer advanced settings for taskbar clock seconds");
Check("ShowSecondsInSystemClock", taskbarClockSecondsSetting.ValueName, "target the Windows taskbar seconds preference");
Check(0, taskbarClockSecondsSetting.Choices.Single(choice => choice.Label == "Hide seconds").Value, "map hidden taskbar clock seconds to the Windows registry value");
Check(1, taskbarClockSecondsSetting.Choices.Single(choice => choice.Label == "Show seconds").Value, "map visible taskbar clock seconds to the Windows registry value");
Check(SettingsCatalog.ExplorerAdvanced, taskbarShowDesktopSetting.RegistryPath, "use Explorer advanced settings for the Show desktop button");
Check("TaskbarSd", taskbarShowDesktopSetting.ValueName, "target the Windows Show desktop button preference");
Check(1, taskbarShowDesktopSetting.Choices.Single(choice => choice.Label == "Show").Value, "map the visible Show desktop button to the Windows registry value");
Check(0, taskbarShowDesktopSetting.Choices.Single(choice => choice.Label == "Hide").Value, "map the hidden Show desktop button to the Windows registry value");
Check(SettingsCatalog.ExplorerAdvanced, taskbarTaskViewSetting.RegistryPath, "use Explorer advanced settings for the Task View button");
Check("TaskbarMn", taskbarTaskViewSetting.ValueName, "target the Windows Task View button preference");
Check(1, taskbarTaskViewSetting.Choices.Single(choice => choice.Label == "Show").Value, "map the visible Task View button to the Windows registry value");
Check(0, taskbarTaskViewSetting.Choices.Single(choice => choice.Label == "Hide").Value, "map the hidden Task View button to the Windows registry value");
Check(SettingsCatalog.ExplorerAdvanced, taskbarWidgetsSetting.RegistryPath, "use Explorer advanced settings for the Widgets button");
Check("TaskbarDa", taskbarWidgetsSetting.ValueName, "target the Windows Widgets button preference");
Check(1, taskbarWidgetsSetting.Choices.Single(choice => choice.Label == "Show").Value, "map the visible Widgets button to the Windows registry value");
Check(0, taskbarWidgetsSetting.Choices.Single(choice => choice.Label == "Hide").Value, "map the hidden Widgets button to the Windows registry value");
Check(SettingsCatalog.Search, taskbarSearchModeSetting.RegistryPath, "use the Windows Search registry location for taskbar search presentation");
Check("SearchboxTaskbarMode", taskbarSearchModeSetting.ValueName, "target the Windows taskbar search presentation preference");
Check(0, taskbarSearchModeSetting.Choices.Single(choice => choice.Label == "Hide").Value, "map hidden taskbar search to the Windows registry value");
Check(1, taskbarSearchModeSetting.Choices.Single(choice => choice.Label == "Icon").Value, "map icon taskbar search to the Windows registry value");
Check(2, taskbarSearchModeSetting.Choices.Single(choice => choice.Label == "Icon and label").Value, "map icon and label taskbar search to the Windows registry value");
Check(3, taskbarSearchModeSetting.Choices.Single(choice => choice.Label == "Search box").Value, "map taskbar search box to the Windows registry value");
Check((int)TaskbarButtonAlignment.Left, alignmentSetting.Choices.Single(choice => choice.Label == "Left").Value, "map left taskbar alignment to the overlay setting");
Check((int)TaskbarButtonAlignment.Center, alignmentSetting.Choices.Single(choice => choice.Label == "Center").Value, "map centered taskbar alignment to the overlay setting");
var temporaryPreferencesDirectory = Path.Combine(Path.GetTempPath(), $"DesktopTuner.Tests-{Guid.NewGuid():N}");
var preferencesPath = Path.Combine(temporaryPreferencesDirectory, "preferences.json");
try
{
    var nativeMenuFolder = Path.Combine(temporaryPreferencesDirectory, "NativeMenu");
    var nativeMenuOtherFolder = Path.Combine(temporaryPreferencesDirectory, "NativeMenuOther");
    Directory.CreateDirectory(nativeMenuFolder);
    Directory.CreateDirectory(nativeMenuOtherFolder);
    var nativeMenuNestedFolder = Path.Combine(nativeMenuFolder, "Nested");
    Directory.CreateDirectory(nativeMenuNestedFolder);
    var nativeMenuFirst = Path.Combine(nativeMenuFolder, "first.txt");
    var nativeMenuSecond = Path.Combine(nativeMenuFolder, "second.txt");
    var nativeMenuNestedMatch = Path.Combine(nativeMenuNestedFolder, "deep-needle.txt");
    File.WriteAllText(nativeMenuFirst, "first");
    File.WriteAllText(nativeMenuSecond, "second");
    File.WriteAllText(nativeMenuNestedMatch, "nested search fixture");
    var shellFolderEntries = await DesktopShellNamespaceCatalog.ReadChildrenAsync(nativeMenuFolder);
    var expectedShellFolderPaths = new HashSet<string>([nativeMenuFirst, nativeMenuSecond, nativeMenuNestedFolder], StringComparer.OrdinalIgnoreCase);
    var returnedShellFolderPaths = shellFolderEntries.Select(entry => Path.GetFullPath(entry.ParsingName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var expectedShellFilePaths = new HashSet<string>([nativeMenuFirst, nativeMenuSecond], StringComparer.OrdinalIgnoreCase);
    if (expectedShellFilePaths.IsSubsetOf(returnedShellFolderPaths))
    {
        CheckTrue(shellFolderEntries.Any(entry => Path.GetFullPath(entry.ParsingName).Equals(nativeMenuFirst, StringComparison.OrdinalIgnoreCase) && !entry.IsFolder), "enumerate filesystem children through the asynchronous Shell namespace browser");
        CheckTrue(shellFolderEntries.Any(entry => Path.GetFullPath(entry.ParsingName).Equals(nativeMenuSecond, StringComparison.OrdinalIgnoreCase) && !entry.IsFolder), "retain every Shell folder child in the namespace browser");
    }
    else
    {
        CheckTrue(returnedShellFolderPaths.IsSubsetOf(expectedShellFolderPaths), "tolerate headless Shell providers that return only a subset of the fixture folder");
    }
    if (returnedShellFolderPaths.Contains(Path.GetFullPath(nativeMenuNestedFolder)))
    {
        var shellSearchResults = await DesktopShellNamespaceCatalog.SearchAsync(nativeMenuFolder, "deep-needle");
        CheckTrue(shellSearchResults.Entries.Any(entry => Path.GetFullPath(entry.ParsingName).Equals(nativeMenuNestedMatch, StringComparison.OrdinalIgnoreCase) && !entry.IsFolder), "search recursively through Shell namespace folders");
    }
    Check(ShellNamespaceBrowserKeyboardAction.SelectAll, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.A, ModifierKeys.Control, itemListFocused: true, hasSelection: false), "select all Shell namespace items with Ctrl+A when the item list is focused");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.A, ModifierKeys.Control, itemListFocused: false, hasSelection: false), "preserve Ctrl+A text selection outside the Shell item list");
    Check(ShellNamespaceBrowserKeyboardAction.ClearSelection, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Escape, ModifierKeys.None, itemListFocused: true, hasSelection: true), "clear Shell namespace selection with Escape");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Escape, ModifierKeys.None, itemListFocused: true, hasSelection: false), "leave Escape available when the Shell item list has no selection");
    Check(ShellNamespaceBrowserKeyboardAction.ShowContextMenu, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.F10, ModifierKeys.Shift, itemListFocused: true, hasSelection: true), "open the native Shell context menu with Shift+F10");
    Check(ShellNamespaceBrowserKeyboardAction.ShowContextMenu, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Apps, ModifierKeys.None, itemListFocused: true, hasSelection: false), "open the native folder context menu with the Menu key");
    Check(ShellNamespaceBrowserKeyboardAction.ShowProperties, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, itemListFocused: true, hasSelection: true, systemKey: Key.Enter), "open native Shell Properties with Alt+Enter");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.System, ModifierKeys.Alt, itemListFocused: true, hasSelection: false, systemKey: Key.Enter), "leave Alt+Enter unhandled when no Shell item is selected");
    Check(ShellNamespaceBrowserKeyboardAction.Copy, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.C, ModifierKeys.Control, itemListFocused: true, hasSelection: true), "copy selected Shell namespace items with Ctrl+C");
    Check(ShellNamespaceBrowserKeyboardAction.Cut, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.X, ModifierKeys.Control, itemListFocused: true, hasSelection: true), "cut selected Shell namespace items with Ctrl+X");
    Check(ShellNamespaceBrowserKeyboardAction.Paste, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.V, ModifierKeys.Control, itemListFocused: true, hasSelection: false), "paste into the current Shell location with Ctrl+V");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.C, ModifierKeys.Control, itemListFocused: true, hasSelection: false), "leave Ctrl+C unhandled without a Shell selection");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.V, ModifierKeys.Control, itemListFocused: false, hasSelection: false), "preserve Ctrl+V outside the Shell item list");
    Check(ShellNamespaceBrowserKeyboardAction.Delete, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Delete, ModifierKeys.None, itemListFocused: true, hasSelection: true), "delete selected Shell items through their native verbs");
Check(ShellNamespaceBrowserKeyboardAction.Delete, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Delete, ModifierKeys.Shift, itemListFocused: true, hasSelection: true), "pass Shift+Delete through to the native Shell delete verb");
Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Delete, ModifierKeys.None, itemListFocused: true, hasSelection: false), "leave Delete unhandled when no Shell item is selected");
Check(ShellNamespaceBrowserKeyboardAction.Delete, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.Delete, ModifierKeys.None, itemListFocused: true, hasSelection: true), "keep the namespace browser Delete menu aligned with its native keyboard command");
    Check(ShellNamespaceBrowserKeyboardAction.Rename, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.F2, ModifierKeys.None, itemListFocused: true, hasSelection: true, canRename: true), "start inline rename for a selected Shell item that supports renaming");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.F2, ModifierKeys.None, itemListFocused: true, hasSelection: true), "leave F2 unhandled for a Shell item that does not support renaming");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.F2, ModifierKeys.None, itemListFocused: false, hasSelection: true, canRename: true), "preserve F2 when the Shell item list does not have focus");
    Check(ShellNamespaceBrowserKeyboardAction.None, ShellNamespaceBrowserKeyboardPolicy.Resolve(Key.F10, ModifierKeys.Control, itemListFocused: true, hasSelection: true), "preserve unrelated modified F10 shortcuts in the Shell browser");
    using (var canceledShellSearch = new CancellationTokenSource())
    {
        canceledShellSearch.Cancel();
        var wasCanceled = false;
        try { await DesktopShellNamespaceCatalog.SearchAsync(nativeMenuFolder, "deep-needle", canceledShellSearch.Token); }
        catch (OperationCanceledException) { wasCanceled = true; }
        CheckTrue(wasCanceled, "cancel a Shell namespace search before traversal starts");
    }
    CheckTrue(await NativeShellContextMenuService.ProbeItemsContextMenuAsync([nativeMenuFirst, nativeMenuSecond]), "build the Windows Shell context menu for a multi-selection");
    CheckTrue(await NativeShellContextMenuService.ProbeShellItemsContextMenuAsync([nativeMenuFirst, nativeMenuSecond]), "build the namespace browser's combined native context menu for a multi-selection");
    CheckTrue(await NativeShellContextMenuService.ProbeFolderBackgroundContextMenuAsync(nativeMenuFolder), "build the Windows Shell folder-background context menu");
    CheckTrue(await NativeShellContextMenuService.ProbeFolderBackgroundContextMenuAsync("shell:MyComputerFolder"), "build the Windows Shell background context menu for a virtual namespace folder");
    CheckTrue(await NativeShellContextMenuService.ProbeDesktopBackgroundContextMenuAsync(), "build the actual desktop Shell background context menu");
    CheckTrue(await NativeShellContextMenuService.ProbeShellItemContextMenuAsync("shell:RecycleBinFolder"), "build the Windows Shell context menu for the Recycle Bin namespace item");
    Check(2, NativeShellContextMenuPolicy.NormalizeSelection([nativeMenuFirst, nativeMenuSecond, nativeMenuFirst]).Count, "allow one native context menu for distinct items in the same folder");
    Check(2, NativeShellContextMenuPolicy.NormalizeShellSelection([nativeMenuFirst, nativeMenuSecond, nativeMenuFirst]).Count, "deduplicate items for a Shell namespace multi-selection");
    Throws<ArgumentException>(() => NativeShellContextMenuPolicy.NormalizeShellSelection(["", " "]), "reject an empty Shell namespace multi-selection");
    Throws<ArgumentException>(() => NativeShellContextMenuPolicy.NormalizeSelection([nativeMenuFirst, Path.Combine(nativeMenuOtherFolder, "third.txt")]), "reject mixed-parent native context menus");
    Check(0x40u, ShellFileOperationPolicy.GetDeleteFlags(permanentDelete: false), "route native Shell deletes to the Recycle Bin by default");
    Check(0x4000u, ShellFileOperationPolicy.GetDeleteFlags(permanentDelete: true), "request the Shell's permanent-delete warning for Shift+Delete");
    CheckTrue(ShellMultiPropertiesPolicy.ShouldUseMergedProperties(sameParent: false, allFileSystemItems: true, selectionCount: 2), "merge Properties for filesystem items from different folders");
    CheckTrue(!ShellMultiPropertiesPolicy.ShouldUseMergedProperties(sameParent: false, allFileSystemItems: false, selectionCount: 2), "keep provider-native Properties for mixed filesystem and virtual selections");
    CheckTrue(!ShellMultiPropertiesPolicy.ShouldUseMergedProperties(sameParent: true, allFileSystemItems: true, selectionCount: 2), "keep shared-folder Properties on the provider's native verb");
    CheckTrue(!ShellMultiPropertiesPolicy.ShouldUseMergedProperties(sameParent: false, allFileSystemItems: true, selectionCount: 1), "use the single-item Properties verb for one item");
    Throws<ArgumentException>(() => NativeShellContextMenuPolicy.NormalizeSelection([Path.GetPathRoot(temporaryPreferencesDirectory)!]), "reject drive-root native context menus");

    var explorerTestDirectory = Path.Combine(temporaryPreferencesDirectory, "ExplorerOperations");
    Directory.CreateDirectory(explorerTestDirectory);
    var propertiesTestFile = Path.Combine(explorerTestDirectory, "Details.txt");
    File.WriteAllText(propertiesTestFile, "properties fixture");
    var propertiesFileEntry = new ExplorerEntry("Details.txt", propertiesTestFile, false, false, 18, DateTime.Now);
    var propertiesDirectoryEntry = new ExplorerEntry("ExplorerOperations", explorerTestDirectory, true, false, null, DateTime.Now);
    CheckTrue(ExplorerPropertiesService.CanShowProperties(propertiesFileEntry), "offer the native Properties sheet for an existing file");
    CheckTrue(ExplorerPropertiesService.CanShowProperties(propertiesDirectoryEntry), "offer the native Properties sheet for an existing folder");
    CheckTrue(ExplorerPropertiesService.CanShowProperties([propertiesFileEntry, propertiesDirectoryEntry]), "offer one merged Properties sheet for multiple existing items");
    CheckTrue(ExplorerPropertiesService.CanCreateShellSelectionDataObject([propertiesFileEntry, propertiesDirectoryEntry]), "build the native Shell selection data object for mixed file and folder Properties");
    CheckTrue(!ExplorerPropertiesService.CanShowProperties([propertiesFileEntry, new ExplorerEntry("C:\\", "C:\\", true, true, null, DateTime.Now)]), "disable merged Properties when selection contains a drive-list entry");
    CheckTrue(!ExplorerPropertiesService.CanShowProperties(new ExplorerEntry("C:\\", "C:\\", true, true, null, DateTime.Now)), "keep drive-list entries out of file Properties selection");
    CheckTrue(!ExplorerPropertiesService.CanShowProperties(new ExplorerEntry("Missing", Path.Combine(explorerTestDirectory, "missing.txt"), false, false, null, DateTime.Now)), "disable Properties for a removed item");
    var recycledFileEntry = new ExplorerEntry("restored.txt", Path.Combine(explorerTestDirectory, "$R123-restored.txt"), false, false, 16, DateTime.Now)
    {
        IsRecycleBinItem = true,
        ShellItemPath = Path.Combine(explorerTestDirectory, "$R123-restored.txt"),
        OriginalLocation = explorerTestDirectory,
        RecycleDeleted = new DateTime(2025, 6, 1)
    };
    CheckTrue(!ExplorerPropertiesService.CanShowProperties(recycledFileEntry), "keep recycled Shell payload paths out of filesystem Properties handling");
    Check(explorerTestDirectory, ExplorerSelectionSummaryService.Resolve([recycledFileEntry]).Location, "show the original folder for a Recycle Bin selection");
    Check(new DateTime(2025, 6, 1).ToString("f"), ExplorerSelectionSummaryService.Resolve([recycledFileEntry]).Modified, "show the deletion date in Recycle Bin item details");
    CheckTrue(ExplorerRecycleBinService.ReadEntries().All(entry => entry.IsRecycleBinItem && !string.IsNullOrWhiteSpace(entry.ShellItemPath)), "enumerate Windows Recycle Bin entries as virtual Explorer rows");
    Check(true, ExplorerRecycleBinPolicy.ShouldShowEmptyCommand(true), "show Empty Recycle Bin in the Recycle Bin location");
    Check(false, ExplorerRecycleBinPolicy.ShouldShowEmptyCommand(false), "hide Empty Recycle Bin outside its virtual location");
    Check(true, ExplorerRecycleBinPolicy.CanEmpty(true, 1), "enable Empty Recycle Bin when it contains items");
    Check(false, ExplorerRecycleBinPolicy.CanEmpty(true, 0), "disable Empty Recycle Bin when there are no items");
    Check(false, ExplorerRecycleBinPolicy.CanEmpty(false, 1), "prevent Empty Recycle Bin from running outside its virtual location");
    Throws<ArgumentOutOfRangeException>(() => ExplorerRecycleBinPolicy.CanEmpty(true, -1), "reject invalid Recycle Bin item counts");
    var startShortcutDirectory = Path.Combine(temporaryPreferencesDirectory, "Start Shortcuts");
    Directory.CreateDirectory(startShortcutDirectory);
    var startShortcutPath = Path.Combine(startShortcutDirectory, "Editor Preview.lnk");
    File.WriteAllText(startShortcutPath, "test shortcut fixture");
    var startShortcut = new AppEntry("Editor", startShortcutPath);
    CheckTrue(startShortcut.CanOpenFileLocation, "offer file location for an existing Start shortcut");
    CheckTrue(startShortcut.CanPinToTaskbar, "offer taskbar pinning for an existing Start shortcut");
    var startFolder = new AppEntry("Projects", startShortcutDirectory, IsDirectory: true);
    CheckTrue(startFolder.CanPinToTaskbar, "offer taskbar pinning for an existing Start folder");
    var fileLocationLaunchInfo = AppCatalogService.BuildFileLocationLaunchInfo(startShortcut);
    Check("explorer.exe", fileLocationLaunchInfo.FileName, "open shortcut locations in File Explorer");
    Check($"/select,\"{startShortcutPath}\"", fileLocationLaunchInfo.ArgumentList.Single(), "select the original shortcut path including spaces");
    CheckTrue(fileLocationLaunchInfo.UseShellExecute, "open shortcut locations through the Windows shell");
    CheckTrue(!AppCatalogService.CanOpenFileLocation(new AppEntry("Missing", Path.Combine(startShortcutDirectory, "missing.lnk"))), "disable file location for a removed Start shortcut");
    CheckTrue(!AppCatalogService.CanOpenFileLocation(new AppEntry("Calculator", "CalculatorApp!App", IsPackagedApp: true)), "do not offer a file location for packaged Windows apps");
    var packagedTaskbarApp = new AppEntry("Calculator", "CalculatorApp!App", IsPackagedApp: true);
    CheckTrue(packagedTaskbarApp.CanPinToTaskbar, "offer taskbar pinning for packaged Windows apps");
    CheckTrue(StartPinCatalog.IsSupported(packagedTaskbarApp), "allow packaged Windows apps in Start pins");
    Throws<NotSupportedException>(() => AppCatalogService.BuildFileLocationLaunchInfo(new AppEntry("Missing", Path.Combine(startShortcutDirectory, "missing.lnk"))), "reject file location for a removed Start shortcut");
    var pinnedStartShortcut = new PinnedTaskbarApp("Editor", startShortcutPath);
    CheckTrue(pinnedStartShortcut.CanOpenLocation, "offer file location for an existing taskbar app pin");
    Check($"/select,\"{startShortcutPath}\"", TaskbarPinCatalog.BuildLocationLaunchInfo(pinnedStartShortcut).ArgumentList.Single(), "select the pinned app's executable or shortcut in Explorer");
    CheckTrue(pinnedStartShortcut.CanRunElevated, "allow elevated launch for a taskbar shortcut pin");
    CheckTrue(!TaskbarPinCatalog.CanRunAsAdministrator("Windows app", @"C:\Windows\System32\ApplicationFrameHost.exe"), "exclude the packaged-app frame host from elevated taskbar launch");
    var elevatedPinLaunchInfo = TaskbarPinCatalog.BuildElevatedLaunchInfo(pinnedStartShortcut);
    Check("runas", elevatedPinLaunchInfo.Verb, "request the Windows elevation prompt for a taskbar shortcut pin");
    CheckTrue(elevatedPinLaunchInfo.UseShellExecute, "launch elevated taskbar pins through the Windows shell");
    CheckTrue(TaskbarPinCatalog.IsSupportedPackagedTarget(packagedTaskbarApp.ShortcutPath), "recognize packaged app identifiers as taskbar targets");
    var packagedPins = TaskbarPinCatalog.AddPackaged([], packagedTaskbarApp.Name, packagedTaskbarApp.ShortcutPath);
    Check(true, packagedPins.Single().IsPackagedApp, "mark packaged taskbar pins for shell launching");
    Check("explorer.exe", TaskbarPinCatalog.BuildLaunchInfo(packagedPins.Single()).FileName, "launch packaged taskbar pins through Explorer's AppsFolder namespace");
    Check("shell:AppsFolder\\CalculatorApp!App", TaskbarPinCatalog.BuildLaunchInfo(packagedPins.Single()).ArgumentList.Single(), "pass the packaged app identity to the AppsFolder namespace");
    CheckTrue(!packagedPins.Single().CanOpenLocation, "hide file location for packaged taskbar pins");
    Check(false, packagedPins.Single().CanRunElevated, "hide elevation for packaged taskbar pins");
    CheckTrue(packagedPins.Single().CanShowJumpList, "allow Jump List menus for packaged taskbar pins");
    CheckTrue(!TaskbarPinIdentityService.Matches(packagedPins.Single(), new RunningWindow(nint.Zero, "Calculator", "Calculator", "", false)), "avoid matching a packaged pin when Windows exposes no app identity");
    var pinnedFolder = new PinnedTaskbarApp("ExplorerOperations", explorerTestDirectory, IsDirectory: true);
    CheckTrue(pinnedFolder.CanOpenLocation, "offer the folder itself for an existing taskbar folder pin");
    Check(explorerTestDirectory, TaskbarPinCatalog.BuildLocationLaunchInfo(pinnedFolder).FileName, "open a pinned folder directly in File Explorer");
    Check(false, pinnedFolder.CanRunElevated, "do not offer elevated launch for a taskbar folder pin");
    Throws<NotSupportedException>(() => TaskbarPinCatalog.BuildElevatedLaunchInfo(pinnedFolder), "reject elevated launch for a taskbar folder pin");
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
    var folderColumnWidths = new ExplorerColumnWidths(420, 180, 140, 115, 160, 165);
    var folderViewPreference = new ExplorerFolderViewPreference(ExplorerViewMode.LargeIcons, ExplorerSortColumn.DateModified, false, folderColumnWidths);
    Check(true, folderViewStore.Save(explorerTestDirectory, folderViewPreference), "save a folder's Explorer view preferences");
    Check(folderViewPreference, folderViewStore.Load(explorerTestDirectory), "restore saved Explorer view preferences");
    Check(folderViewPreference, folderViewStore.Load(explorerTestDirectory.ToUpperInvariant()), "match saved Explorer view preferences regardless of path casing");
    Check(folderColumnWidths, folderViewStore.Load(explorerTestDirectory)!.ColumnWidths, "restore resizable Details column widths for a folder");
    Check(false, folderViewStore.Save("relative-folder", folderViewPreference), "reject relative paths for saved Explorer folder views");
    Check(false, folderViewStore.Save(explorerTestDirectory, folderViewPreference with { ColumnWidths = folderColumnWidths with { Name = double.NaN } }), "reject non-finite saved Explorer column widths");
    Check(false, folderViewStore.Save(explorerTestDirectory, folderViewPreference with { ColumnWidths = folderColumnWidths with { Size = 5000 } }), "reject unreasonable saved Explorer column widths");
    var legacyFolderViewPath = Path.Combine(temporaryPreferencesDirectory, "legacy-folder-views.json");
    File.WriteAllText(legacyFolderViewPath, $"{{\"{explorerTestDirectory.Replace("\\", "\\\\")}\":{{\"ViewMode\":0,\"SortColumn\":0,\"SortAscending\":true}}}}");
    Check(null, new ExplorerFolderViewStore(legacyFolderViewPath).Load(explorerTestDirectory)!.ColumnWidths, "load older folder view preferences without column widths");
    var legacyColumnWidthsPath = Path.Combine(temporaryPreferencesDirectory, "legacy-column-widths.json");
    File.WriteAllText(legacyColumnWidthsPath, $"{{\"{explorerTestDirectory.Replace("\\", "\\\\")}\":{{\"ViewMode\":0,\"SortColumn\":0,\"SortAscending\":true,\"ColumnWidths\":{{\"Name\":400,\"DateModified\":170,\"Type\":125,\"Size\":100}}}}}}");
    var migratedColumnWidths = new ExplorerFolderViewStore(legacyColumnWidthsPath).Load(explorerTestDirectory)!.ColumnWidths;
    Check(155d, migratedColumnWidths!.DateCreated, "default new Date created column width when loading an older folder view");
    Check(155d, migratedColumnWidths.DateAccessed, "default new Date accessed column width when loading an older folder view");
    var corruptFolderViewPath = Path.Combine(temporaryPreferencesDirectory, "corrupt-folder-views.json");
    File.WriteAllText(corruptFolderViewPath, "invalid-json");
    Check(null, new ExplorerFolderViewStore(corruptFolderViewPath).Load(explorerTestDirectory), "recover from corrupt saved Explorer folder views");
    var explorerSessionStore = new ExplorerSessionStore(Path.Combine(temporaryPreferencesDirectory, "explorer-session.json"));
    var savedExplorerSession = new ExplorerSession(1,
    [
        new ExplorerTabSession(new ExplorerLocation(explorerTestDirectory), [new ExplorerLocation(null, IsHome: true)], ViewMode: ExplorerViewMode.LargeIcons),
        new ExplorerTabSession(new ExplorerLocation(null, IsHome: true, SearchQuery: "report"), [new ExplorerLocation(null, IsRecycleBin: true)], SortColumn: ExplorerSortColumn.DateModified, SortAscending: false, GroupDrives: false)
    ], DetailsPaneHeight: 245, DetailsPaneVisible: false);
    explorerSessionStore.Save(savedExplorerSession);
    var loadedExplorerSession = explorerSessionStore.Load();
    Check(1, loadedExplorerSession!.ActiveTabIndex, "restore the active companion Explorer tab");
    Check(2, loadedExplorerSession.Tabs.Count, "restore every open companion Explorer tab");
    Check(ExplorerViewMode.LargeIcons, loadedExplorerSession.Tabs[0].ViewMode, "restore each Explorer tab's view mode");
    Check(ExplorerSortColumn.DateModified, loadedExplorerSession.Tabs[1].SortColumn, "restore each Explorer tab's sort column");
    Check(false, loadedExplorerSession.Tabs[1].SortAscending, "restore each Explorer tab's sort direction");
    Check("report", loadedExplorerSession.Tabs[1].Location.SearchQuery, "restore a companion Explorer tab's search query");
    Check(true, loadedExplorerSession.Tabs[1].Back!.Single().IsRecycleBin, "restore Recycle Bin navigation history from an Explorer session");
    Check(new ExplorerLocation(null, IsHome: true), loadedExplorerSession.Tabs[0].Back!.Single(), "restore companion Explorer navigation history");
    Check(245d, loadedExplorerSession.DetailsPaneHeight, "restore the companion Explorer details pane height");
    Check(false, loadedExplorerSession.DetailsPaneVisible, "restore a hidden companion Explorer details pane");
    Check(false, loadedExplorerSession.OpenFoldersInNewTab, "restore the companion Explorer folder opening preference");
    Check(true, loadedExplorerSession.CommandRibbonVisible, "default the companion Explorer command ribbon to visible");
    var classicExplorerSessionPath = Path.Combine(temporaryPreferencesDirectory, "classic-explorer-session.json");
    var classicExplorerSessionStore = new ExplorerSessionStore(classicExplorerSessionPath);
    classicExplorerSessionStore.Save(savedExplorerSession with { CommandRibbonVisible = false });
    Check(false, classicExplorerSessionStore.Load()!.CommandRibbonVisible, "save and restore the classic Explorer command-only layout");
    Check(true, ExplorerFolderOpenPolicy.ShouldOpenInNewTab(true, isDirectory: true, isDrive: false), "open folders in a new tab when the preference is enabled");
    Check(false, ExplorerFolderOpenPolicy.ShouldOpenInNewTab(false, isDirectory: true, isDrive: false), "navigate in the active tab by default");
    Check(false, ExplorerFolderOpenPolicy.ShouldOpenInNewTab(true, isDirectory: true, isDrive: true), "keep drive rows out of the folder-in-new-tab preference");
    Check(false, ExplorerFolderOpenPolicy.ShouldOpenInNewTab(true, isDirectory: false, isDrive: false), "open files normally when folder-in-new-tab is enabled");
    var legacyExplorerSessionPath = Path.Combine(temporaryPreferencesDirectory, "legacy-explorer-session.json");
    File.WriteAllText(legacyExplorerSessionPath, "{\"ActiveTabIndex\":0,\"Tabs\":[{\"Location\":{\"IsHome\":true}}]}");
    Check(false, new ExplorerSessionStore(legacyExplorerSessionPath).Load()!.OpenFoldersInNewTab, "default the new Explorer preference when loading older sessions");
    var openFoldersInNewTabSessionPath = Path.Combine(temporaryPreferencesDirectory, "open-folders-in-new-tab.json");
    var openFoldersInNewTabStore = new ExplorerSessionStore(openFoldersInNewTabSessionPath);
    openFoldersInNewTabStore.Save(savedExplorerSession with { OpenFoldersInNewTab = true });
    Check(true, openFoldersInNewTabStore.Load()!.OpenFoldersInNewTab, "save and restore the Explorer new-tab preference");
    var boundedExplorerSessionStore = new ExplorerSessionStore(Path.Combine(temporaryPreferencesDirectory, "bounded-explorer-session.json"));
    boundedExplorerSessionStore.Save(new ExplorerSession(0, [new ExplorerTabSession(new ExplorerLocation(null, IsHome: true))], DetailsPaneHeight: 9000));
    Check(8000d, boundedExplorerSessionStore.Load()!.DetailsPaneHeight, "bound the restored Explorer details pane height");
    boundedExplorerSessionStore.Save(new ExplorerSession(99, Enumerable.Range(0, ExplorerSessionStore.MaximumTabs + 1)
        .Select(_ => new ExplorerTabSession(new ExplorerLocation(null, IsHome: true))).ToList()));
    Check(ExplorerSessionStore.MaximumTabs, boundedExplorerSessionStore.Load()!.Tabs.Count, "bound the number of restored Explorer tabs");
    Check(ExplorerSessionStore.MaximumTabs - 1, boundedExplorerSessionStore.Load()!.ActiveTabIndex, "clamp the restored active Explorer tab index");
    var corruptExplorerSessionPath = Path.Combine(temporaryPreferencesDirectory, "corrupt-explorer-session.json");
    File.WriteAllText(corruptExplorerSessionPath, "invalid-json");
    Check(null, new ExplorerSessionStore(corruptExplorerSessionPath).Load(), "recover from a corrupt saved Explorer session");
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
    var pathLocation = AppCatalogService.BuildPathLocationLaunchInfo(sourceFile);
    Check($"/select,\"{Path.GetFullPath(sourceFile)}\"", pathLocation.ArgumentList[0], "build a document location launch command");
    CheckTrue(new ExplorerEntry("draft.txt", sourceFile, false, false, new FileInfo(sourceFile).Length, File.GetLastWriteTime(sourceFile)).Icon is not null, "expose shell file icons to Explorer rows");
    CheckTrue(new ExplorerEntry("ExplorerOperations", explorerTestDirectory, true, false, null, Directory.GetLastWriteTime(explorerTestDirectory)).Icon is not null, "expose shell folder icons to Explorer rows");
    var editableEntry = new ExplorerEntry("draft.txt", sourceFile, false, false, 5, DateTime.Now);
    var renameChanges = new List<string>();
    editableEntry.PropertyChanged += (_, args) => renameChanges.Add(args.PropertyName!);
    editableEntry.RenameText = "notes.txt";
    editableEntry.IsRenaming = true;
    editableEntry.IsRenaming = false;
    Check("RenameText,IsRenaming,IsRenaming", string.Join(',', renameChanges), "notify Explorer name editors when their text and editing state change");
    var renamedFile = ExplorerFileOperationService.Rename(sourceFile, "notes.txt");
    Check(true, File.Exists(renamedFile), "rename a file within its current folder");
    Check(6, ExplorerRenamePolicy.GetInitialSelectionLength("report.pdf", isDirectory: false), "select a file name without its extension when renaming");
    Check(11, ExplorerRenamePolicy.GetInitialSelectionLength("archive.tar.gz", isDirectory: false), "preserve only the final file extension during rename");
    Check(10, ExplorerRenamePolicy.GetInitialSelectionLength(".gitignore", isDirectory: false), "select dotfile names without treating the leading dot as an extension");
    Check(10, ExplorerRenamePolicy.GetInitialSelectionLength("New folder", isDirectory: true), "select the full folder name when renaming");
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
    var searchableText = Path.Combine(explorerTestDirectory, "project-update.md");
    File.WriteAllText(searchableText, "Project update\nThe launch date is next Friday.");
    var boundaryText = Path.Combine(explorerTestDirectory, "boundary.txt");
    File.WriteAllText(boundaryText, new string('x', 4094) + "cross-boundary-phrase" + new string('y', 32));
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
    Check(searchableText, ExplorerSearchService.SearchAsync(explorerTestDirectory, "content:\"launch date\"").GetAwaiter().GetResult().Entries.Single().FullPath,
        "find a quoted phrase inside a text document without requiring the phrase in its file name");
    Check(searchableText, ExplorerSearchService.SearchAsync(explorerTestDirectory, "content:launch content:friday").GetAwaiter().GetResult().Entries.Single().FullPath,
        "require every content term to appear in the document");
    Check(boundaryText, ExplorerSearchService.SearchAsync(explorerTestDirectory, "content:cross-boundary-phrase").GetAwaiter().GetResult().Entries.Single().FullPath,
        "match text spanning the streaming read buffer boundary");
    var unsupportedContentSearch = ExplorerSearchService.SearchAsync(explorerTestDirectory, "content:invoice").GetAwaiter().GetResult();
    CheckTrue(unsupportedContentSearch.SkippedContentItems > 0, "report document formats that content search cannot read");
    CheckTrue(unsupportedContentSearch.Entries.All(entry => entry.FullPath != searchablePdf), "avoid treating unsupported document formats as content matches");
    CheckTrue(ExplorerContentSearch.CanSearch(searchableText, new FileInfo(searchableText).Length), "allow bounded plain-text files in content searches");
    CheckTrue(!ExplorerContentSearch.CanSearch(searchablePdf, new FileInfo(searchablePdf).Length), "exclude binary document formats from plain-text content search");
    CheckTrue(!ExplorerContentSearch.CanSearch(searchableText, ExplorerContentSearch.MaximumFileBytes + 1), "skip oversized text files instead of reading them without a bound");
    using (var canceledContentSearch = new CancellationTokenSource())
    {
        canceledContentSearch.Cancel();
        Throws<OperationCanceledException>(() => ExplorerContentSearch.ContainsAll(searchableText, ["launch"], canceledContentSearch.Token),
            "cancel content scanning before reading a file");
    }
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
    Throws<ArgumentException>(() => ExplorerSearchQuery.Parse("content:"), "explain empty content search filters");
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
        new ExplorerEntry("first.txt", @"C:\Docs\first.txt", false, false, 5, sharedModifiedDate) { Created = new DateTime(2025, 1, 1), Accessed = new DateTime(2025, 2, 2) },
        new ExplorerEntry("second.txt", @"C:\Docs\second.txt", false, false, 7, sharedModifiedDate) { Created = new DateTime(2025, 1, 1), Accessed = new DateTime(2025, 2, 2) }
    };
    var fileSelectionSummary = ExplorerSelectionSummaryService.Resolve(selectedFiles);
    Check("2 items selected", fileSelectionSummary.Name, "summarize a multi-file Explorer selection");
    Check("2 files", fileSelectionSummary.Type, "show selected file counts in Explorer details");
    Check(@"C:\Docs", fileSelectionSummary.Location, "show the common parent for a multi-file Explorer selection");
    Check("12 B", fileSelectionSummary.Size, "sum known file sizes in Explorer details");
    Check(sharedModifiedDate.ToString("f"), fileSelectionSummary.Modified, "show a shared modified date for selected files");
    Check(new DateTime(2025, 1, 1).ToString("f"), fileSelectionSummary.Created, "show a shared creation date for selected files");
    Check(new DateTime(2025, 2, 2).ToString("f"), fileSelectionSummary.Accessed, "show a shared access date for selected files");
    var singleFileSummary = ExplorerSelectionSummaryService.Resolve([selectedFiles[0]]);
    Check(new DateTime(2025, 1, 1).ToString("f"), singleFileSummary.Created, "show the creation date for one selected file");
    Check(new DateTime(2025, 2, 2).ToString("f"), singleFileSummary.Accessed, "show the access date for one selected file");
    var mixedSelectionSummary = ExplorerSelectionSummaryService.Resolve(
    [
        .. selectedFiles,
        new ExplorerEntry("folder", @"C:\Docs\folder", true, false, null, sharedModifiedDate)
    ]);
    Check("2 files, 1 folder", mixedSelectionSummary.Type, "summarize mixed file and folder selections");
    Check("Some dates unavailable", mixedSelectionSummary.Created, "explain unavailable creation dates in a mixed selection");
    Check("Some dates unavailable", mixedSelectionSummary.Accessed, "explain unavailable access dates in a mixed selection");
    Check("12 B · folder sizes not included", mixedSelectionSummary.Size, "state explicitly that selected folder sizes are not included");
    Check("Multiple locations", ExplorerSelectionSummaryService.Resolve(
    [
        selectedFiles[0],
        new ExplorerEntry("other.txt", @"D:\Other\other.txt", false, false, 1, new DateTime(2025, 2, 4))
    ]).Location, "report when selected Explorer items come from different locations");
    Throws<ArgumentException>(() => ExplorerSelectionSummaryService.Resolve([]), "reject an empty Explorer selection summary");
    var explorerSortEntries = new[]
    {
        new ExplorerEntry("z-folder", @"C:\items\z-folder", true, false, null, new DateTime(2024, 1, 1)) { Created = new DateTime(2024, 1, 4), Accessed = new DateTime(2024, 1, 1) },
        new ExplorerEntry("a.txt", @"C:\items\a.txt", false, false, 20, new DateTime(2024, 1, 2)) { Created = new DateTime(2024, 1, 2), Accessed = new DateTime(2024, 1, 2) },
        new ExplorerEntry("b-folder", @"C:\items\b-folder", true, false, null, new DateTime(2024, 1, 3)) { Created = new DateTime(2024, 1, 1), Accessed = new DateTime(2024, 1, 4) },
        new ExplorerEntry("b.log", @"C:\items\b.log", false, false, 5, new DateTime(2024, 1, 4)) { Created = new DateTime(2024, 1, 3), Accessed = new DateTime(2024, 1, 3) }
    };
    Check("z-folder,b-folder,b.log,a.txt", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.Name, ascending: false).Select(entry => entry.Name)), "sort names descending while keeping folders grouped first");
    Check("b-folder,z-folder,b.log,a.txt", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.DateModified, ascending: false).Select(entry => entry.Name)), "sort by modification date with directory grouping");
    Check("b-folder,z-folder,a.txt,b.log", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.Type, ascending: false).Select(entry => entry.Name)), "sort by type with folders grouped first");
    Check("b-folder,z-folder,a.txt,b.log", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.Size, ascending: false).Select(entry => entry.Name)), "sort files by numeric size instead of formatted size text");
    Check("b-folder,z-folder,a.txt,b.log", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.DateCreated, ascending: true).Select(entry => entry.Name)), "sort by creation date while keeping folders grouped first");
    Check("b-folder,z-folder,b.log,a.txt", string.Join(',', ExplorerSortPolicy.Sort(explorerSortEntries, ExplorerSortColumn.DateAccessed, ascending: false).Select(entry => entry.Name)), "sort by last-access date descending while keeping folders grouped first");

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
    Check("Alpha (C:),Zeta (Z:),USB (E:),Share (N:)", string.Join(',', ExplorerDriveCatalog.Sort(driveEntries, ExplorerSortColumn.DateCreated, ascending: true).Select(entry => entry.Name)), "support creation-date sorting for grouped This PC drives");

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
    Check(TaskbarWindowDisplayMode.AllTaskbars, freshPreferencesStore.Load().TaskbarWindowDisplayMode, "show app windows on every taskbar by default");
    Check(false, freshPreferencesStore.Load().TaskbarShowWindowsFromAllVirtualDesktops, "show only current virtual desktop windows by default");
    Check(TaskbarStyle.EdgeToEdge, freshPreferencesStore.Load().TaskbarLayout, "default a new install to the full-edge taskbar layout");
    Check(false, freshPreferencesStore.Load().CenterStartMenu, "default new installs to taskbar-aligned Start menus");
    Check(false, freshPreferencesStore.Load().FolderShellIntegrationEnabled, "keep folder context menu integration opt-in on new installs");
Check(false, freshPreferencesStore.Load().ReplaceExplorerShortcut, "keep Win+E Explorer routing opt-in on new installs");
Check(false, freshPreferencesStore.Load().TaskbarWeather!.Enabled, "keep taskbar weather opt-in on new installs");
Check(false, TaskbarWeatherPolicy.Normalize(new TaskbarWeatherSettings(Enabled: true)).Enabled, "require a valid chosen location before enabling taskbar weather");
Check(false, TaskbarWeatherPolicy.Normalize(new TaskbarWeatherSettings(Enabled: true, Latitude: 95, Longitude: 10)).Enabled, "disable weather settings with out-of-range coordinates");
Check("°F", TaskbarWeatherPolicy.GetTemperatureUnit("US"), "use Fahrenheit for US taskbar weather");
Check("°C", TaskbarWeatherPolicy.GetTemperatureUnit("GB"), "use Celsius for UK taskbar weather");
Check("Thunderstorm with hail", TaskbarWeatherPolicy.GetCondition(99, true), "describe severe thunderstorm taskbar weather codes");
Check("☾", TaskbarWeatherPolicy.GetGlyph(0, false), "show a night glyph for clear night conditions");
Check(TaskbarWeatherAnimation.Sun, TaskbarWeatherPolicy.GetAnimation(0), "animate clear-sky taskbar weather with a sun cycle");
Check(TaskbarWeatherAnimation.Rain, TaskbarWeatherPolicy.GetAnimation(63), "animate rainy taskbar weather with a rain motion");
Check(TaskbarWeatherAnimation.Snow, TaskbarWeatherPolicy.GetAnimation(75), "animate snowy taskbar weather with a snow motion");
Check(TaskbarWeatherAnimation.Storm, TaskbarWeatherPolicy.GetAnimation(99), "animate storm taskbar weather with a warning motion");
Check(TaskbarWeatherAnimation.None, TaskbarWeatherPolicy.GetAnimation(3), "keep overcast taskbar weather stable");
var snapLeft = new RunningWindow((nint)101, "Editor", "Editor", "C:\\Apps\\Editor.exe", false) { DisplayDeviceName = "DISPLAY1", Bounds = new TaskbarBounds(0, 0, 960, 1080) };
var snapRight = new RunningWindow((nint)102, "Browser", "Browser", "C:\\Apps\\Browser.exe", false) { DisplayDeviceName = "DISPLAY1", Bounds = new TaskbarBounds(960, 0, 960, 1080) };
Check(1, TaskbarSnapGroupPolicy.Detect([snapLeft, snapRight]).Count, "detect adjacent same-display windows as a snap group");
var unrelatedWindow = snapRight with { Handle = (nint)103, Bounds = new TaskbarBounds(1200, 100, 500, 400) };
Check(0, TaskbarSnapGroupPolicy.Detect([snapLeft, unrelatedWindow]).Count, "leave non-adjacent same-display windows ungrouped");
var shellNewText = new ShellNewItem(".txt", "Text Document", null, true);
Check("New Text Document.txt", shellNewText.CreateName(), "name ShellNew documents with their registered extension");
var shellNewTemplate = new ShellNewItem(".docx", "Word Document", @"C:\Templates\blank.docx", false);
Check(@"C:\Templates\blank.docx", shellNewTemplate.TemplatePath, "retain a ShellNew template path for native file creation");
CheckTrue(ShellNewItemCatalog.MaximumItems > 0, "bound the number of ShellNew templates in the replacement desktop menu");
var shellNewItems = ShellNewItemCatalog.Read();
CheckTrue(shellNewItems.Count <= ShellNewItemCatalog.MaximumItems && shellNewItems.All(item => item.Extension.StartsWith('.') && !string.IsNullOrWhiteSpace(item.Label)), "read bounded, labeled ShellNew templates for the replacement desktop menu");
Check(new TaskbarCurrentWeather(21.5, "°F", 2, true), TaskbarWeatherPolicy.ParseCurrentResponse("""{"current":{"temperature_2m":21.5,"weather_code":2,"is_day":1}}""", "fahrenheit"), "parse current weather API conditions and unit");
Throws<System.IO.InvalidDataException>(() => TaskbarWeatherPolicy.ParseCurrentResponse("{}", "celsius"), "reject weather responses without current conditions");
    var staleTaskbarSnapshot = Path.Combine(temporaryPreferencesDirectory, "taskbar-restore.json");
    File.WriteAllText(staleTaskbarSnapshot, """[{"Handle":-1,"WasVisible":true}]""");
    NativeTaskbarVisibilityService.RestoreSnapshot(staleTaskbarSnapshot);
    Check(false, File.Exists(staleTaskbarSnapshot), "clean up a valid taskbar recovery snapshot after skipping a stale window handle");
    var orphanedTaskbarSnapshotDirectory = Path.Combine(temporaryPreferencesDirectory, "orphaned-taskbar-snapshots");
    Directory.CreateDirectory(orphanedTaskbarSnapshotDirectory);
    var missingOwnerSnapshot = Path.Combine(orphanedTaskbarSnapshotDirectory, "taskbar-restore-2147483647.json");
    File.WriteAllText(missingOwnerSnapshot, """[{"Handle":-1,"WasVisible":true}]""");
    var currentOwnerSnapshot = Path.Combine(orphanedTaskbarSnapshotDirectory, $"taskbar-restore-{Environment.ProcessId}.json");
    File.WriteAllText(currentOwnerSnapshot, """[{"Handle":-1,"WasVisible":true}]""");
    Check(1, NativeTaskbarVisibilityService.RestoreOrphanedSnapshots(orphanedTaskbarSnapshotDirectory), "restore only snapshots whose owning process is no longer running");
    Check(false, File.Exists(missingOwnerSnapshot), "remove a recovered snapshot for an exited process");
    Check(true, File.Exists(currentOwnerSnapshot), "preserve taskbar recovery snapshots owned by a live process");
    File.SetLastWriteTimeUtc(currentOwnerSnapshot, DateTime.UtcNow.AddDays(-1));
    Check(1, NativeTaskbarVisibilityService.RestoreOrphanedSnapshots(orphanedTaskbarSnapshotDirectory), "recognize a recycled process id when its snapshot predates the new process");
    Check(false, File.Exists(currentOwnerSnapshot), "remove an orphaned snapshot whose process id has been reused");
    var preferencesStore = new DesktopPreferencesStore(preferencesPath);
    var savedStartPlaces = StartMenuPlaceCatalog.Normalize(new StartMenuPlacePreferences(["run", "documents"], ["documents", "run"]));
    var savedControlPanelApplets = ControlPanelAppletCatalog.Normalize(new ControlPanelAppletPreferences(
        ["network-sharing", "programs", "credential-manager"], ["programs", "network-sharing"]));
    var savedTaskbarButtons = TaskbarSystemButtonVisibility.Default
        .WithVisibility(TaskbarSystemButton.Search, false)
        .WithVisibility(TaskbarSystemButton.Emoji, false)
        .WithVisibility(TaskbarSystemButton.Microphone, false)
        .WithVisibility(TaskbarSystemButton.InputMethod, false)
        .WithVisibility(TaskbarSystemButton.OnScreenKeyboard, false)
        .WithVisibility(TaskbarSystemButton.Widgets, false);
    var savedWeather = new TaskbarWeatherSettings(true, "Seattle, Washington", "Seattle, Washington, United States", 47.6062, -122.3321);
    var expectedPreferences = new DesktopPreferences(TaskbarEdge.Left, TaskbarSize.Large, true,
        [new PinnedTaskbarApp("Projects", @"C:\Users\test\Projects", true)], true, StartMenuStyle.Classic, false, TaskbarStyle.Floating,
        PinnedStartApps: [new AppEntry("Editor", @"C:\Apps\Editor.lnk", TileSize: StartTileSize.Wide, GroupName: "Dev")], ReplaceNativeTaskbar: true, TaskbarDynamicTransparency: true, TaskbarButtonEffect: TaskbarButtonEffect.DynamicAura, StartMenuPlaces: savedStartPlaces, StartRecentAppCount: 8, TaskbarSystemButtons: savedTaskbarButtons, CenterStartMenu: true, TaskbarWindowDisplayMode: TaskbarWindowDisplayMode.PrimaryAndTaskbarOnWhichWindowIsOpen, TaskbarShowWindowsFromAllVirtualDesktops: true, ControlPanelApplets: savedControlPanelApplets, TaskbarWeather: savedWeather, StartMenuIconSize: StartMenuIconSize.Large, StartOpenAllApps: true);
    preferencesStore.Save(expectedPreferences);
    var loadedPreferences = preferencesStore.Load();
    Check(expectedPreferences.TaskbarEdge, loadedPreferences.TaskbarEdge, "persist taskbar edge");
    Check(expectedPreferences.TaskbarSize, loadedPreferences.TaskbarSize, "persist taskbar size");
    Check(expectedPreferences.StartMenuStyle, loadedPreferences.StartMenuStyle, "persist Start menu style");
    Check(StartMenuIconSize.Large, loadedPreferences.StartMenuIconSize, "persist Start menu icon size");
    Check(true, loadedPreferences.CenterStartMenu, "persist centered Start menu preference");
    Check(true, loadedPreferences.StartOpenAllApps, "persist opening Start directly to All apps");
    Check(savedWeather, loadedPreferences.TaskbarWeather, "persist opt-in weather location and coordinates");
    Check(TaskbarWindowDisplayMode.PrimaryAndTaskbarOnWhichWindowIsOpen, loadedPreferences.TaskbarWindowDisplayMode, "persist the taskbar app display mode");
    Check(true, loadedPreferences.TaskbarShowWindowsFromAllVirtualDesktops, "persist showing app windows from all virtual desktops");
    preferencesStore.Save(expectedPreferences with { FolderShellIntegrationEnabled = true });
    Check(true, preferencesStore.Load().FolderShellIntegrationEnabled, "persist folder context menu integration");
    preferencesStore.Save(expectedPreferences with { ReplaceExplorerShortcut = true });
    Check(true, preferencesStore.Load().ReplaceExplorerShortcut, "persist Win+E Explorer routing");
    preferencesStore.Save(expectedPreferences);
    Check("Editor", loadedPreferences.PinnedStartApps!.Single().Name, "persist pinned Start apps");
    Check(StartTileSize.Wide, loadedPreferences.PinnedStartApps!.Single().TileSize, "persist a pinned Start tile's size");
    Check("Dev", loadedPreferences.PinnedStartApps!.Single().GroupName, "persist a pinned Start tile's group");
    Check(string.Join(',', savedStartPlaces.Order!), string.Join(',', loadedPreferences.StartMenuPlaces!.Order!), "persist custom Start system-place order");
    Check(string.Join(',', savedStartPlaces.Visible!), string.Join(',', loadedPreferences.StartMenuPlaces.Visible!), "persist custom Start system-place visibility");
    Check(string.Join(',', savedControlPanelApplets.Order!), string.Join(',', loadedPreferences.ControlPanelApplets!.Order!), "persist custom Control Panel applet order");
    Check(string.Join(',', savedControlPanelApplets.Visible!), string.Join(',', loadedPreferences.ControlPanelApplets.Visible!), "persist custom Control Panel applet visibility");
preferencesStore.Save(expectedPreferences with { PinnedApps = [new PinnedTaskbarApp("This PC", "shell:MyComputerFolder", IsShellNamespace: true)] });
var loadedShellPin = preferencesStore.Load().PinnedApps!.Single();
Check(true, loadedShellPin.IsShellNamespace, "persist Shell namespace taskbar pin identity");
Check("shell:MyComputerFolder", loadedShellPin.ExecutablePath, "persist Shell namespace taskbar pin parsing name");
preferencesStore.Save(expectedPreferences with { PinnedApps = [new PinnedTaskbarApp("Editor", Environment.ProcessPath!, PinnedDestinations: [new TaskbarJumpListDestination("DesktopTuner", Environment.CurrentDirectory)])] });
var loadedPinnedDestination = preferencesStore.Load().PinnedApps!.Single().PinnedDestinations!.Single();
Check("DesktopTuner", loadedPinnedDestination.Name, "persist pinned Jump List destination labels");
Check(Path.GetFullPath(Environment.CurrentDirectory), loadedPinnedDestination.ParsingName, "persist pinned Jump List destination paths");
preferencesStore.Save(expectedPreferences with { PinnedStartApps = [new AppEntry("This PC", "shell:MyComputerFolder", IsDirectory: true, IsShellNamespace: true)] });
var loadedShellStartPin = preferencesStore.Load().PinnedStartApps!.Single();
Check(true, loadedShellStartPin.IsShellNamespace, "persist Shell namespace Start pin identity");
Check("shell:MyComputerFolder", loadedShellStartPin.ShortcutPath, "persist Shell namespace Start pin parsing name");
preferencesStore.Save(expectedPreferences);
    Check(8, loadedPreferences.StartRecentAppCount, "persist the configured Start recent-app count");
    Check(false, loadedPreferences.TaskbarSystemButtons!.IsVisible(TaskbarSystemButton.Emoji), "persist hidden taskbar emoji button");
    Check(false, loadedPreferences.TaskbarSystemButtons.IsVisible(TaskbarSystemButton.Microphone), "persist hidden taskbar microphone button");
    Check(false, loadedPreferences.TaskbarSystemButtons.IsVisible(TaskbarSystemButton.InputMethod), "persist hidden taskbar keyboard-layout button");
    Check(false, loadedPreferences.TaskbarSystemButtons.IsVisible(TaskbarSystemButton.OnScreenKeyboard), "persist hidden taskbar On-Screen Keyboard button");
    Check(false, loadedPreferences.TaskbarSystemButtons.IsVisible(TaskbarSystemButton.Widgets), "persist hidden taskbar Widgets button");
    Check(false, loadedPreferences.TaskbarSystemButtons.IsVisible(TaskbarSystemButton.Search), "persist hidden taskbar Search button");
    Check(true, loadedPreferences.TaskbarSystemButtons.IsVisible(TaskbarSystemButton.Clock), "preserve enabled taskbar clock visibility");
    Check(true, TaskbarSystemButtonVisibility.Normalize(null).IsVisible(TaskbarSystemButton.Emoji), "default older taskbar preferences to all system buttons visible");
    Check(true, TaskbarSystemButtonVisibility.Normalize(null).IsVisible(TaskbarSystemButton.InputMethod), "default keyboard-layout button to visible for older taskbar preferences");
    Check(true, TaskbarSystemButtonVisibility.Normalize(null).IsVisible(TaskbarSystemButton.OnScreenKeyboard), "default On-Screen Keyboard button to visible for older taskbar preferences");
    Check(true, TaskbarSystemButtonVisibility.Normalize(null).IsVisible(TaskbarSystemButton.Microphone), "default microphone button to visible for older taskbar preferences");
    Check(true, TaskbarSystemButtonVisibility.Normalize(null).IsVisible(TaskbarSystemButton.Search), "default Search button to visible for older taskbar preferences");
    var legacyTaskbarButtons = JsonSerializer.Deserialize<TaskbarSystemButtonVisibility>("""{"Settings":false,"Network":false}""")!;
    Check(true, legacyTaskbarButtons.IsVisible(TaskbarSystemButton.InputMethod), "default keyboard-layout visibility when loading older custom system-button preferences");
    Check(true, legacyTaskbarButtons.IsVisible(TaskbarSystemButton.OnScreenKeyboard), "default On-Screen Keyboard visibility when loading older custom system-button preferences");
    Check(true, legacyTaskbarButtons.IsVisible(TaskbarSystemButton.Microphone), "default microphone visibility when loading older custom system-button preferences");
    preferencesStore.Save(expectedPreferences with { StartRecentAppCount = 0 });
    Check(0, preferencesStore.Load().StartRecentAppCount, "allow disabling the Start recent-app section");
    preferencesStore.Save(expectedPreferences with { StartRecentAppCount = StartRecentAppsStore.MaximumEntries });
    Check(StartRecentAppsStore.MaximumEntries, preferencesStore.Load().StartRecentAppCount, "allow the maximum Start recent-app count");
    preferencesStore.Save(expectedPreferences with { StartRecentAppCount = StartRecentAppsStore.MaximumEntries + 1 });
    Check(StartRecentAppsStore.MaximumEntries, preferencesStore.Load().StartRecentAppCount, "clamp excessive Start recent-app counts");
    preferencesStore.Save(expectedPreferences with { StartRecentAppCount = -1 });
    Check(0, preferencesStore.Load().StartRecentAppCount, "clamp negative Start recent-app counts");
    Check(expectedPreferences.TaskbarOnAllDisplays, loadedPreferences.TaskbarOnAllDisplays, "persist taskbar display coverage");
    Check(expectedPreferences.TaskbarLayout, loadedPreferences.TaskbarLayout, "persist floating taskbar style");
    Check(true, loadedPreferences.ReplaceNativeTaskbar, "persist native taskbar replacement mode");
    Check(true, loadedPreferences.TaskbarDynamicTransparency, "persist adaptive taskbar transparency");
    Check(TaskbarButtonEffect.DynamicAura, loadedPreferences.TaskbarButtonEffect, "persist the Dynamic Aura button effect");
    preferencesStore.Save(expectedPreferences with { TaskbarVisualStyle = TaskbarVisualStyle.Windows7 });
    Check(TaskbarVisualStyle.Windows7, preferencesStore.Load().TaskbarVisualStyle, "persist the Windows 7 Aero taskbar visual style");
    preferencesStore.Save(expectedPreferences with { TaskbarVisualStyle = TaskbarVisualStyle.Windows10 });
    Check(TaskbarVisualStyle.Windows10, preferencesStore.Load().TaskbarVisualStyle, "persist the Windows 10 taskbar visual style");
    preferencesStore.Save(expectedPreferences with { StartMenuStyle = StartMenuStyle.Windows7 });
    Check(StartMenuStyle.Windows7, preferencesStore.Load().StartMenuStyle, "persist the Windows 7-inspired Start menu style");
    preferencesStore.Save(expectedPreferences with { StartMenuStyle = StartMenuStyle.Windows8 });
    Check(StartMenuStyle.Windows8, preferencesStore.Load().StartMenuStyle, "persist the Windows 8-inspired Start tile style");
    preferencesStore.Save(expectedPreferences with { StartMenuStyle = StartMenuStyle.Windows10 });
    Check(StartMenuStyle.Windows10, preferencesStore.Load().StartMenuStyle, "persist the Windows 10-inspired Start tile layout");
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
    preferencesStore.Save(expectedPreferences with { TaskbarLayout = TaskbarStyle.DockLike });
    Check(TaskbarStyle.DockLike, preferencesStore.Load().TaskbarLayout, "persist the apps-dock taskbar style");
    preferencesStore.Save(expectedPreferences with { TaskbarGrouping = TaskbarGroupingMode.Never });
    Check(TaskbarGroupingMode.Never, preferencesStore.Load().TaskbarGrouping, "persist ungrouped taskbar mode");
    preferencesStore.Save(expectedPreferences with { TaskbarButtonAlignment = TaskbarButtonAlignment.Left });
    Check(TaskbarButtonAlignment.Left, preferencesStore.Load().TaskbarButtonAlignment, "persist left-aligned taskbar buttons");
    preferencesStore.Save(expectedPreferences with { TaskbarShowLabels = false, TaskbarIconSize = TaskbarIconSize.Large });
    Check(false, preferencesStore.Load().TaskbarShowLabels, "persist hidden taskbar labels");
    Check(TaskbarIconSize.Large, preferencesStore.Load().TaskbarIconSize, "persist large taskbar icons");
    preferencesStore.Save(expectedPreferences with { TaskbarLabelVisibility = TaskbarLabelVisibility.WhenFull });
    Check(TaskbarLabelVisibility.WhenFull, preferencesStore.Load().TaskbarLabelVisibility, "persist taskbar labels when-full mode");
    preferencesStore.Save(expectedPreferences with { TaskbarSearchStyle = TaskbarSearchStyle.Box });
    Check(TaskbarSearchStyle.Box, preferencesStore.Load().TaskbarSearchStyle, "persist the taskbar search-box mode");
    preferencesStore.Save(expectedPreferences with { TaskbarSearchStyle = TaskbarSearchStyle.None });
    Check(TaskbarSearchStyle.None, preferencesStore.Load().TaskbarSearchStyle, "persist the hidden taskbar search mode");
    preferencesStore.Save(expectedPreferences with { TaskbarSearchStyle = TaskbarSearchStyle.IconAndLabel });
    Check(TaskbarSearchStyle.IconAndLabel, preferencesStore.Load().TaskbarSearchStyle, "persist the labeled taskbar search mode");
    Check(new TouchMenuMetrics(new System.Windows.Thickness(10, 7, 10, 7), 32), TouchTargetPolicy.Resolve(hasTouchInput: false), "keep context menus compact for pointer input");
    Check(new TouchMenuMetrics(new System.Windows.Thickness(14, 11, 14, 11), 44), TouchTargetPolicy.Resolve(hasTouchInput: true), "expand context-menu hit targets for touch input");
    preferencesStore.Save(expectedPreferences with { TaskbarButtonSpacing = TaskbarButtonSpacing.Relaxed });
    Check(TaskbarButtonSpacing.Relaxed, preferencesStore.Load().TaskbarButtonSpacing, "persist relaxed taskbar button spacing");
    Check(true, loadedPreferences.PinnedApps!.Single().IsDirectory, "persist folder pin type");

    File.WriteAllText(preferencesPath, """{"TaskbarEdge":0,"PinnedApps":[{"Name":"Legacy app","ExecutablePath":"C:\\Apps\\Editor.exe"}],"PinnedStartApps":[{"Name":"Legacy Start app","ShortcutPath":"C:\\Apps\\legacy.lnk"}]}""");
    Check(StartMenuStyle.Modern, preferencesStore.Load().StartMenuStyle, "default legacy preferences to the Modern Start menu");
    Check(false, preferencesStore.Load().TaskbarOnAllDisplays, "keep legacy taskbar preferences on the primary display");
    Check(false, preferencesStore.Load().ReplaceNativeTaskbar, "keep native taskbar replacement disabled for legacy preferences");
    Check(false, preferencesStore.Load().TaskbarShowWindowsFromAllVirtualDesktops, "default older preferences to the current virtual desktop");
    Check(false, preferencesStore.Load().StartWithWindows, "disable sign-in startup for older preference files");
    Check(4, preferencesStore.Load().StartRecentAppCount, "default older preferences to four recent Start apps");
    Check(StartMenuIconSize.Standard, preferencesStore.Load().StartMenuIconSize, "default older preferences to standard Start menu icons");
    Check(5, preferencesStore.Load().TaskbarTransparency, "default taskbar transparency for older preference files");
    Check(false, preferencesStore.Load().TaskbarDynamicTransparency, "disable adaptive transparency for older preference files");
    Check(TaskbarButtonEffect.Accent, preferencesStore.Load().TaskbarButtonEffect, "default older preference files to the Windows accent button effect");
    Check(TaskbarVisualStyle.Windows11, preferencesStore.Load().TaskbarVisualStyle, "default older preference files to the Windows 11 taskbar visual style");
    Check(TaskbarStyle.EdgeToEdge, preferencesStore.Load().TaskbarLayout, "default legacy preferences to a full-edge taskbar");
    Check(TaskbarGroupingMode.Always, preferencesStore.Load().TaskbarGrouping, "default legacy preferences to grouped taskbar buttons");
    Check(TaskbarButtonAlignment.Center, preferencesStore.Load().TaskbarButtonAlignment, "default legacy preferences to centered taskbar buttons");
    Check(true, preferencesStore.Load().TaskbarShowLabels, "default legacy preferences to visible taskbar labels");
    Check(TaskbarLabelVisibility.Always, preferencesStore.Load().TaskbarLabelVisibility, "default legacy preferences to always-visible taskbar labels");
    Check(TaskbarIconSize.Standard, preferencesStore.Load().TaskbarIconSize, "default legacy preferences to standard taskbar icons");
    Check(TaskbarButtonSpacing.Standard, preferencesStore.Load().TaskbarButtonSpacing, "default legacy preferences to standard button spacing");
    Check(TaskbarSearchStyle.Button, preferencesStore.Load().TaskbarSearchStyle, "default legacy preferences to the taskbar search button");
    Check(24, preferencesStore.Load().StartMenuPlaces!.Visible!.Count, "show all Start places for older preference files");
    Check(43, preferencesStore.Load().ControlPanelApplets!.Visible!.Count, "show all Control Panel applets for older preference files");
    Check(false, preferencesStore.Load().PinnedApps!.Single().IsDirectory, "default old pin records to app launch behavior");
    Check(StartPinCatalog.DefaultGroupName, preferencesStore.Load().PinnedStartApps!.Single().GroupName, "default older Start pin records to the Pinned group");
    File.WriteAllText(preferencesPath, """{"TaskbarEdge":0,"TaskbarButtonEffect":99}""");
    Check(TaskbarButtonEffect.Accent, preferencesStore.Load().TaskbarButtonEffect, "reject an unknown taskbar button effect and fall back to the default");
    File.WriteAllText(preferencesPath, """{"TaskbarEdge":0,"TaskbarVisualStyle":99}""");
    Check(TaskbarVisualStyle.Windows11, preferencesStore.Load().TaskbarVisualStyle, "reject an unknown taskbar visual style and fall back to defaults");

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
