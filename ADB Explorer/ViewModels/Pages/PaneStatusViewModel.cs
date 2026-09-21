using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels.Pages;

/// <summary>
/// The status bar's readouts of one explorer pane: its item count, selection and listing progress.
/// A split view shows one of these for each pane, so they read that pane's instance, not the focused one.
/// </summary>
public partial class PaneStatusViewModel : ObservableObject
{
    private ExplorerInstance? _instance;
    private INotifyPropertyChanged? _items;
    private bool _attached;
    private bool _refreshQueued;

    private static ExplorerViewModel? Explorer => App.Services.GetService<ExplorerViewModel>();

    [ObservableProperty]
    public partial Visibility ContainerVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string ItemsCount { get; set; } = "";

    [ObservableProperty]
    public partial string ItemsSuffix { get; set; } = "";

    [ObservableProperty]
    public partial Visibility ItemsVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility UnfinishedVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string SelectedCount { get; set; } = "0";

    [ObservableProperty]
    public partial string SelectedSuffix { get; set; } = "";

    [ObservableProperty]
    public partial Visibility SelectedVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string SelectedTotalSize { get; set; } = "";

    [ObservableProperty]
    public partial Visibility SelectedTotalSizeVisibility { get; set; } = Visibility.Collapsed;

    public ExplorerInstance? Instance
    {
        get => _instance;
        set
        {
            if (ReferenceEquals(_instance, value))
                return;

            if (_attached)
                Unsubscribe();

            _instance = value;

            if (_attached)
                Subscribe();
        }
    }

    /// <summary>Starts following the pane; done while the readout is on screen, so a closed pane's isn't kept alive.</summary>
    public void Attach()
    {
        if (_attached)
            return;

        _attached = true;
        Subscribe();
    }

    public void Detach()
    {
        if (!_attached)
            return;

        Unsubscribe();
        _attached = false;
    }

    private void Subscribe()
    {
        if (_instance is { } instance)
        {
            instance.PropertyChanged += Instance_PropertyChanged;
            instance.FileList.Actions.PropertyChanged += Actions_PropertyChanged;
            WatchItems(instance);
        }

        if (Explorer is { } explorer)
            explorer.PropertyChanged += Explorer_PropertyChanged;

        Refresh();
    }

    private void Unsubscribe()
    {
        if (_instance is { } instance)
        {
            instance.PropertyChanged -= Instance_PropertyChanged;
            instance.FileList.Actions.PropertyChanged -= Actions_PropertyChanged;
        }

        if (_items is not null)
            _items.PropertyChanged -= Items_PropertyChanged;

        _items = null;

        if (Explorer is { } explorer)
            explorer.PropertyChanged -= Explorer_PropertyChanged;
    }

    /// <summary>The count belongs to the pane's current items view, which is replaced as it navigates.</summary>
    private void WatchItems(ExplorerInstance instance)
    {
        if (_items is not null)
            _items.PropertyChanged -= Items_PropertyChanged;

        _items = instance.ExplorerItemsSource as INotifyPropertyChanged;

        if (_items is not null)
            _items.PropertyChanged += Items_PropertyChanged;
    }

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerInstance.ExplorerItemsSource) && _instance is { } instance)
            App.SafeBeginInvoke(() => WatchItems(instance));

        if (e.PropertyName is nameof(ExplorerInstance.ExplorerItemsSource) or nameof(ExplorerInstance.IsListingUnfinished))
            QueueRefresh();
    }

    private void Items_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Count")
            QueueRefresh();
    }

    private void Actions_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileActionsEnable.SelectedItemsCount)
            or nameof(FileActionsEnable.IsExplorerVisible)
            or nameof(FileActionsEnable.IsDriveViewVisible)
            or nameof(FileActionsEnable.IsAppDrive))
        {
            QueueRefresh();
        }
    }

    private void Explorer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.SelectedFilesTotalSize))
            QueueRefresh();
    }

    /// <summary>Changes arrive in bursts, and off the UI thread, so the readouts are derived once, there, afterwards.</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued)
            return;

        _refreshQueued = true;
        App.SafeBeginInvoke(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, DispatcherPriority.Background);
    }

    private static int CountOf(ICollectionView? view) => view switch
    {
        CollectionView collection => collection.Count,
        null => 0,
        _ => view.Cast<object>().Count(),
    };

    private void Refresh()
    {
        if (_instance is not { } instance)
        {
            ContainerVisibility = Visibility.Collapsed;
            return;
        }

        var actions = instance.FileList.Actions;
        var isExplorer = actions.IsExplorerVisible;

        ContainerVisibility = isExplorer || actions.IsDriveViewVisible ? Visibility.Visible : Visibility.Collapsed;

        ItemsCount = isExplorer ? $"{CountOf(instance.ExplorerItemsSource)}" : "";
        ItemsVisibility = ItemsCount is "" or "0" ? Visibility.Collapsed : Visibility.Visible;
        ItemsSuffix = ItemsCount is "1" ? Strings.Resources.S_BROWSER_ITEMS : Strings.Resources.S_BROWSER_ITEMS_PLURAL;
        UnfinishedVisibility = instance.IsListingUnfinished ? Visibility.Visible : Visibility.Collapsed;

        var selectedFiles = instance.FileList.SelectedFiles ?? [];
        var selected = actions.IsAppDrive
            ? (instance.FileList.SelectedPackages ?? []).Count()
            : selectedFiles.Count();

        SelectedCount = $"{selected}";
        SelectedVisibility = selected == 0 ? Visibility.Collapsed : Visibility.Visible;
        SelectedSuffix = selected == 1 ? Strings.Resources.S_ITEMS_SELECTED : Strings.Resources.S_ITEMS_SELECTED_PLURAL;

        var totalSize = FileHelper.TotalSize(selectedFiles);
        SelectedTotalSize = totalSize > 0 ? totalSize.BytesToSize(true) : "";
        SelectedTotalSizeVisibility = totalSize > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
