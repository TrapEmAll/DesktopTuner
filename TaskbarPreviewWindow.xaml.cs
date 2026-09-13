using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTuner;

public partial class TaskbarPreviewWindow : Window
{
    private const uint ThumbnailRectDestination = 0x00000001;
    private const uint ThumbnailOpacity = 0x00000004;
    private const uint ThumbnailVisible = 0x00000008;
    private const int HResultOk = 0;
    private readonly List<(RunningWindow Window, Border Surface)> _items = [];
    private readonly Dictionary<nint, nint> _thumbnails = [];
    private readonly DispatcherTimer _layoutTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private TaskbarDisplay _display;
    private bool _closed;

    public TaskbarPreviewWindow(IReadOnlyList<RunningWindow> windows, TaskbarDisplay display, TaskbarEdge edge, Button placementTarget)
    {
        InitializeComponent();
        _display = display;
        Owner = Window.GetWindow(placementTarget);
        _layoutTimer.Tick += (_, _) => UpdateThumbnailLayouts();

        var availableWidth = Math.Max(250, display.Width / display.ScaleX - 16);
        Width = Math.Min(760, availableWidth);
        CardsPanel.Width = Math.Max(Width - 20, windows.Count * 236d);
        foreach (var window in windows)
        {
            var surface = new Border
            {
                Width = 220,
                Height = 132,
                Background = new SolidColorBrush(Color.FromRgb(36, 42, 55)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(82, 94, 116)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true
            };
            var title = new TextBlock
            {
                Text = window.Title,
                Foreground = Brushes.White,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(3, 8, 3, 1),
                Width = 218
            };
            var button = new Button
            {
                Tag = window,
                ToolTip = window.Title,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(4, 2, 4, 2),
                Content = new StackPanel { Children = { surface, title } }
            };
            button.Click += PreviewButton_Click;
            CardsPanel.Children.Add(button);
            _items.Add((window, surface));
        }

        Loaded += (_, _) => PlaceNearTarget(placementTarget, edge);
    }

    public bool Matches(IReadOnlyList<RunningWindow> windows) =>
        _items.Select(item => item.Window.Handle).SequenceEqual(windows.Select(window => window.Handle));

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        foreach (var (window, _) in _items)
        {
            if (window.Handle == IntPtr.Zero || !IsWindow(window.Handle)) continue;
            var result = DwmRegisterThumbnail(handle, window.Handle, out var thumbnail);
            if (result == HResultOk) _thumbnails[window.Handle] = thumbnail;
            else Trace.TraceWarning($"Could not create a taskbar preview for '{window.Title}' (HRESULT 0x{result:X8}).");
        }

        LayoutUpdated += (_, _) => QueueThumbnailLayoutUpdate();
        _layoutTimer.Start();
        UpdateThumbnailLayouts();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _layoutTimer.Stop();
        foreach (var thumbnail in _thumbnails.Values)
        {
            var result = DwmUnregisterThumbnail(thumbnail);
            if (result != HResultOk) Trace.TraceWarning($"Could not release a taskbar preview (HRESULT 0x{result:X8}).");
        }
        _thumbnails.Clear();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private void CardsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => QueueThumbnailLayoutUpdate();

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RunningWindow window }) RunningWindowService.Activate(window);
        Close();
    }

    private void PlaceNearTarget(Button target, TaskbarEdge edge)
    {
        try
        {
            _display = TaskbarDisplayService.ReadWindowDpi(_display, this);
            var targetTopLeft = target.PointToScreen(new Point(0, 0));
            var targetBottomRight = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
            var anchor = new TaskbarBounds(targetTopLeft.X, targetTopLeft.Y,
                targetBottomRight.X - targetTopLeft.X, targetBottomRight.Y - targetTopLeft.Y);
            var bounds = TaskbarPreviewLayoutPolicy.Calculate(_display, edge, anchor, Width, Height);
            if (!TaskbarDisplayService.PositionWindow(this, bounds))
                Trace.TraceWarning("Windows could not position the taskbar preview window.");
        }
        catch (InvalidOperationException ex)
        {
            Trace.TraceWarning($"Could not position taskbar window previews: {ex.Message}");
        }
    }

    private void QueueThumbnailLayoutUpdate()
    {
        if (!_closed && !_layoutTimer.IsEnabled) _layoutTimer.Start();
    }

    private void UpdateThumbnailLayouts()
    {
        _layoutTimer.Stop();
        if (_closed || !IsLoaded) return;

        var destinationHandle = new WindowInteropHelper(this).Handle;
        if (!GetClientRect(destinationHandle, out _) || !ClientToScreen(destinationHandle, out var clientOrigin)) return;
        foreach (var (window, surface) in _items)
        {
            if (!_thumbnails.TryGetValue(window.Handle, out var thumbnail)) continue;
            try
            {
                var topLeft = surface.PointToScreen(new Point(0, 0));
                var bottomRight = surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight));
                var properties = new ThumbnailProperties
                {
                    Flags = ThumbnailRectDestination | ThumbnailOpacity | ThumbnailVisible,
                    Destination = new NativeRect
                    {
                        Left = (int)Math.Round(topLeft.X - clientOrigin.X),
                        Top = (int)Math.Round(topLeft.Y - clientOrigin.Y),
                        Right = (int)Math.Round(bottomRight.X - clientOrigin.X),
                        Bottom = (int)Math.Round(bottomRight.Y - clientOrigin.Y)
                    },
                    Opacity = 255,
                    Visible = true,
                    SourceClientAreaOnly = false
                };
                var result = DwmUpdateThumbnailProperties(thumbnail, ref properties);
                if (result != HResultOk) Trace.TraceWarning($"Could not update a taskbar preview for '{window.Title}' (HRESULT 0x{result:X8}).");
            }
            catch (InvalidOperationException ex)
            {
                Trace.TraceWarning($"Could not position preview for '{window.Title}': {ex.Message}");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint handle, out NativePoint point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);
}
