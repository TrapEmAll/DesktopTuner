using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

namespace DesktopTuner;

public partial class RunDialogWindow : Window
{
    private readonly Action<string, bool> _runCommand;

    public RunDialogWindow(Action<string, bool> runCommand)
    {
        InitializeComponent();
        _runCommand = runCommand;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CommandBox.Focus();
        Keyboard.Focus(CommandBox);
        CommandBox.SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _runCommand(CommandBox.Text, RunAsAdministratorCheckBox.IsChecked == true);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not run command", MessageBoxButton.OK, MessageBoxImage.Error);
            CommandBox.Focus();
            CommandBox.SelectAll();
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "Programs and files|*.exe;*.com;*.bat;*.cmd;*.msc;*.cpl;*.*|All files|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            CommandBox.Text = $"\"{dialog.FileName}\"";
            CommandBox.Focus();
            CommandBox.CaretIndex = CommandBox.Text.Length;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
