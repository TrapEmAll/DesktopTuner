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
    private const string PreviewWindowDragFormat = "DesktopTuner.TaskbarPreviewWindow";
    private const uint ThumbnailRectDestination = 0x00000001;
    private const uint ThumbnailOpacity = 0x00000004;
    private const uint ThumbnailVisible = 0x00000008;
    private const int HResultOk = 0;
    private readonly List<(RunningWindow Window, Border Surface, StackPanel Card)> _items = [];
    private readonly Dictionary<nint, nint> _thumbnails = [];
    private readonly DispatcherTimer _layoutTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private TaskbarDisplay _display;
    private bool _closed;
    private Point _dragStart;

    public TaskbarPreviewWindow(IReadOnlyList<RunningWindow> windows, TaskbarDisplay display, TaskbarEdge edge, Button placementTarget, Action<RunningWindow, RunningWindow> moveWindow)
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
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true
            };
            surface.SetResourceReference(Border.BackgroundProperty, "TaskbarPreviewCardBrush");
            surface.SetResourceReference(Border.BorderBrushProperty, "TaskbarPreviewBorderBrush");
            var title = new TextBlock
            {
                Text = window.Title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0)
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TaskbarForegroundBrush");
            var previewButton = new Button
            {
                Tag = window,
                ToolTip = window.Title,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Content = surface
            };
            previewButton.Click += PreviewButton_Click;
            var closeButton = new Button
            {
                Tag = window,
                Content = "×",
                ToolTip = "Close window",
                Width = 28,
                Height = 25,
                Margin = new Thickness(4, 2, 4, 2),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            closeButton.Click += ClosePreviewWindow_Click;
            var footer = new DockPanel { Height = 31, LastChildFill = true };
            DockPanel.SetDock(closeButton, Dock.Right);
            footer.Children.Add(closeButton);
            footer.Children.Add(title);
            var card = new StackPanel { Width = 236, Margin = new Thickness(4, 2, 4, 2), Tag = window, AllowDrop = true };
            card.Children.Add(previewButton);
            card.Children.Add(footer);
            previewButton.PreviewMouseLeftButtonDown += PreviewCard_MouseLeftButtonDown;
            previewButton.PreviewMouseMove += PreviewCard_MouseMove;
            card.DragOver += PreviewCard_DragOver;
            card.DragLeave += PreviewCard_DragLeave;
            card.Drop += (_, e) =>
            {
                if (card.Tag is not RunningWindow target || !e.Data.GetDataPresent(PreviewWindowDragFormat) ||
                    e.Data.GetData(PreviewWindowDragFormat) is not RunningWindow moving || moving.Handle == target.Handle) return;
                moveWindow(moving, target);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            };
            CardsPanel.Children.Add(card);
            _items.Add((window, surface, card));
        }

        Loaded += (_, _) => PlaceNearTarget(placementTarget, edge);
    }

    public bool Matches(IReadOnlyList<RunningWindow> windows) =>
        _items.Select(item => item.Window.Handle).SequenceEqual(windows.Select(window => window.Handle));

    public void ApplyWindowOrder(IReadOnlyList<RunningWindow> windows)
    {
        var positions = windows.Select((window, index) => (window.Handle, index))
            .ToDictionary(item => item.Handle, item => item.index);
        var orderedItems = _items.OrderBy(item => positions.GetValueOrDefault(item.Window.Handle, int.MaxValue)).ToList();
        _items.Clear();
        _items.AddRange(orderedItems);
        CardsPanel.Children.Clear();
        foreach (var item in _items) CardsPanel.Children.Add(item.Card);
        CardsPanel.Width = Math.Max(Width - 20, _items.Count * 236d);
        QueueThumbnailLayoutUpdate();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SystemBackdropService.TryApplySmallRoundedCorners(handle);
        if (!SystemBackdropService.TryApplyTransientBackdrop(handle)) return;
        Background = Brushes.Transparent;
        PreviewSurface.Background = Brushes.Transparent;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        foreach (var (window, _, _) in _items)
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

    private void ClosePreviewWindow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RunningWindow window }) RunningWindowService.Close(window);
        Close();
        e.Handled = true;
    }

    private void PreviewCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button) _dragStart = e.GetPosition(button);
    }

    private void PreviewCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button { Tag: RunningWindow window } button) return;
        var current = e.GetPosition(button);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = new DataObject(PreviewWindowDragFormat, window);
        DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
    }

    private void PreviewCard_DragOver(object sender, DragEventArgs e)
    {
        var canReorder = sender is StackPanel { Tag: RunningWindow target } &&
            e.Data.GetDataPresent(PreviewWindowDragFormat) && e.Data.GetData(PreviewWindowDragFormat) is RunningWindow moving &&
            moving.Handle != target.Handle;
        if (sender is StackPanel card) card.Opacity = canReorder ? 0.72 : 1;
        e.Effects = canReorder ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PreviewCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is StackPanel card) card.Opacity = 1;
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
        foreach (var (window, surface, _) in _items)
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
