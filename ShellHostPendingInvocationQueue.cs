namespace DesktopTuner;

public readonly record struct ShellHostPendingInvocation(string Value, bool IsShellLocation, bool IsFileLocation = false);

public sealed class ShellHostPendingInvocationQueue
{
    private readonly Queue<ShellHostPendingInvocation> _pending = new();

    public int Count => _pending.Count;

    public void EnqueueFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        _pending.Enqueue(new ShellHostPendingInvocation(folderPath, IsShellLocation: false));
    }

    public void EnqueueFileLocation(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _pending.Enqueue(new ShellHostPendingInvocation(filePath, IsShellLocation: false, IsFileLocation: true));
    }

    public void EnqueueShellLocation(string shellLocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellLocation);
        _pending.Enqueue(new ShellHostPendingInvocation(shellLocation, IsShellLocation: true));
    }

    public IReadOnlyList<ShellHostPendingInvocation> Drain()
    {
        var values = _pending.ToArray();
        _pending.Clear();
        return values;
    }
}
