using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopTuner;

public partial class RunDialogWindow : Window
{
    private readonly Action<string, bool> _runCommand;
    private readonly RunCommandHistoryStore _historyStore;

    public RunDialogWindow(Action<string, bool> runCommand, RunCommandHistoryStore? historyStore = null)
    {
        InitializeComponent();
        _runCommand = runCommand;
        _historyStore = historyStore ?? new RunCommandHistoryStore();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SystemBackdropService.TryApplySmallRoundedCorners(handle);
        SystemBackdropService.TryApplyMica(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CommandBox.ItemsSource = _historyStore.Load();
        CommandBox.ApplyTemplate();
        if (CommandBox.Template.FindName("PART_EditableTextBox", CommandBox) is not TextBox editBox)
        {
            CommandBox.Focus();
            return;
        }
        editBox.Focus();
        Keyboard.Focus(editBox);
        editBox.SelectAll();
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
            if (!_historyStore.TryRecord(CommandBox.Text))
                Trace.TraceWarning("The command ran successfully, but Desktop Tuner could not save it to Run history.");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not run command", MessageBoxButton.OK, MessageBoxImage.Error);
            if (CommandBox.Template.FindName("PART_EditableTextBox", CommandBox) is TextBox editBox)
            {
                editBox.Focus();
                editBox.SelectAll();
            }
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
            CommandBox.ApplyTemplate();
            if (CommandBox.Template.FindName("PART_EditableTextBox", CommandBox) is TextBox editBox)
            {
                editBox.Focus();
                editBox.CaretIndex = editBox.Text.Length;
            }
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!_historyStore.TryClear())
        {
            MessageBox.Show(this, "Desktop Tuner could not clear the saved Run history.", "Could not clear history", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var currentText = CommandBox.Text;
        CommandBox.ItemsSource = Array.Empty<string>();
        CommandBox.Text = currentText;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
