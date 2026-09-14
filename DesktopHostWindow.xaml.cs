using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopTuner;

public partial class DesktopHostWindow : Window
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private readonly string _userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private readonly string _publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public DesktopHostWindow()
    {
        InitializeComponent();
        Resources["DesktopHostTextShadow"] = new DropShadowEffect { Color = System.Windows.Media.Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 };
        RefreshDesktop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DesktopWallpaperService.LoadPrimaryWallpaper() is { } wallpaper)
            Background = new ImageBrush(wallpaper) { Stretch = Stretch.UniformToFill };
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    private void RefreshDesktop() => DesktopItems.ItemsSource = DesktopHostCatalog.ReadItems([_userDesktop, _publicDesktop]);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5) return;
        RefreshDesktop();
        e.Handled = true;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: ExplorerEntry entry }) return;
        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Could not open desktop item", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshDesktop();

    private void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ExplorerFileOperationService.CreateFolder(_userDesktop);
            RefreshDesktop();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Could not create folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenDesktopFolderClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_userDesktop) { UseShellExecute = true });

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
