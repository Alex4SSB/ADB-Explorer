using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using System.Windows.Media.Animation;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsPane.xaml
/// </summary>
public partial class DetailsPane : UserControl
{
    private DriveViewModel? _mountOptionsDrive;

    public enum SidePaneMode
    {
        Details,
        Preview,
    }

    public partial class PdfPageItem(BitmapSource image, int index) : ObservableObject
    {
        public BitmapSource Image { get; } = image;

        [ObservableProperty]
        public partial string Label { get; set; } = $"{index}/…";
    }

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

            OnSelectedFilesChanged(d, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, pane.SelectedFiles, pane.SelectedFiles));
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

    public string? EditorText
    {
        get => (string?)GetValue(EditorTextProperty);
        set => SetValue(EditorTextProperty, value);
    }

    public static readonly DependencyProperty EditorTextProperty =
        DependencyProperty.Register(nameof(EditorText), typeof(string),
          typeof(DetailsPane), new PropertyMetadata(null));

    public bool IsEditorFocused
    {
        get => (bool)GetValue(IsEditorFocusedProperty);
        set => SetValue(IsEditorFocusedProperty, value);
    }

    public static readonly DependencyProperty IsEditorFocusedProperty =
        DependencyProperty.Register(nameof(IsEditorFocused), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

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

    public ObservableCollection<IDetailsViewModel> SelectionInfoItems { get; } = [];
    public ObservableCollection<IDetailsViewModel> PermissionsItems { get; } = [];
    public ObservableCollection<MountOptionViewModel> MountOptionsItems { get; } = [];
    

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand PdfUnlockCommand { get; }

    public bool IsPdfPasswordPromptVisible
    {
        get => (bool)GetValue(IsPdfPasswordPromptVisibleProperty);
        set => SetValue(IsPdfPasswordPromptVisibleProperty, value);
    }

    public static readonly DependencyProperty IsPdfPasswordPromptVisibleProperty =
        DependencyProperty.Register(nameof(IsPdfPasswordPromptVisible), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public bool IsPdfPasswordWrong
    {
        get => (bool)GetValue(IsPdfPasswordWrongProperty);
        set => SetValue(IsPdfPasswordWrongProperty, value);
    }

    public static readonly DependencyProperty IsPdfPasswordWrongProperty =
        DependencyProperty.Register(nameof(IsPdfPasswordWrong), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public Action RequestModeRefresh
    {
        get => (Action)GetValue(RequestModeRefreshProperty);
        set => SetValue(RequestModeRefreshProperty, value);
    }

    public static readonly DependencyProperty RequestModeRefreshProperty =
        DependencyProperty.Register(nameof(RequestModeRefresh), typeof(Action),
          typeof(DetailsPane), new PropertyMetadata(null));

    private static readonly FileClass MultipleFiles = new("MultipleFiles", "/MultipleFiles", AbstractFile.FileType.MultipleFiles);
    private static readonly FileClass DriveIcon = new("Drive", "/Drive", AbstractFile.FileType.Drive);
    private static readonly FileClass EmptyTrash = new("RecycleBin", "/RecycleBin", AbstractFile.FileType.EmptyTrash);
    private static readonly FileClass FullTrash = new("RecycleBin", "/RecycleBin", AbstractFile.FileType.FullTrash);
    private static readonly FileClass Phone = new("Phone", "/Phone", AbstractFile.FileType.Phone);
    private static readonly BitmapSource AppIcon = FileToIconConverter.LoadBitmap(new System.Drawing.Icon(Properties.AppGlobal.APK_icon, 128, 128).ToBitmap());

    private CancellationTokenSource? _cancellationToken;
    private MemoryStream? _pdfMemoryStream;

    private static void OnSelectedFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => App.SafeBeginInvoke(() =>
    {
        var control = (DetailsPane)d;
        var files = (IEnumerable<IBrowserItem>)e.NewValue;

        // Unsubscribe from the previous single FileClass selection
        if (e.OldValue is IEnumerable<IBrowserItem> oldFiles
            && oldFiles.Count() == 1
            && oldFiles.First() is FileClass oldFile)
        {
            oldFile.PropertyChanged -= control.OnFileFullPathChanged;
        }

        control.EditorText = null;
        control.PdfScrollViewer.Visibility = Visibility.Collapsed;
        control.PdfPagesControl.ItemsSource = null;
        control.IsPdfPasswordPromptVisible = false;
        control.IsPdfPasswordWrong = false;
        control._pdfMemoryStream = null;
        control._cancellationToken?.Cancel();
        control._cancellationToken = null;
        if (files.FirstOrDefault() is FileClass fc && string.IsNullOrEmpty(fc.FullName))
            return;

        if (control.Mode is SidePaneMode.Preview)
        {
            control.NoPreviewTextBlock.Text = files.Any()
                ? Strings.Resources.S_PREVIEW_INVALID
                : Strings.Resources.S_PREVIEW_EMPTY_SELECTION;

            if (files.Count() == 1
                && !Data.FileActions.IsRecycleBin
                && files.First() is FileClass file
                && file.Type is AbstractFile.FileType.File
                && !AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
                && !file.IsApk
                && !file.IsLink
                && file.Size / 1000 < Data.Settings.MaxPreviewFileSize)
            {
                control.NoPreviewTextBlock.Visibility = Visibility.Collapsed;

                if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var cts = new CancellationTokenSource();
                    control._cancellationToken = cts;
                    var device = Data.DevicesObject.Current;
                    var fullPath = file.FullPath;

                    _ = Task.Run(async () =>
                    {
                        var stream = await AdbHelper.ReadFileAsStreamAsync(device, fullPath, cts.Token);
                        if (stream is null || cts.IsCancellationRequested) return;

                        var memStream = new MemoryStream();
                        await stream.CopyToAsync(memStream, cts.Token);
                        if (cts.IsCancellationRequested) return;

                        App.SafeInvoke(() => control._pdfMemoryStream = memStream);

                        await RenderPdfAsync(control, memStream, password: null, cts);
                    }, cts.Token);
                }
                else
                {
                    var cts = new CancellationTokenSource();
                    control._cancellationToken = cts;
                    var device = Data.DevicesObject.Current;
                    var fullPath = Data.SelectedFiles.First().FullPath;

                    _ = Task.Run(async () =>
                    {
                        var text = await AdbHelper.ReadTextFileAsync(device, fullPath, cts.Token);
                        if (text is not null && !cts.IsCancellationRequested)
                            App.SafeInvoke(() => control.EditorText = text);
                    }, cts.Token);
                }
            }
            else
            {
                control.NoPreviewTextBlock.Visibility = Visibility.Visible;
            }
        }
        else
        {
            control.SelectionInfoItems.Clear();
            control.PermissionsItems.Clear();
            control.MountOptionsItems.Clear();
            control.UnsubscribeMountOptionsDrive();
            control.InvalidSelectionBorder.Visibility = Visibility.Visible;

            if (files.Count() == 1)
            {
                var item = files.First();
                if (item is FileClass f)
                {
                    control.File = f;
                    f.PropertyChanged += control.OnFileFullPathChanged;
                    control.FileNameTextBlock.Text = f.DisplayName;
                    control.FileNameTextBlock.FlowDirection = f.NameIsRtl
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight;

                    control.LargeFileIcon.Source = f.DragImage;
                    control.LargeFileIcon.MaxHeight = f.CacheThumbnail?.Image is null ? 128 : 192;
                    control.SmallFileIcon.Source = f.CacheThumbnail?.Image is null ? null : f.FileIcon32;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    control.PopulateThumbnailInfoItems(f);
                }
                else if (item is Package p)
                {
                    control.Package = p;
                    control.FileNameTextBlock.Text = p.DisplayName;
                    control.FileNameTextBlock.FlowDirection = FlowDirection.LeftToRight;
                    control.LargeFileIcon.Source = AppIcon;
                    control.LargeFileIcon.MaxHeight = 128;
                    control.SmallFileIcon.Source = null;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    control.PopulateThumbnailInfoItems(p);
                }
                else if (item is DriveViewModel drive)
                {
                    control.File = null;
                    control.Package = null;
                    control.Drive = drive;
                    control.FileNameTextBlock.Text = drive.DisplayName;
                    control.FileNameTextBlock.FlowDirection = Data.RuntimeSettings.IsRTL
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight;

                    var trashDrive = Data.DevicesObject.Current?.Drives.OfType<VirtualDriveViewModel>().FirstOrDefault(d => d.Type is AbstractDrive.DriveType.Trash);

                    control.LargeFileIcon.Source = drive.Type switch
                    {
                        AbstractDrive.DriveType.Trash when trashDrive?.ItemsCount == 0 => EmptyTrash.DragImage,
                        AbstractDrive.DriveType.Trash => FullTrash.DragImage,
                        AbstractDrive.DriveType.Package => AppIcon,
                        _ => DriveIcon.DragImage,
                    };

                    control.LargeFileIcon.MaxHeight = 128;
                    control.SmallFileIcon.Source = null;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    control.PopulateThumbnailInfoItems(drive);
                }
            }
            else if (files.Count() > 1)
            {
                control.File = null;
                control.Package = null;
                control.FileNameTextBlock.Text = $"{files.Count()} {Strings.Resources.S_ITEMS_SELECTED_PLURAL}";
                control.FileNameTextBlock.FlowDirection = Data.RuntimeSettings.IsRTL
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight;

                control.LargeFileIcon.Source = MultipleFiles.DragImage;
                control.LargeFileIcon.MaxHeight = 128;
                control.SmallFileIcon.Source = null;
                control.InvalidSelectionBorder.Visibility = Visibility.Visible;
            }
            else
            {
                control.File = null;
                control.Package = null;
                control.Drive = null;

                control.FileNameTextBlock.FlowDirection = Data.RuntimeSettings.IsRTL
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight;

                if (Data.CurrentPath is null)
                {
                    control.FileNameTextBlock.Text = "";
                    control.LargeFileIcon.Source = null;
                }
                else if (Data.FileActions.IsRecycleBin)
                {
                    control.FileNameTextBlock.Text = Strings.Resources.S_DRIVE_TRASH;
                    bool emptyTrash = Data.DevicesObject.Current.Drives.OfType<VirtualDriveViewModel>().First(d => d.Type is AbstractDrive.DriveType.Trash).ItemsCount == 0;
                    control.LargeFileIcon.Source = emptyTrash ? EmptyTrash.DragImage : FullTrash.DragImage;
                }
                else if (Data.FileActions.IsAppDrive)
                {
                    control.FileNameTextBlock.Text = Strings.Resources.S_DRIVE_APPS;
                    control.LargeFileIcon.Source = AppIcon;
                }
                else if (Data.FileActions.IsDriveViewVisible)
                {
                    control.FileNameTextBlock.Text = Data.DevicesObject.Current?.Name ?? "";
                    control.LargeFileIcon.Source = Phone.DragImage;
                    control.FileNameTextBlock.FlowDirection = FlowDirection.LeftToRight;
                }
                else if (Data.CurrentDrive?.Path == Data.CurrentPath)
                {
                    control.FileNameTextBlock.Text = $"{Data.CurrentDrive.DisplayName}\n{TextHelper.LTR_MARK}({Data.CurrentDrive.Path}){TextHelper.LTR_MARK}";
                    control.LargeFileIcon.Source = DriveIcon.DragImage;
                }
                else if (Data.DirList?.CurrentLocation is { } location)
                {
                    control.File = location;
                    control.FileNameTextBlock.Text = location.DisplayName;
                    control.LargeFileIcon.Source = location.DragImage;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    control.PopulateThumbnailInfoItems(location);
                }
                else
                {
                    control.FileNameTextBlock.Text = FileHelper.GetFullName(Data.CurrentPath);
                    control.LargeFileIcon.Source = new FileClass("", Data.CurrentPath, AbstractFile.FileType.Folder).DragImage;
                }

                control.LargeFileIcon.MaxHeight = 128;
                control.SmallFileIcon.Source = null;
            }
        }
    }, DispatcherPriority.Render);

    private void OnFileFullPathChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileClass.DisplayName))
            OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, SelectedFiles, SelectedFiles));
    }

    public DetailsPane()
    {
        SaveCommand = new AsyncRelayCommand(async () =>
        {
            var file = SelectedFiles.First() as FileClass;
            var result = await AdbHelper.WriteTextFileAsync(Data.DevicesObject.Current, file, EditorText, _cancellationToken.Token);

            if (result)
            {
                var text = EditorText;
                EditorText = null;
                EditorText = text;

                file.ModifiedTime = DateTime.Now;
                file.Size = Encoding.UTF8.GetByteCount(EditorText);
            }
        });

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

        ContentBox.Width = Data.Settings.DetailsPaneWidth;

        EditorTextBox.IsKeyboardFocusWithinChanged += (s, e) =>
        {
            IsEditorFocused = EditorTextBox.IsKeyboardFocusWithin || EditorTextBox.IsContextMenuOpen;
        };

        RequestModeRefresh = () =>
        {
            Mode = IsPreviewAllowed()
                ? Data.Settings.SidePane
                : SidePaneMode.Details;

            OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
        };

        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
    }

    public static bool IsPreviewAllowed() => !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive && !Data.FileActions.IsDriveViewVisible;

    private static async Task RenderPdfAsync(DetailsPane control, MemoryStream memStream, string? password, CancellationTokenSource cts)
    {
        memStream.Position = 0;
        var rasStream = memStream.AsRandomAccessStream();

        PdfDocument pdfDoc;
        try
        {
            pdfDoc = password is null
                ? await PdfDocument.LoadFromStreamAsync(rasStream)
                : await PdfDocument.LoadFromStreamAsync(rasStream, password);
        }
        catch (Exception ex) when (ex.HResult is (int)NativeMethods.HResult.ERROR_WRONG_PASSWORD)
        {
            App.SafeInvoke(() =>
            {
                control.PdfScrollViewer.Visibility = Visibility.Collapsed;
                control.PdfPagesControl.ItemsSource = null;
                control.IsPdfPasswordWrong = password is not null;
                control.IsPdfPasswordPromptVisible = true;
                control.NoPreviewTextBlock.Visibility = Visibility.Collapsed;
            });
            return;
        }

        if (cts.IsCancellationRequested) return;

        var pages = new ObservableCollection<PdfPageItem>();
        App.SafeInvoke(() =>
        {
            control.IsPdfPasswordPromptVisible = false;
            control.IsPdfPasswordWrong = false;
            control.PdfPagesControl.ItemsSource = pages;
            control.PdfScrollViewer.Visibility = Visibility.Visible;
            control.PdfScrollViewer.ScrollToTop();
        });

        uint pageCount = pdfDoc.PageCount;
        for (uint i = 0; i < pageCount; i++)
        {
            if (cts.IsCancellationRequested) break;

            using var pdfPage = pdfDoc.GetPage(i);
            var ms = new InMemoryRandomAccessStream();
            var renderOptions = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)(Data.Settings.DetailsPaneWidth / Data.RuntimeSettings.MainWindowScalingFactor / 0.75)
            };
            await pdfPage.RenderToStreamAsync(ms, renderOptions);

            if (cts.IsCancellationRequested) break;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms.AsStream();
            bmp.EndInit();
            bmp.Freeze();

            uint pageIndex = i;
            App.SafeInvoke(() => pages.Add(new PdfPageItem(bmp, (int)pageIndex + 1)));
        }

        if (cts.IsCancellationRequested) return;

        var total = pages.Count;
        App.SafeInvoke(() =>
        {
            foreach (var page in pages)
                page.Label = $"{pages.IndexOf(page) + 1}/{total}";
        });
    }

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

    private void PopulateThumbnailInfoItems(DriveViewModel drive)
    {
        SelectionInfoItems.Clear();
        UnsubscribeMountOptionsDrive();
        MountOptionsItems.Clear();

        if (drive is LogicalDriveViewModel or VirtualDriveViewModel { Type: AbstractDrive.DriveType.Temp })
        {
            SelectionInfoItems.Add(new ItemDetailsViewModel<DriveViewModel>(drive, Strings.Resources.S_MOUNT_POINT, d => d.MountPoint is null ? "" : d.MountPoint, valueIsLtr: true).Init());

            SelectionInfoItems.Add(new ItemDetailsViewModel<DriveViewModel>(drive, Strings.Resources.S_FILE_SYSTEM, d => d.FileSystem is null ? "" : d.FileSystem.ToUpper(), valueIsLtr: true).Init());

            SelectionInfoItems.Add(new ItemDetailsViewModel<DriveViewModel>(drive, Strings.Resources.S_FILE_BLOCK, d => d.BlockDevice is null ? "" : d.BlockDevice, valueIsLtr: true).Init());

            PopulateMountOptions(drive);
            SubscribeMountOptionsDrive(drive);

            if (drive.FSInfo is null)
            {
                var device = Data.DevicesObject.Current;
                var cts = new CancellationTokenSource();
                _cancellationToken = cts;
                _ = Task.Run(() => AdbHelper.ApplyMountInfo(device, cts.Token), cts.Token);
            }
        }
    }

    private void PopulateThumbnailInfoItems(Package package)
    {
        SelectionInfoItems.Clear();

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_ITEM_LOCATION, f => FileHelper.GetParentPath(f.Path), valueIsLtr: true));

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_TYPE, p => $"{p.Type}"));

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_USER_ID, p => $"{p.Uid}"));
        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_VERSION, p => p.VersionName ?? $"{p.Version}").Init());

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_DATE_MODIFIED, p => p.LastUpdateTime.HasValue ? p.LastUpdateTime.Value.ToString(Data.Settings.ActualFormatCulture) : "").Init());

        var cts = new CancellationTokenSource();
        _cancellationToken = cts;

        if (package.VersionName is null || package.LastUpdateTime is null)
        {
            var device = Data.DevicesObject.Current;
            _ = Task.Run(() => AdbHelper.FetchDumpsysInfoAsync(device, package, cts.Token), cts.Token);
        }
    }

    private void PopulateThumbnailInfoItems(FileClass file)
    {
        SelectionInfoItems.Clear();
        PermissionsItems.Clear();

        if (file.Type is not AbstractFile.FileType.Unknown)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_TYPE, f => f.FolderViewModel.TypeName));

        if (Data.FileActions.IsRecycleBin)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_ORIGINAL_LOCATION, f => f.TrashIndex.OriginalPath, valueIsLtr: true));
        else
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_ITEM_LOCATION, f => f.ParentPath, valueIsLtr: true));

        if (file.Type is AbstractFile.FileType.File && file.Size.HasValue)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString));

        if (file.ModifiedTime.HasValue)
        {
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_FILE_INFO_MODIFIED, f => f.FolderViewModel.ModifiedTimeWithOffsetString, valueIsLtr: true).Init());

            if (Data.CurrentDrive?.Restrictions.SupportsAccessTime is true)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_DATE_ACCESSED, f => f.FolderViewModel.LastAccessTimeString, valueIsLtr: true).Init());

            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_CREATION_TIME, f => f.FolderViewModel.CreationTimeString, valueIsLtr: true).Init());
        }

        if (Data.FileActions.IsRecycleBin)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_DATE_DELETED, f => f.TrashIndex.ModifiedTimeString));
        if (file.IsLink)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_FILE_TYPE_LINK, f => f.LinkTarget, valueIsLtr: true));

        if (file.CacheThumbnail is { } thumb)
        {
            var info = thumb.Info;

            if (info.Resolution.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_PICTURE_DIMENSIONS, f => f.CacheThumbnail!.Value.Info.ResolutionString, valueIsLtr: true));

            if (info.Duration.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_VIDEO_DURATION, f => f.CacheThumbnail!.Value.Info.DurationString));

            if (info.FNumber.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_IMAGE_F_STOP, f => f.CacheThumbnail!.Value.Info.FNumberString, valueIsLtr: true));

            if (info.ExposureTime.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_IMAGE_EXPOSURE, f => f.CacheThumbnail!.Value.Info.ExposureTimeString));

            if (info.ISO.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_IMAGE_ISO, f => f.CacheThumbnail!.Value.Info.ISOString, valueIsLtr: true));

            if (info.Bitrate.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_VIDEO_BITRATE, f => f.CacheThumbnail!.Value.Info.BitrateString, valueIsLtr: true));
        }

        if (file.Permissions.HasValue)
        {
            static string userSelector(FileClass f)
            {
                string user = f.User is null ? "" : $"({f.User})";
                string permission = f.FolderViewModel.UserPermissionsString;

                return Data.RuntimeSettings.IsRTL
                    ? $"{permission} {user}"
                    : $"{user} {permission}";
            }

            static string groupSelector(FileClass f)
            {
                string group = f.Group is null ? "" : $"({f.Group})";
                string permission = f.FolderViewModel.GroupPermissionsString;

                return Data.RuntimeSettings.IsRTL
                    ? $"{permission} {group}"
                    : $"{group} {permission}";
            }

            PermissionsItems.Add(new ItemDetailsViewModel<FileClass>(file,
                                                                     Strings.Resources.S_FILE_PERM_USER,
                                                                     userSelector,
                                                                     valueIsLtr: true,
                                                                     useConsoleFont: true).Init());

            PermissionsItems.Add(new ItemDetailsViewModel<FileClass>(file,
                                                                     Strings.Resources.S_FILE_PERM_GROUP,
                                                                     groupSelector,
                                                                     valueIsLtr: true,
                                                                     useConsoleFont: true).Init());

            PermissionsItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_FILE_PERM_OTHER, f => $"{f.FolderViewModel.OtherPermissionsString}", valueIsLtr: true, useConsoleFont: true));

            var cts = new CancellationTokenSource();
            _cancellationToken = cts;

            if (file.User is null || file.Group is null)
            {
                _ = Task.Run(() => file.UpdateExtraInfo(cts.Token), cts.Token);
            }
        }
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

    private void PdfPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        IsPdfPasswordWrong = false;
    }
}
