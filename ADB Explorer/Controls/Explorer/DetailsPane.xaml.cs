using System.Windows.Media.Animation;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsPane.xaml
/// </summary>
public partial class DetailsPane : UserControl
{
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false, OnIsOpenChanged));

    public double PaneMinWidth
    {
        get => (double)GetValue(PaneMinWidthProperty);
        set => SetValue(PaneMinWidthProperty, value);
    }

    public static readonly DependencyProperty PaneMinWidthProperty =
        DependencyProperty.Register(nameof(PaneMinWidth), typeof(double),
          typeof(DetailsPane), new PropertyMetadata(100.0));

    public double PaneMaxWidth
    {
        get => (double)GetValue(PaneMaxWidthProperty);
        set => SetValue(PaneMaxWidthProperty, value);
    }

    public static readonly DependencyProperty PaneMaxWidthProperty =
        DependencyProperty.Register(nameof(PaneMaxWidth), typeof(double),
          typeof(DetailsPane), new PropertyMetadata(1000.0));

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DetailsPane pane) return;
        bool isOpen = (bool)e.NewValue;

        var animation = new DoubleAnimation
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };

        if (isOpen)
        {
            pane.Visibility = Visibility.Visible;
            pane.SlideTransform.X = pane.ActualWidth > 0 ? pane.ActualWidth : Data.Settings.DetailsPaneWidth;
            animation.To = 0;
            pane.SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);

            // Selection is not pushed into this pane while it is closed. Pick up the
            // current explorer/drive selection so opening does not keep none-selected UI.
            pane.ApplyCurrentExplorerSelection();
        }
        else
        {
            animation.To = pane.ActualWidth > 0 ? pane.ActualWidth : Data.Settings.DetailsPaneWidth;
            animation.Completed += (_, _) =>
            {
                pane.Visibility = Visibility.Collapsed;
                pane.SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
                pane.SlideTransform.X = 0;
            };
            pane.SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }
    }

    public IEnumerable<IBrowserItem> SelectedFiles
    {
        get => (IEnumerable<IBrowserItem>)GetValue(SelectedFilesProperty);
        set => SetValue(SelectedFilesProperty, value);
    }

    public static readonly DependencyProperty SelectedFilesProperty =
        DependencyProperty.Register(nameof(SelectedFiles), typeof(IEnumerable<IBrowserItem>),
          typeof(DetailsPane), new PropertyMetadata(Array.Empty<IBrowserItem>(), OnSelectedFilesChanged));

    public FileClass? File
    {
        get => (FileClass?)GetValue(FileProperty);
        private set => SetValue(FileProperty, value);
    }

    public static readonly DependencyProperty FileProperty =
        DependencyProperty.Register(nameof(File), typeof(FileClass),
          typeof(DetailsPane), new PropertyMetadata(null));

    public Package? Package
    {
        get => (Package?)GetValue(PackageProperty);
        private set => SetValue(PackageProperty, value);
    }

    public static readonly DependencyProperty PackageProperty =
        DependencyProperty.Register(nameof(Package), typeof(Package),
          typeof(DetailsPane), new PropertyMetadata(null));

    public DriveViewModel? Drive
    {
        get => (DriveViewModel?)GetValue(DriveProperty);
        private set => SetValue(DriveProperty, value);
    }

    public static readonly DependencyProperty DriveProperty =
        DependencyProperty.Register(nameof(Drive), typeof(DriveViewModel),
          typeof(DetailsPane), new PropertyMetadata(null));

    public SidePaneMode Mode
    {
        get => (SidePaneMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(SidePaneMode),
          typeof(DetailsPane), new PropertyMetadata(SidePaneMode.Details));

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand PdfUnlockCommand { get; }

    public Action RequestModeRefresh
    {
        get => (Action)GetValue(RequestModeRefreshProperty);
        set => SetValue(RequestModeRefreshProperty, value);
    }

    public static readonly DependencyProperty RequestModeRefreshProperty =
        DependencyProperty.Register(nameof(RequestModeRefresh), typeof(Action),
          typeof(DetailsPane), new PropertyMetadata(null));

    private static readonly BitmapSource AppIcon = DefaultAndroidPackageIcon.Bitmap;

    private CancellationTokenSource? _cancellationToken;
    private CancellationTokenSource? _extraInfoCts;
    private string? _extraInfoPath;

    private static void OnSelectedFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => App.SafeBeginInvoke(
        () => ((DetailsPane)d).ApplySelection(e.OldValue as IEnumerable<IBrowserItem>, (IEnumerable<IBrowserItem>)e.NewValue),
        DispatcherPriority.Render);

    private void ApplySelection(IEnumerable<IBrowserItem>? oldFiles, IEnumerable<IBrowserItem> files)
    {
        UnsubscribeTrashCountDrive();

        var oldFile = SingleFile(oldFiles);
        var newFile = SingleFile(files);

        // Selection refresh rebuilds the SelectedFiles enumerable even when the file is unchanged.
        // Cancelling an in-flight custom pull then loses ThumbnailUpdated and can miss the UI update.
        if (oldFile is not null)
        {
            oldFile.PropertyChanged -= OnFileFullPathChanged;
            if (!ReferenceEquals(oldFile, newFile))
                oldFile.CancelCacheThumbnailLoading();
        }

        ResetSelectionState();
        if (files.FirstOrDefault() is FileClass fc && string.IsNullOrEmpty(fc.FullName))
            return;

        if (Mode is SidePaneMode.Preview)
            ShowPreview(files);
        else
            ShowDetails(files);
    }

    private static FileClass? SingleFile(IEnumerable<IBrowserItem>? files)
        => files is not null && files.Count() == 1 && files.First() is FileClass file ? file : null;

    private void ResetSelectionState()
    {
        UnsubscribePreviewMounts();
        UnsubscribePermissionDevice();
        ClearPhotoPreview();
        IsEditingPermissions = false;
        CanEditPermissions = false;

        EditorText = null;
        _previewBytes = null;
        IsEditorReadOnly = false;
        ResetSyntaxToAutomatic();
        _previewFileExtension = null;
        UpdateSyntaxSelectorVisibility();
        ApplyPreviewSyntaxHighlighting();
        PdfScrollViewer.Visibility = Visibility.Collapsed;
        PdfPagesControl.ItemsSource = null;
        IsPdfPasswordPromptVisible = false;
        IsPdfPasswordWrong = false;
        _pdfMemoryStream = null;
        _cancellationToken?.Cancel();
        _cancellationToken = null;
        _extraInfoCts?.Cancel();
        _extraInfoCts = null;
        _extraInfoPath = null;
    }

    private static FlowDirection UiFlowDirection => Data.RuntimeSettings.IsRTL
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    private void SetHeader(string text, FlowDirection flowDirection, ImageSource? icon, double iconMaxHeight = 128, ImageSource? smallIcon = null)
    {
        FileNameTextBlock.Text = text;
        FileNameTextBlock.FlowDirection = flowDirection;
        LargeFileIcon.Source = icon;
        LargeFileIcon.MaxHeight = iconMaxHeight;
        SmallFileIcon.Source = smallIcon;
    }

    private void DetachPackage()
    {
        if (Package is not null)
            Package.PropertyChanged -= OnPackagePropertyChanged;
    }

    private void ShowDetails(IEnumerable<IBrowserItem> files)
    {
        SelectionInfoItems.Clear();
        PermissionsItems.Clear();
        MountOptionsItems.Clear();
        UnsubscribeMountOptionsDrive();
        InvalidSelectionBorder.Visibility = Visibility.Visible;

        var count = files.Count();
        if (count == 1)
        {
            switch (files.First())
            {
                case FileClass file:
                    ShowFileDetails(file);
                    break;

                case Package package:
                    ShowPackageDetails(package);
                    break;

                case DriveViewModel drive:
                    ShowDriveDetails(drive);
                    break;
            }
        }
        else if (count > 1)
        {
            DetachPackage();
            File = null;
            Package = null;
            SetHeader($"{count} {Strings.Resources.S_ITEMS_SELECTED_PLURAL}", UiFlowDirection, FileIconProvider.GetMultipleFilesIcon(120));
        }
        else
            ShowLocationDetails();
    }

    private void ShowFileDetails(FileClass file)
    {
        File = file;
        file.PropertyChanged -= OnFileFullPathChanged;
        file.PropertyChanged += OnFileFullPathChanged;

        var hasThumbnail = file.CacheThumbnail?.Image is not null;
        SetHeader(file.DisplayName,
                  file.NameIsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                  file.DragImage,
                  hasThumbnail ? 192 : 128,
                  hasThumbnail ? file.FileIcon32 : null);

        InvalidSelectionBorder.Visibility = Visibility.Collapsed;
        file.BeginLoadCacheThumbnail();
        if (file.IsApk)
            ApkIconService.BeginLoadForFile(file, ApkIconService.ApkLoadPriority.Selected);

        // BeginLoad is async; if the pane cache was already warm, refresh now.
        if (file.CacheThumbnail?.Image is not null)
            UpdateFileThumbnailDisplay(file);

        PopulateThumbnailInfoItems(file);
    }

    private void ShowPackageDetails(Package package)
    {
        DetachPackage();
        Package = package;
        package.PropertyChanged -= OnPackagePropertyChanged;
        package.PropertyChanged += OnPackagePropertyChanged;

        SetHeader(package.DisplayName, FlowDirection.LeftToRight, package.IconViewModel.LargeIcon);
        InvalidSelectionBorder.Visibility = Visibility.Collapsed;
        PopulateThumbnailInfoItems(package);
        ApkIconService.BeginLoadForPackage(package, ApkIconService.ApkLoadPriority.Selected);
    }

    private void ShowDriveDetails(DriveViewModel drive)
    {
        DetachPackage();
        File = null;
        Package = null;
        Drive = drive;

        VirtualDriveViewModel? trashDrive = null;
        if (drive is VirtualDriveViewModel { Type: AbstractDrive.DriveType.Trash } virtualDrive)
            trashDrive = virtualDrive;
        else if (drive.Type is AbstractDrive.DriveType.Trash)
            trashDrive = TrashHelper.GetTrashDrive(Data.ActiveExplorerInstance.EffectiveDevice);

        SubscribeTrashCountDrive(drive.Type is AbstractDrive.DriveType.Trash ? trashDrive : null);
        SetHeader(drive.DisplayName, UiFlowDirection, FileIconProvider.GetDriveIcon(drive.Type, 120, trashDrive?.ItemsCount is null or <= 0));
        InvalidSelectionBorder.Visibility = Visibility.Collapsed;
        PopulateThumbnailInfoItems(drive);
    }

    /// <summary>
    /// Nothing is selected: describe the location being browsed.
    /// </summary>
    private void ShowLocationDetails()
    {
        if (File is FileClass previousFile)
            previousFile.PropertyChanged -= OnFileFullPathChanged;

        DetachPackage();
        File = null;
        Package = null;
        Drive = null;

        if (Data.CurrentPath is null)
            SetHeader("", UiFlowDirection, null);
        else if (Data.FileActions.IsRecycleBin)
        {
            var trashDrive = TrashHelper.GetTrashDrive(Data.ActiveExplorerInstance.EffectiveDevice);
            SubscribeTrashCountDrive(trashDrive);
            SetHeader(Strings.Resources.S_DRIVE_TRASH, UiFlowDirection, TrashIcon(trashDrive));
        }
        else if (Data.FileActions.IsAppDrive)
            SetHeader(Strings.Resources.S_DRIVE_APPS, UiFlowDirection, AppIcon);
        else if (Data.FileActions.IsDriveViewVisible)
            SetHeader(Data.ActiveExplorerInstance.EffectiveDevice?.Name ?? "", FlowDirection.LeftToRight, FileIconProvider.GetPhoneIcon(120));
        else if (Data.FileActions.IsSearchMode)
            SetHeader(Data.FileActions.ExplorerFilter, UiFlowDirection, FileIconProvider.GetSearchIcon(120));
        else if (Data.CurrentDrive?.Path == Data.CurrentPath)
            SetHeader(Data.CurrentDrive.DisplayName, UiFlowDirection, FileIconProvider.GetDriveIcon(Data.CurrentDrive.Type, 120));
        else if (Data.DirList?.CurrentLocation is { } location)
        {
            File = location;
            location.PropertyChanged -= OnFileFullPathChanged;
            location.PropertyChanged += OnFileFullPathChanged;
            SetHeader(location.DisplayName, UiFlowDirection, location.DragImage);
            InvalidSelectionBorder.Visibility = Visibility.Collapsed;

            // Skip redundant loads when selection refresh is replayed for the same folder.
            if (location.CacheThumbnail?.Image is null)
                location.BeginLoadCacheThumbnail();
            else
                UpdateFileThumbnailDisplay(location);

            PopulateThumbnailInfoItems(location);

            // The thumbnail refresh above may enlarge the icon; the location header keeps it standard.
            LargeFileIcon.MaxHeight = 128;
            SmallFileIcon.Source = null;
        }
        else
            SetHeader(FileHelper.GetFullName(Data.CurrentPath), UiFlowDirection, new FileClass("", Data.CurrentPath, AbstractFile.FileType.Folder).DragImage);
    }

    public void RefreshSelection() =>
        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, SelectedFiles, SelectedFiles));

    /// <summary>
    /// Copies the live explorer selection into <see cref="SelectedFiles"/>.
    /// Used when the pane opens; while closed, <see cref="ExplorerPageContent"/> skips that update.
    /// </summary>
    private void ApplyCurrentExplorerSelection()
    {
        if (Data.FileActions.IsDriveViewVisible)
        {
            var drive = Data.RuntimeSettings.SelectedDrive;
            SelectedFiles = drive is null ? [] : [drive];
            return;
        }

        if (Data.FileActions.IsAppDrive)
        {
            SelectedFiles = Data.Packages?.Where(static p => p.IsSelected).ToList()
                ?? Data.SelectedPackages?.ToList()
                ?? [];
            return;
        }

        SelectedFiles = Data.DirList?.FileList?.Where(static f => f.IsSelected).ToList()
            ?? Data.SelectedFiles?.ToList()
            ?? [];
    }

    private void OnPackagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => App.SafeBeginInvoke(() =>
    {
        if (sender is not Package package || !ReferenceEquals(Package, package))
            return;

        if (e.PropertyName is nameof(Package.Icon) or nameof(Package.IconLoadCompleted))
            LargeFileIcon.Source = package.IconViewModel.LargeIcon;
        else if (e.PropertyName is nameof(Package.Label) or nameof(Package.DisplayName) or nameof(Package.Name))
        {
            FileNameTextBlock.Text = package.DisplayName;
            PopulateThumbnailInfoItems(package);
        }
    });

    private void OnFileFullPathChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => App.SafeBeginInvoke(() =>
    {
        if (e.PropertyName == nameof(FileClass.DisplayName))
            RefreshSelection();
        else if (e.PropertyName is nameof(FileClass.Size) or nameof(FilePath.ShellLsSize))
        {
            // Size often arrives after the initial pane load request; only retry when still missing.
            if (sender is FileClass file
                && ReferenceEquals(File, file)
                && ThumbnailService.IsCustomThumbnailCandidate(file)
                && file.CacheThumbnail?.Image is null)
            {
                if (Mode is SidePaneMode.Preview)
                    NoPreviewTextBlock.Visibility = Visibility.Collapsed;

                file.BeginLoadCacheThumbnail();
            }
        }
        else if (e.PropertyName is nameof(FileClass.CreationTime)
            or nameof(FileClass.IsCreationTimeResolved)
            or nameof(FileClass.User)
            or nameof(FileClass.Group)
            or nameof(FileClass.OwnerUid)
            or nameof(FileClass.OwnerGid)
            or nameof(FileClass.LastAccessTime)
            or nameof(FileClass.ModifiedTimeWithOffset)
            or nameof(FileClass.LinkTarget))
        {
            if (e.PropertyName is nameof(FileClass.User)
                or nameof(FileClass.Group)
                or nameof(FileClass.OwnerUid)
                or nameof(FileClass.OwnerGid))
                UpdateCanEditPermissions();
            return;
        }
        else if (e.PropertyName is nameof(FileClass.DragImage) or nameof(FileClass.CacheThumbnail) or nameof(FileClass.ApkIcon))
        {
            if (sender is FileClass file && ReferenceEquals(File, file))
            {
                if (Mode is SidePaneMode.Preview)
                    UpdatePhotoPreviewFromThumbnail(file);
                else
                    UpdateFileThumbnailDisplay(file);
            }
        }
        else if (sender is FileClass file && ReferenceEquals(File, file))
        {
            if (e.PropertyName is nameof(FileClass.Permissions))
                UpdateCanEditPermissions();
            PopulateThumbnailInfoItems(file);
        }
    });

    public DetailsPane()
    {
        SaveCommand = new AsyncRelayCommand(async () =>
        {
            if (IsEditorReadOnly)
                return;

            if (SelectedFiles.First() is not FileClass file)
                return;

            var token = _cancellationToken?.Token ?? default;
            bool result;
            int byteCount;
            if (SelectedSyntax?.IsHex is true)
            {
                var bytes = HexText.Parse(EditorText);
                result = await AdbHelper.WriteBytesFileAsync(Data.ActiveDevice, file, bytes, token);
                byteCount = bytes.Length;
                if (result)
                    _previewBytes = bytes;
            }
            else
            {
                result = await AdbHelper.WriteTextFileAsync(Data.ActiveDevice, file, EditorText ?? "", token);
                byteCount = Encoding.UTF8.GetByteCount(EditorText ?? "");
                if (result)
                    _previewBytes = Encoding.UTF8.GetBytes(EditorText ?? "");
            }

            if (result)
            {
                var text = EditorText;
                EditorText = null;
                EditorText = text;

                file.ModifiedTime = DateTime.Now;
                file.ShellLsSize = byteCount;
                file.Size = byteCount;

                App.Services.GetService<ExplorerViewModel>()?.NotifySelectedFilesTotalSize();
            }
        });

        SavePermissionsCommand = new AsyncRelayCommand(SavePermissionsAsync);
        EditPermissionsCommand = new RelayCommand(() => IsEditingPermissions ^= true);

        PdfUnlockCommand = new RelayCommand(() =>
        {
            if (_pdfMemoryStream is null || _cancellationToken is null) return;

            IsPdfPasswordWrong = false;
            var password = PdfPasswordBox.Password;
            var cts = _cancellationToken;
            var memStream = _pdfMemoryStream;

            _ = Task.Run(() => RenderPdfAsync(this, memStream, password, cts));
        });

        InitializeComponent();

        SelectedSyntax = SyntaxOptions[0];

        ContentBox.Width = Data.Settings.DetailsPaneWidth;

        EditorTextBox.IsKeyboardFocusWithinChanged += (s, e) => UpdateIsEditorFocused();

        RequestModeRefresh = () =>
        {
            Mode = Data.FileActions.IsPreviewAllowed
                ? Data.Settings.SidePane
                : SidePaneMode.Details;

            OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
        };

        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = ContentBox.ActualWidth - e.HorizontalChange;
        if (newWidth > PaneMinWidth && newWidth < PaneMaxWidth)
        {
            ContentBox.Width = newWidth;
            Data.Settings.DetailsPaneWidth = (int)ContentBox.Width;
        }
    }
}
