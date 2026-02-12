using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels.Pages;

public partial class ExplorerViewModel : ObservableObject
{
    public ExplorerViewModel()
    {
        Data.FileActions.PropertyChanged += FileActions_PropertyChanged;
        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppRuntimeSettings.ExplorerSource))
            UpdateExplorerView();
    }

    private void FileActions_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileActionsEnable.SelectedItemsCount):
                OnPropertyChanged(nameof(SelectedFilesTotalSize));
                OnPropertyChanged(nameof(SelectedFilesCount));
                break;

            case nameof(FileActionsEnable.IsAppDrive):
            case nameof(FileActionsEnable.IsRecycleBin):
                OnPropertyChanged(nameof(FolderColumnVisibility));
                OnPropertyChanged(nameof(RecycleBinColumnVisibility));
                OnPropertyChanged(nameof(PackageColumnVisibility));
                break;

            default:
                break;
        }
    }

    [ObservableProperty]
    private ICollectionView _explorerItemsSource;

    [ObservableProperty]
    private ListSortDirection? _nameColumnSortDirection;

    [ObservableProperty]
    private ListSortDirection? _packageTypeColumnSortDirection;

    public void UpdateExplorerView()
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (!Data.FileActions.IsExplorerVisible)
                return;

            var source = Data.RuntimeSettings.ExplorerSource;
            if (source is null)
                return;

            var view = CollectionViewSource.GetDefaultView(source);
            if (view is null)
                return;

            if (Data.FileActions.IsAppDrive)
            {
                view.Filter = Data.Settings.ShowSystemPackages
                    ? FileHelper.PkgFilter()
                    : pkg => ((Package)pkg).Type is Package.PackageType.User;

                if (view.SortDescriptions.All(d => d.PropertyName != nameof(Package.Type)))
                {
                    view.SortDescriptions.Add(new(nameof(Package.Type), ListSortDirection.Descending));
                }

                PackageTypeColumnSortDirection ??= ListSortDirection.Descending;
            }
            else
            {
                view.Filter = !Data.Settings.ShowHiddenItems
                    ? FileHelper.HideFiles()
                    : file => !FileHelper.IsHiddenRecycleItem((FileClass)file);

                if (!view.SortDescriptions.Any(d => d.PropertyName
                        is nameof(FileClass.IsTemp)
                        or nameof(FileClass.IsDirectory)
                        or nameof(FileClass.SortName)))
                {
                    view.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
                    view.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListSortDirection.Descending));
                    view.SortDescriptions.Add(new(nameof(FileClass.SortName), ListSortDirection.Ascending));
                }

                NameColumnSortDirection ??= ListSortDirection.Ascending;
            }

            ExplorerItemsSource = view;
        });
    }

    public string SelectedFilesTotalSize => (Data.SelectedFiles is not null && FileHelper.TotalSize(Data.SelectedFiles) is long size and > 0) ? size.BytesToSize() : "";
    public string SelectedFilesCount => $"{(Data.FileActions.IsAppDrive ? Data.SelectedPackages.Count() : Data.SelectedFiles.Count())}";

    public Visibility FolderColumnVisibility
        => Data.FileActions.IsAppDrive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RecycleBinColumnVisibility
        => Data.FileActions.IsRecycleBin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PackageColumnVisibility
        => Data.FileActions.IsAppDrive ? Visibility.Visible : Visibility.Collapsed;

}
