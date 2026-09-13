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
Console.WriteLine($"Passed {count} taskbar layout and auto-hide checks.");

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
