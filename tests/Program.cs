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
