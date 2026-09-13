using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopTuner;

public sealed class ExplorerNavigationNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isLoaded;
    private bool _isLoading;
    private bool _isSelected;

    public ExplorerNavigationNode(string label, string? path = null, bool isThisPc = false, bool isPlaceholder = false)
    {
        Label = label;
        Path = path;
        IsThisPc = isThisPc;
        IsPlaceholder = isPlaceholder;
        if (!isPlaceholder && (path is not null || isThisPc)) Children.Add(new("Loading...", isPlaceholder: true));
    }

    public string Label { get; }
    public string? Path { get; }
    public bool IsThisPc { get; }
    public bool IsPlaceholder { get; }
    public ObservableCollection<ExplorerNavigationNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public Task? ChildrenLoadTask { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
