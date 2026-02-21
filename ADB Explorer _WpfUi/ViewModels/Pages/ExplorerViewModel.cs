using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels.Pages;

public partial class ExplorerViewModel : ObservableObject
{

    [ObservableProperty]
    private ICollectionView _explorerItemsSource;

    [ObservableProperty]
    private ICollectionView _driveItemsSource;

    [ObservableProperty]
    private ListSortDirection? _nameColumnSortDirection;

    [ObservableProperty]
    private ListSortDirection? _packageTypeColumnSortDirection;

    public string SelectedFilesTotalSize => (Data.SelectedFiles is not null && FileHelper.TotalSize(Data.SelectedFiles) is long size and > 0) ? size.BytesToSize() : "";
    public string SelectedFilesCount => $"{(Data.FileActions.IsAppDrive ? Data.SelectedPackages.Count() : Data.SelectedFiles.Count())}";

    public Visibility FolderColumnVisibility
        => Data.FileActions.IsAppDrive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RecycleBinColumnVisibility
        => Data.FileActions.IsRecycleBin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PackageColumnVisibility
        => Data.FileActions.IsAppDrive ? Visibility.Visible : Visibility.Collapsed;

    public ExplorerViewModel()
    {
        Data.FileActions.PropertyChanged += FileActions_PropertyChanged;
        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        Data.Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.EnableApk):
            case nameof(AppSettings.EnableRecycle):
                UpdateDriveView();
                break;

            default:
                break;
        }
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppRuntimeSettings.ExplorerSource):
                UpdateExplorerView();
                break;

            case nameof(AppRuntimeSettings.FilterDrives):
                UpdateDriveView();
                break;

            default:
                break;
        }
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

            case nameof(FileActionsEnable.IsDriveViewVisible):
                UpdateDriveView();
                break;

            default:
                break;
        }
    }

    private void UpdateExplorerView()
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

    private void UpdateDriveView()
    {
        var source = Data.DevicesObject.Current.Drives;
        if (source is null)
            return;

        var view = CollectionViewSource.GetDefaultView(source);
        if (view is null)
            return;

        if (view.Filter is not null)
        {
            view.Refresh();
            return;
        }

        Predicate<object> predicate = d =>
        {
            var drive = (DriveViewModel)d;

            return drive.Type switch
            {
                AbstractDrive.DriveType.Trash => Data.Settings.EnableRecycle,
                AbstractDrive.DriveType.Temp or AbstractDrive.DriveType.Package => Data.Settings.EnableApk,
                _ => true,
            };
        };

        view.Filter = predicate;

        if (view.SortDescriptions.All(d => d.PropertyName != nameof(DriveViewModel.Type)))
            view.SortDescriptions.Add(new(nameof(DriveViewModel.Type), ListSortDirection.Ascending));

        DriveItemsSource = view;
    }

}
