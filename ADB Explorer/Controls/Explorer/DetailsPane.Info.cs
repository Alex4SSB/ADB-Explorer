namespace ADB_Explorer.Controls;

public partial class DetailsPane
{
    private DriveViewModel? _mountOptionsDrive;

    private VirtualDriveViewModel? _trashCountDrive;

    public ObservableCollection<IDetailsViewModel> SelectionInfoItems { get; } = [];

    public ObservableCollection<MountOptionViewModel> MountOptionsItems { get; } = [];

    private void UnsubscribeTrashCountDrive()
    {
        if (_trashCountDrive is null)
            return;

        _trashCountDrive.PropertyChanged -= OnTrashCountChanged;
        _trashCountDrive = null;
    }

    private void SubscribeTrashCountDrive(VirtualDriveViewModel? trash)
    {
        UnsubscribeTrashCountDrive();
        _trashCountDrive = trash;
        if (trash is not null)
            trash.PropertyChanged += OnTrashCountChanged;
    }

    private void OnTrashCountChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(VirtualDriveViewModel.ItemsCount))
            return;

        App.SafeBeginInvoke(() => LargeFileIcon.Source = TrashIcon(_trashCountDrive));
    }

    private static BitmapSource TrashIcon(VirtualDriveViewModel? trash)
        => FileIconProvider.GetDriveIcon(AbstractDrive.DriveType.Trash, 120, trash?.ItemsCount is null or <= 0);

    private void UnsubscribeMountOptionsDrive()
    {
        _mountOptionsDrive?.PropertyChanged -= OnMountOptionsDrivePropertyChanged;
        _mountOptionsDrive = null;
    }

    private void SubscribeMountOptionsDrive(DriveViewModel drive)
    {
        UnsubscribeMountOptionsDrive();
        _mountOptionsDrive = drive;
        _mountOptionsDrive.PropertyChanged += OnMountOptionsDrivePropertyChanged;
    }

    private void OnMountOptionsDrivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(DriveViewModel.MountOptions))
            return;

        if (sender is not DriveViewModel drive || drive != _mountOptionsDrive || Drive != drive)
            return;

        App.SafeInvoke(() =>
        {
            MountOptionsItems.Clear();
            if (drive.MountOptions is { } newOpts)
            {
                foreach (var opt in newOpts)
                    MountOptionsItems.Add(new MountOptionViewModel(opt));
            }
        });
    }

    private void PopulateMountOptions(DriveViewModel drive)
    {
        MountOptionsItems.Clear();
        if (drive.MountOptions is { } opts)
        {
            foreach (var opt in opts)
                MountOptionsItems.Add(new MountOptionViewModel(opt));
        }
    }

    private static void ReplaceItems(ObservableCollection<IDetailsViewModel> items, IEnumerable<IDetailsViewModel> rows)
    {
        items.Clear();
        foreach (var row in rows)
            items.Add(row);
    }

    private void PopulateThumbnailInfoItems(DriveViewModel drive)
    {
        ReplaceItems(SelectionInfoItems, DetailsRows.ForDrive(drive));
        UnsubscribeMountOptionsDrive();
        MountOptionsItems.Clear();

        if (drive is not (LogicalDriveViewModel or VirtualDriveViewModel { Type: AbstractDrive.DriveType.Temp }))
            return;

        PopulateMountOptions(drive);
        SubscribeMountOptionsDrive(drive);

        if (drive.FSInfo is null)
        {
            var device = Data.ActiveDevice;
            var cts = new CancellationTokenSource();
            _cancellationToken = cts;
            _ = Task.Run(() => AdbHelper.ApplyMountInfo(device, cts.Token), cts.Token);
        }
    }

    private void PopulateThumbnailInfoItems(Package package)
    {
        ReplaceItems(SelectionInfoItems, DetailsRows.ForPackage(package));

        _cancellationToken?.Cancel();
        var cts = new CancellationTokenSource();
        _cancellationToken = cts;

        if (package.VersionName is null || package.LastUpdateTime is null)
        {
            var device = Data.ActiveDevice;
            _ = Task.Run(() => AdbHelper.FetchDumpsysInfoAsync(device, package, cts.Token), cts.Token);
        }
    }

    private void PopulateThumbnailInfoItems(FileClass file)
    {
        ReplaceItems(SelectionInfoItems, DetailsRows.ForFile(file));
        ReplaceItems(PermissionsItems, DetailsRows.ForPermissions(file));

        var deviceId = Data.ActiveDevice?.ID;
        var probeExtraInfo = !Data.FileActions.IsRecycleBin
            && !ArchivePath.IsArchivePath(file.FullPath, deviceId)
            && !file.IsCreationTimeResolved;

        if (probeExtraInfo && _extraInfoPath != file.FullPath)
        {
            _extraInfoCts?.Cancel();
            _extraInfoCts = new CancellationTokenSource();
            var probePath = file.FullPath;
            _extraInfoPath = probePath;
            var cts = _extraInfoCts;
            _ = file.UpdateExtraInfoAsync(cts.Token).ContinueWith(_ =>
            {
                if (_extraInfoPath == probePath)
                    _extraInfoPath = null;
            }, TaskScheduler.Default);
        }

        SubscribePermissionDevice();
        UpdateCanEditPermissions();
    }
}
