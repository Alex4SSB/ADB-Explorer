using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsPane.xaml
/// </summary>
public partial class DetailsPane : UserControl
{
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
        DependencyProperty.Register("IsOpen", typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public string? EditorText
    {
        get => (string?)GetValue(EditorTextProperty);
        set => SetValue(EditorTextProperty, value);
    }

    public static readonly DependencyProperty EditorTextProperty =
        DependencyProperty.Register("EditorText", typeof(string),
          typeof(DetailsPane), new PropertyMetadata(null));

    public bool IsEditorFocused
    {
        get => (bool)GetValue(IsEditorFocusedProperty);
        set => SetValue(IsEditorFocusedProperty, value);
    }

    public static readonly DependencyProperty IsEditorFocusedProperty =
        DependencyProperty.Register("IsEditorFocused", typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public IEnumerable<IBrowserItem> SelectedFiles
    {
        get => (IEnumerable<IBrowserItem>)GetValue(SelectedFilesProperty);
        set => SetValue(SelectedFilesProperty, value);
    }

    public static readonly DependencyProperty SelectedFilesProperty =
        DependencyProperty.Register("SelectedFiles", typeof(IEnumerable<IBrowserItem>),
          typeof(DetailsPane), new PropertyMetadata(Array.Empty<IBrowserItem>(), OnSelectedFilesChanged));

    public FileClass? File
    {
        get => (FileClass?)GetValue(FileProperty);
        private set => SetValue(FileProperty, value);
    }

    public static readonly DependencyProperty FileProperty =
        DependencyProperty.Register("File", typeof(FileClass),
          typeof(DetailsPane), new PropertyMetadata(null));

    public Package? Package
    {
        get => (Package?)GetValue(PackageProperty);
        private set => SetValue(PackageProperty, value);
    }

    public static readonly DependencyProperty PackageProperty =
        DependencyProperty.Register("Package", typeof(Package),
          typeof(DetailsPane), new PropertyMetadata(null));

    public ObservableCollection<IDetailsViewModel> ThumbnailInfoItems { get; } = [];

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand PdfUnlockCommand { get; }

    public bool IsPdfPasswordPromptVisible
    {
        get => (bool)GetValue(IsPdfPasswordPromptVisibleProperty);
        set => SetValue(IsPdfPasswordPromptVisibleProperty, value);
    }

    public static readonly DependencyProperty IsPdfPasswordPromptVisibleProperty =
        DependencyProperty.Register("IsPdfPasswordPromptVisible", typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public bool IsPdfPasswordWrong
    {
        get => (bool)GetValue(IsPdfPasswordWrongProperty);
        set => SetValue(IsPdfPasswordWrongProperty, value);
    }

    public static readonly DependencyProperty IsPdfPasswordWrongProperty =
        DependencyProperty.Register("IsPdfPasswordWrong", typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    private static readonly FileClass MultipleFiles = new("MultipleFiles", "/MultipleFiles", AbstractFile.FileType.MultipleFiles);
    private static readonly FileClass Drive = new("Drive", "/Drive", AbstractFile.FileType.Drive);
    private static readonly FileClass EmptyTrash = new("RecycleBin", "/RecycleBin", AbstractFile.FileType.EmptyTrash);
    private static readonly FileClass FullTrash = new("RecycleBin", "/RecycleBin", AbstractFile.FileType.FullTrash);
    private static readonly BitmapSource AppIcon = FileToIconConverter.LoadBitmap(new System.Drawing.Icon(Properties.AppGlobal.APK_icon, 128, 128).ToBitmap());

    private CancellationTokenSource? _cancellationToken;
    private MemoryStream? _pdfMemoryStream;

    private static void OnSelectedFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DetailsPane)d;
        var files = (IEnumerable<IBrowserItem>)e.NewValue;

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

        if (Data.Settings.SidePane is AppSettings.SidePaneMode.Preview)
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
            if (files.Count() == 1)
            {
                var item = files.First();
                if (item is FileClass f)
                {
                    control.File = f;
                    control.FileNameTextBlock.Text = f.DisplayName;
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
                    control.LargeFileIcon.Source = AppIcon;
                    control.LargeFileIcon.MaxHeight = 128;
                    control.SmallFileIcon.Source = null;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    control.PopulateThumbnailInfoItems(p);
                }
            }
            else if (files.Count() > 1)
            {
                control.File = null;
                control.Package = null;
                control.FileNameTextBlock.Text = $"{files.Count()} {Strings.Resources.S_ITEMS_SELECTED_PLURAL}";
                control.LargeFileIcon.Source = MultipleFiles.DragImage;
                control.LargeFileIcon.MaxHeight = 128;
                control.SmallFileIcon.Source = null;
                control.InvalidSelectionBorder.Visibility = Visibility.Visible;
            }
            else
            {
                control.File = null;
                control.Package = null;
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
                else if (Data.CurrentDrive?.Path == Data.CurrentPath)
                {
                    control.FileNameTextBlock.Text = $"{Data.CurrentDrive.DisplayName}{(Data.CurrentDrive.Path == "/" ? " " : "\n")}({Data.CurrentDrive.Path})";
                    control.LargeFileIcon.Source = Drive.DragImage;
                }
                else
                {
                    control.FileNameTextBlock.Text = FileHelper.GetFullName(Data.CurrentPath);
                    control.LargeFileIcon.Source = new FileClass("", Data.CurrentPath, AbstractFile.FileType.Folder).DragImage;
                }

                control.LargeFileIcon.MaxHeight = 128;
                control.SmallFileIcon.Source = null;
                control.InvalidSelectionBorder.Visibility = Visibility.Visible;
            }
        }
    }

    public DetailsPane()
    {
        SaveCommand = new AsyncRelayCommand(async () =>
        {
            var file = SelectedFiles.First() as FileClass;
            var result = await AdbHelper.WriteTextFileAsync(Data.DevicesObject.Current, file.FullPath, EditorText, _cancellationToken.Token);

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

        Data.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.SidePane))
                OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
        };

        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
    }

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

    private void PopulateThumbnailInfoItems(Package package)
    {
        ThumbnailInfoItems.Clear();

        ThumbnailInfoItems.Add(new PackageDetailsViewModel(package, Strings.Resources.S_ITEM_LOCATION, f => FileHelper.GetParentPath(f.Path), valueIsLtr: true));

        ThumbnailInfoItems.Add(new PackageDetailsViewModel(package, Strings.Resources.S_COLUMN_TYPE, p => $"{p.Type}"));

        ThumbnailInfoItems.Add(new PackageDetailsViewModel(package, Strings.Resources.S_COLUMN_USER_ID, p => $"{p.Uid}"));

        ThumbnailInfoItems.Add(new PackageDetailsViewModel(package, Strings.Resources.S_COLUMN_VERSION, p => p.VersionName ?? $"{p.Version}").Init());

        ThumbnailInfoItems.Add(new PackageDetailsViewModel(package, Strings.Resources.S_COLUMN_DATE_MODIFIED, p => p.LastUpdateTime.HasValue ? p.LastUpdateTime.Value.ToString(Data.Settings.ActualUICulture) : "").Init());

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
        ThumbnailInfoItems.Clear();

        if (file.Type is not AbstractFile.FileType.Unknown)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_COLUMN_TYPE, f => f.FolderViewModel.TypeName));

        if (Data.FileActions.IsRecycleBin)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_COLUMN_ORIGINAL_LOCATION, f => f.TrashIndex.OriginalPath, valueIsLtr: true));
        else
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_ITEM_LOCATION, f => f.ParentPath, valueIsLtr: true));

        if (file.Type is AbstractFile.FileType.File && file.Size.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString));

        if (file.ModifiedTime.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_COLUMN_DATE_MODIFIED, f => f.FolderViewModel.ModifiedTimeString));

        if (Data.FileActions.IsRecycleBin)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_COLUMN_DATE_DELETED, f => f.TrashIndex.ModifiedTimeString));

        if (file.IsLink)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_FILE_TYPE_LINK, f => f.LinkTarget, valueIsLtr: true));

        if (file.CacheThumbnail is not { } thumb)
            return;

        var info = thumb.Info;

        if (info.Resolution.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_PICTURE_DIMENSIONS, f => f.CacheThumbnail!.Value.Info.ResolutionString, valueIsLtr: true));

        if (info.Duration.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_VIDEO_DURATION, f => f.CacheThumbnail!.Value.Info.DurationString));

        if (info.FNumber.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_IMAGE_F_STOP, f => f.CacheThumbnail!.Value.Info.FNumberString, valueIsLtr: true));

        if (info.ExposureTime.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_IMAGE_EXPOSURE, f => f.CacheThumbnail!.Value.Info.ExposureTimeString));

        if (info.ISO.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_IMAGE_ISO, f => f.CacheThumbnail!.Value.Info.ISOString, valueIsLtr: true));

        if (info.Bitrate.HasValue)
            ThumbnailInfoItems.Add(new FileDetailsViewModel(file, Strings.Resources.S_VIDEO_BITRATE, f => f.CacheThumbnail!.Value.Info.BitrateString, valueIsLtr: true));
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = ContentBox.ActualWidth - e.HorizontalChange;
        if (newWidth > MinWidth && newWidth < MaxWidth)
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
