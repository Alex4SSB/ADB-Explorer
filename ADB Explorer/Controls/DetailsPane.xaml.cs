using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media.Animation;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsPane.xaml
/// </summary>
public partial class DetailsPane : UserControl
{
    public sealed class PreviewSyntaxOption(string displayName, string? highlightingName, bool disableHighlighting = false, bool isHex = false)
    {
        public string DisplayName { get; } = displayName;

        /// <summary>Null means Automatic (pick by file extension) unless <see cref="DisableHighlighting"/>.</summary>
        public string? HighlightingName { get; } = highlightingName;

        /// <summary>When true, no syntax highlighting is applied (None and Hex).</summary>
        public bool DisableHighlighting { get; } = disableHighlighting;

        public bool IsHex { get; } = isHex;
    }

    private DriveViewModel? _mountOptionsDrive;
    private LogicalDeviceViewModel? _previewMountDevice;
    private VirtualDriveViewModel? _trashCountDrive;
    private string? _previewFileExtension;
    private byte[]? _previewBytes;
    private bool _updatingSyntaxSelection;

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

    public bool IsEditingPermissions
    {
        get => (bool)GetValue(IsEditingPermissionsProperty);
        set => SetValue(IsEditingPermissionsProperty, value);
    }

    public static readonly DependencyProperty IsEditingPermissionsProperty =
        DependencyProperty.Register(nameof(IsEditingPermissions), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false, OnIsEditingPermissionsChanged));

    private static void OnIsEditingPermissionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DetailsPane pane) return;

        pane.EditPermissionsTooltip = pane.IsEditingPermissions
            ? Strings.Resources.S_CANCEL
            : Strings.Resources.S_MENU_EDIT;

        pane.UpdateIsEditorFocused();

        if (pane.IsEditingPermissions)
            pane.BeginPermissionsEdit();
    }

    public bool CanEditPermissions
    {
        get => (bool)GetValue(CanEditPermissionsProperty);
        set => SetValue(CanEditPermissionsProperty, value);
    }

    public static readonly DependencyProperty CanEditPermissionsProperty =
        DependencyProperty.Register(nameof(CanEditPermissions), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public string EditPermissionsTooltip
    {
        get => (string)GetValue(EditPermissionsTooltipProperty);
        set => SetValue(EditPermissionsTooltipProperty, value);
    }

    public static readonly DependencyProperty EditPermissionsTooltipProperty =
        DependencyProperty.Register(nameof(EditPermissionsTooltip), typeof(string),
          typeof(DetailsPane), new PropertyMetadata(Strings.Resources.S_BUTTON_CHANGE));

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

    public string? EditorText
    {
        get => (string?)GetValue(EditorTextProperty);
        set => SetValue(EditorTextProperty, value);
    }

    public static readonly DependencyProperty EditorTextProperty =
        DependencyProperty.Register(nameof(EditorText), typeof(string),
          typeof(DetailsPane), new PropertyMetadata(null, OnEditorTextChanged));

    private static void OnEditorTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DetailsPane pane)
            return;

        pane.UpdateSyntaxSelectorVisibility();
        pane.ApplyPreviewSyntaxHighlighting();
    }

    public bool IsEditorReadOnly
    {
        get => (bool)GetValue(IsEditorReadOnlyProperty);
        set => SetValue(IsEditorReadOnlyProperty, value);
    }

    public static readonly DependencyProperty IsEditorReadOnlyProperty =
        DependencyProperty.Register(nameof(IsEditorReadOnly), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public PreviewSyntaxOption? SelectedSyntax
    {
        get => (PreviewSyntaxOption?)GetValue(SelectedSyntaxProperty);
        set => SetValue(SelectedSyntaxProperty, value);
    }

    public static readonly DependencyProperty SelectedSyntaxProperty =
        DependencyProperty.Register(nameof(SelectedSyntax), typeof(PreviewSyntaxOption),
          typeof(DetailsPane), new PropertyMetadata(null, OnSelectedSyntaxChanged));

    public bool IsSyntaxSelectorVisible
    {
        get => (bool)GetValue(IsSyntaxSelectorVisibleProperty);
        private set => SetValue(IsSyntaxSelectorVisibleProperty, value);
    }

    public static readonly DependencyProperty IsSyntaxSelectorVisibleProperty =
        DependencyProperty.Register(nameof(IsSyntaxSelectorVisible), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public IReadOnlyList<PreviewSyntaxOption> SyntaxOptions { get; } = BuildSyntaxOptions();

    private static IReadOnlyList<PreviewSyntaxOption> BuildSyntaxOptions()
    {
        var automatic = new PreviewSyntaxOption(Strings.Resources.S_PREVIEW_SYNTAX_AUTOMATIC, null);
        var hex = new PreviewSyntaxOption("Hex", null, disableHighlighting: true, isHex: true);
        var none = new PreviewSyntaxOption(Strings.Resources.S_DISABLED, null, disableHighlighting: true);
        var named = HighlightingManager.Instance.HighlightingDefinitions
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new PreviewSyntaxOption(d.Name, d.Name));
        return [automatic, hex, none, .. named];
    }

    private static void OnSelectedSyntaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DetailsPane pane || pane._updatingSyntaxSelection)
            return;

        var wasHex = e.OldValue is PreviewSyntaxOption { IsHex: true };
        var isHex = pane.SelectedSyntax?.IsHex is true;
        if (wasHex != isHex)
            pane.SwitchPreviewEncoding(fromHex: wasHex);

        pane.ApplyPreviewSyntaxHighlighting();
    }

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
    public PermissionsEditViewModel PermissionsEdit { get; } = new();
    

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand SavePermissionsCommand { get; }
    public RelayCommand EditPermissionsCommand { get; }
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
    private static readonly BitmapSource AppIcon = DefaultAndroidPackageIcon.Bitmap;

    private CancellationTokenSource? _cancellationToken;
    private CancellationTokenSource? _extraInfoCts;
    private string? _extraInfoPath;
    private MemoryStream? _pdfMemoryStream;
    private LogicalDeviceViewModel? _permissionDevice;

    private static void OnSelectedFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => App.SafeBeginInvoke(() =>
    {
        var control = (DetailsPane)d;
        control.UnsubscribeTrashCountDrive();
        var files = (IEnumerable<IBrowserItem>)e.NewValue;

        FileClass? oldFile = e.OldValue is IEnumerable<IBrowserItem> oldFiles
            && oldFiles.Count() == 1
            && oldFiles.First() is FileClass previous
            ? previous
            : null;

        FileClass? newFile = files.Count() == 1 && files.First() is FileClass next ? next : null;

        // Selection refresh rebuilds the SelectedFiles enumerable even when the file is unchanged.
        // Cancelling an in-flight custom pull then loses ThumbnailUpdated and can miss the UI update.
        if (oldFile is not null)
        {
            oldFile.PropertyChanged -= control.OnFileFullPathChanged;
            if (!ReferenceEquals(oldFile, newFile))
                oldFile.CancelCacheThumbnailLoading();
        }

        control.UnsubscribePreviewMounts();
        control.UnsubscribePermissionDevice();
        control.ClearPhotoPreview();
        control.IsEditingPermissions = false;
        control.CanEditPermissions = false;

        control.EditorText = null;
        control._previewBytes = null;
        control.IsEditorReadOnly = false;
        control.ResetSyntaxToAutomatic();
        control._previewFileExtension = null;
        control.UpdateSyntaxSelectorVisibility();
        control.ApplyPreviewSyntaxHighlighting();
        control.PdfScrollViewer.Visibility = Visibility.Collapsed;
        control.PdfPagesControl.ItemsSource = null;
        control.IsPdfPasswordPromptVisible = false;
        control.IsPdfPasswordWrong = false;
        control._pdfMemoryStream = null;
        control._cancellationToken?.Cancel();
        control._cancellationToken = null;
        control._extraInfoCts?.Cancel();
        control._extraInfoCts = null;
        control._extraInfoPath = null;
        if (files.FirstOrDefault() is FileClass fc && string.IsNullOrEmpty(fc.FullName))
            return;

        if (control.Mode is SidePaneMode.Preview)
        {
            control.NoPreviewTextBlock.Text = files.Any()
                ? Strings.Resources.S_PREVIEW_INVALID
                : Strings.Resources.S_PREVIEW_EMPTY_SELECTION;

            if (files.Count() == 1
                && !Data.FileActions.IsRecycleBin
                && files.First() is FileClass previewFile
                && ThumbnailService.IsPhotoPaneThumbnailCandidate(previewFile))
            {
                control.ShowPhotoPreview(previewFile);
            }
            else if (files.Count() == 1
                && !Data.FileActions.IsRecycleBin
                && files.First() is FileClass file
                && file.Type is AbstractFile.FileType.File
                && !AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
                && !file.IsApk
                && ArchiveHelper.GetFamily(file.FullName) is ArchiveFamily.None
                && !file.IsLink
                && file.Size / 1000 < Data.Settings.MaxPreviewFileSize)
            {
                control.NoPreviewTextBlock.Visibility = Visibility.Collapsed;

                var device = Data.DevicesObject.Current;
                var deviceId = device?.ID ?? "";
                control.SubscribePreviewMounts(device);
                control.IsEditorReadOnly = IsPreviewTextReadOnly(file, device);
                control._previewFileExtension = file.Extension;
                control.UpdateSyntaxSelectorVisibility();
                control.ApplyPreviewSyntaxHighlighting();

                if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    control.IsSyntaxSelectorVisible = false;
                    var cts = new CancellationTokenSource();
                    control._cancellationToken = cts;
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
                    var fullPath = file.FullPath;

                    _ = Task.Run(async () =>
                    {
                        var stream = await AdbHelper.ReadFileAsStreamAsync(device, fullPath, cts.Token);
                        if (stream is null || cts.IsCancellationRequested) return;

                        var bytes = stream.ToArray();
                        if (cts.IsCancellationRequested) return;

                        App.SafeInvoke(() =>
                        {
                            control._previewBytes = bytes;
                            control.ApplyPreviewContent();
                        });
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
                    f.PropertyChanged -= control.OnFileFullPathChanged;
                    f.PropertyChanged += control.OnFileFullPathChanged;
                    control.FileNameTextBlock.Text = f.DisplayName;
                    control.FileNameTextBlock.FlowDirection = f.NameIsRtl
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight;

                    control.LargeFileIcon.Source = f.DragImage;
                    control.LargeFileIcon.MaxHeight = f.CacheThumbnail?.Image is null ? 128 : 192;
                    control.SmallFileIcon.Source = f.CacheThumbnail?.Image is null ? null : f.FileIcon32;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    f.BeginLoadCacheThumbnail();
                    if (f.IsApk)
                        ApkIconService.BeginLoadForFile(f, ApkIconService.ApkLoadPriority.Selected);
                    // BeginLoad is async; if the pane cache was already warm, refresh now.
                    if (f.CacheThumbnail?.Image is not null)
                        control.UpdateFileThumbnailDisplay(f);

                    control.PopulateThumbnailInfoItems(f);
                }
                else if (item is Package p)
                {
                    if (control.Package is not null)
                        control.Package.PropertyChanged -= control.OnPackagePropertyChanged;

                    control.Package = p;
                    p.PropertyChanged -= control.OnPackagePropertyChanged;
                    p.PropertyChanged += control.OnPackagePropertyChanged;
                    control.FileNameTextBlock.Text = p.DisplayName;
                    control.FileNameTextBlock.FlowDirection = FlowDirection.LeftToRight;
                    control.LargeFileIcon.Source = p.IconViewModel.LargeIcon;
                    control.LargeFileIcon.MaxHeight = 128;
                    control.SmallFileIcon.Source = null;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    control.PopulateThumbnailInfoItems(p);
                    ApkIconService.BeginLoadForPackage(p, ApkIconService.ApkLoadPriority.Selected);
                }
                else if (item is DriveViewModel drive)
                {
                    if (control.Package is not null)
                        control.Package.PropertyChanged -= control.OnPackagePropertyChanged;

                    control.File = null;
                    control.Package = null;
                    control.Drive = drive;
                    control.FileNameTextBlock.Text = drive.DisplayName;
                    control.FileNameTextBlock.FlowDirection = Data.RuntimeSettings.IsRTL
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight;

                    VirtualDriveViewModel? trashDrive = null;
                    if (drive is VirtualDriveViewModel virtualDrive
                        && virtualDrive.Type is AbstractDrive.DriveType.Trash)
                        trashDrive = virtualDrive;
                    else if (drive.Type is AbstractDrive.DriveType.Trash)
                        trashDrive = TrashHelper.GetTrashDrive(Data.DevicesObject.Current);

                    control.SubscribeTrashCountDrive(drive.Type is AbstractDrive.DriveType.Trash ? trashDrive : null);
                    control.LargeFileIcon.Source = drive.Type switch
                    {
                        AbstractDrive.DriveType.Trash => TrashIcon(trashDrive),
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
                if (control.Package is not null)
                    control.Package.PropertyChanged -= control.OnPackagePropertyChanged;

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
                if (control.File is FileClass previousFile)
                    previousFile.PropertyChanged -= control.OnFileFullPathChanged;

                if (control.Package is not null)
                    control.Package.PropertyChanged -= control.OnPackagePropertyChanged;

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
                    var trashDrive = TrashHelper.GetTrashDrive(Data.DevicesObject.Current);
                    control.SubscribeTrashCountDrive(trashDrive);
                    control.LargeFileIcon.Source = TrashIcon(trashDrive);
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
                else if (Data.FileActions.IsSearchMode)
                {
                    control.FileNameTextBlock.Text = Data.FileActions.ExplorerFilter;
                    control.LargeFileIcon.Source = MultipleFiles.DragImage;
                }
                else if (Data.CurrentDrive?.Path == Data.CurrentPath)
                {
                    control.FileNameTextBlock.Text = $"{Data.CurrentDrive.DisplayName}\n{TextHelper.LTR_MARK}({Data.CurrentDrive.Path}){TextHelper.LTR_MARK}";
                    control.LargeFileIcon.Source = DriveIcon.DragImage;
                }
                else if (Data.DirList?.CurrentLocation is { } location)
                {
                    control.File = location;
                    location.PropertyChanged -= control.OnFileFullPathChanged;
                    location.PropertyChanged += control.OnFileFullPathChanged;
                    control.FileNameTextBlock.Text = location.DisplayName;
                    control.LargeFileIcon.Source = location.DragImage;
                    control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                    // Skip redundant loads when selection refresh is replayed for the same folder.
                    if (location.CacheThumbnail?.Image is null)
                        location.BeginLoadCacheThumbnail();
                    else
                        control.UpdateFileThumbnailDisplay(location);

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

    public void RefreshSelection() =>
        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, SelectedFiles, SelectedFiles));

    /// <summary>
    /// Copies the live explorer selection into <see cref="SelectedFiles"/>.
    /// Used when the pane opens; while closed, <see cref="ExplorerPageHeader"/> skips that update.
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

    private void UpdateFileThumbnailDisplay(FileClass file)
    {
        LargeFileIcon.Source = file.DragImage;
        LargeFileIcon.MaxHeight = file.CacheThumbnail?.Image is null ? 128 : 192;
        SmallFileIcon.Source = file.CacheThumbnail?.Image is null ? null : file.FileIcon32;
        PopulateThumbnailInfoItems(file);
    }

    private void ShowPhotoPreview(FileClass file)
    {
        File = file;
        file.PropertyChanged -= OnFileFullPathChanged;
        file.PropertyChanged += OnFileFullPathChanged;

        // Never use DragImage here — it falls back to the shell file icon.
        PhotoPreviewImage.Source = null;
        PhotoPreviewScrollViewer.Visibility = Visibility.Collapsed;
        NoPreviewTextBlock.Visibility = Visibility.Collapsed;

        if (file.CacheThumbnail?.Image is BitmapSource cached)
        {
            ApplyPhotoPreviewImage(cached);
            return;
        }

        file.BeginLoadCacheThumbnail();

        if (file.CacheThumbnail?.Image is BitmapSource afterLoad)
            ApplyPhotoPreviewImage(afterLoad);
        else if (!ThumbnailService.IsPaneThumbnailLoading(file))
            NoPreviewTextBlock.Visibility = Visibility.Visible;
    }

    private void UpdatePhotoPreviewFromThumbnail(FileClass file)
    {
        if (Mode is not SidePaneMode.Preview)
            return;

        if (file.CacheThumbnail?.Image is BitmapSource image)
        {
            ApplyPhotoPreviewImage(image);
            return;
        }

        if (ThumbnailService.IsPaneThumbnailLoading(file))
            return;

        ClearPhotoPreview();
        NoPreviewTextBlock.Visibility = Visibility.Visible;
    }

    private void ApplyPhotoPreviewImage(BitmapSource image)
    {
        NoPreviewTextBlock.Visibility = Visibility.Collapsed;
        PhotoPreviewScrollViewer.Visibility = Visibility.Visible;
        PhotoPreviewImage.Source = image;
    }

    private void ClearPhotoPreview()
    {
        PhotoPreviewImage.Source = null;
        PhotoPreviewScrollViewer.Visibility = Visibility.Collapsed;
    }

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
                result = await AdbHelper.WriteBytesFileAsync(Data.DevicesObject.Current, file, bytes, token);
                byteCount = bytes.Length;
                if (result)
                    _previewBytes = bytes;
            }
            else
            {
                result = await AdbHelper.WriteTextFileAsync(Data.DevicesObject.Current, file, EditorText ?? "", token);
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
            Mode = IsPreviewAllowed()
                ? Data.Settings.SidePane
                : SidePaneMode.Details;

            OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
        };

        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
    }

    private void ApplyPreviewContent()
    {
        if (_previewBytes is null)
            return;

        if (SelectedSyntax?.IsHex is true)
            EditorText = HexText.Format(_previewBytes);
        else
            EditorText = DecodePreviewText(_previewBytes);
    }

    private static string DecodePreviewText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void SwitchPreviewEncoding(bool fromHex)
    {
        var unsaved = EditorTextBox?.HasUnsavedChanges is true;

        if (unsaved)
        {
            if (fromHex)
                _previewBytes = HexText.Parse(EditorText);
            else
                _previewBytes = Encoding.UTF8.GetBytes(EditorText ?? "");
        }

        ApplyPreviewContent();
        if (unsaved)
            EditorTextBox?.MarkAsUnsaved();
    }

    private void ResetSyntaxToAutomatic()
    {
        _updatingSyntaxSelection = true;
        SelectedSyntax = SyntaxOptions[0];
        _updatingSyntaxSelection = false;
    }

    private void UpdateSyntaxSelectorVisibility()
    {
        IsSyntaxSelectorVisible = EditorText is not null && Mode is SidePaneMode.Preview;
    }

    private void ApplyPreviewSyntaxHighlighting()
    {
        if (EditorTextBox is null)
            return;

        if (EditorText is null || SelectedSyntax?.DisableHighlighting is true)
        {
            EditorTextBox.SetSyntaxHighlighting(null);
            return;
        }

        IHighlightingDefinition? definition = null;
        if (SelectedSyntax?.HighlightingName is { } name)
            definition = HighlightingManager.Instance.GetDefinition(name);
        else if (!string.IsNullOrEmpty(_previewFileExtension))
            definition = HighlightingManager.Instance.GetDefinitionByExtension(_previewFileExtension);

        EditorTextBox.SetSyntaxHighlighting(definition);
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
        => trash?.ItemsCount == 0 ? EmptyTrash.DragImage : FullTrash.DragImage;

    private static bool IsPreviewTextReadOnly(FileClass file, LogicalDeviceViewModel? device)
    {
        var deviceId = device?.ID ?? "";
        if (ArchiveHelper.IsMemberPreviewReadOnly(file.FullPath, deviceId))
            return true;

        return DriveHelper.GetRestrictions(file.FullPath, device).ReadOnly;
    }

    private void SubscribePreviewMounts(LogicalDeviceViewModel? device)
    {
        if (_previewMountDevice == device)
            return;

        UnsubscribePreviewMounts();
        _previewMountDevice = device;
        if (_previewMountDevice is not null)
            _previewMountDevice.PropertyChanged += OnPreviewMountsChanged;

        if (AdbHelper.NeedsMountInfo(_previewMountDevice))
            _ = Task.Run(() => AdbHelper.ApplyMountInfo(_previewMountDevice, Data.DeviceCts.Token), Data.DeviceCts.Token);
    }

    private void UnsubscribePreviewMounts()
    {
        if (_previewMountDevice is null)
            return;

        _previewMountDevice.PropertyChanged -= OnPreviewMountsChanged;
        _previewMountDevice = null;
    }

    private void OnPreviewMountsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LogicalDeviceViewModel.Mounts)
            and not nameof(LogicalDeviceViewModel.HasRootShell))
            return;

        App.SafeInvoke(UpdatePreviewEditorReadOnly);
    }

    private void UpdatePreviewEditorReadOnly()
    {
        if (Mode is not SidePaneMode.Preview)
            return;

        if (SelectedFiles?.Count() != 1 || SelectedFiles.First() is not FileClass file)
            return;

        if (file.Type is not AbstractFile.FileType.File)
            return;

        IsEditorReadOnly = IsPreviewTextReadOnly(file, Data.DevicesObject.Current);
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

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_PACKAGE_ID, p => p.Name, valueIsLtr: true));

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_ITEM_LOCATION, f => FileHelper.GetParentPath(f.Path), valueIsLtr: true));

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_TYPE, p => $"{p.Type}"));

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_USER_ID, p => $"{p.Uid}"));
        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_VERSION, p => p.VersionName ?? $"{p.Version}").Init());

        SelectionInfoItems.Add(new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_DATE_MODIFIED, p => p.LastUpdateTime.HasValue ? p.LastUpdateTime.Value.ToString(Data.Settings.ActualFormatCulture) : "").Init());

        _cancellationToken?.Cancel();
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
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_ITEM_LOCATION, FormatArchiveLocation, valueIsLtr: true));

        var archiveSummaryPath = TryGetArchiveSummaryPath(file);

        if (archiveSummaryPath is not null
            && ArchiveListing.TryGetArchiveSummary(archiveSummaryPath, out var archiveSummary))
        {
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_COMPRESSED_SIZE, _ => archiveSummary.CompressedSize.BytesToSize(true), valueIsLtr: true));

            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_PACKED_SIZE, _ => archiveSummary.UncompressedSize.BytesToSize(true), valueIsLtr: true));

            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_COMPRESSION_RATIO, _ => archiveSummary.Ratio, valueIsLtr: true));

            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_MENU_FILES, _ => $"{archiveSummary.FileCount}", valueIsLtr: true));
        }
        else if (archiveSummaryPath is not null && ArchiveHelper.IsTarFamily(archiveSummaryPath))
        {
            if (file.ShellLsSize is >= 0 || file.Size.HasValue)
            {
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                    file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString, valueIsLtr: true).Init());
            }
        }
        else
        {
            if (file.Type is AbstractFile.FileType.File && file.Size.HasValue)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString).Init());

            if (file.CompressedSize is long compressedSize)
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COMPRESSED_SIZE, _ => compressedSize.BytesToSize(true), valueIsLtr: true));

            if (!string.IsNullOrEmpty(file.CompressionRatio))
                SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COMPRESSION_RATIO, f => f.CompressionRatio!, valueIsLtr: true));
        }

        if (archiveSummaryPath is null && !string.IsNullOrEmpty(file.CompressionMethod))
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COMPRESSION_METHOD, f => ArchiveHelper.GetZipMethodDisplayName(f.CompressionMethod!), valueIsLtr: true));

        if (!string.IsNullOrEmpty(file.Crc32))
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, "CRC-32", f => f.Crc32!.ToUpperInvariant(), valueIsLtr: true, useConsoleFont: true));

        if (file.ModifiedTime.HasValue)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_FILE_INFO_MODIFIED, f => f.FolderViewModel.ModifiedTimeWithOffsetString, valueIsLtr: true).Init());

        if (DriveHelper.GetRestrictions(file.FullPath).SupportsAccessTime)
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                file,
                Strings.Resources.S_DATE_ACCESSED,
                f => f.LastAccessTime.HasValue ? f.FolderViewModel.LastAccessTimeString : "",
                valueIsLtr: true,
                rowVisibility: static f => f.IsCreationTimeResolved && !f.LastAccessTime.HasValue
                    ? Visibility.Hidden
                    : Visibility.Visible).Init());

        if (!Data.FileActions.IsRecycleBin
            && !ArchivePath.IsArchivePath(file.FullPath, Data.DevicesObject?.Current?.ID))
        {
            SelectionInfoItems.Add(new ItemDetailsViewModel<FileClass>(
                file,
                Strings.Resources.S_CREATION_TIME,
                f => f.CreationTime.HasValue ? f.FolderViewModel.CreationTimeString : "",
                valueIsLtr: true,
                rowVisibility: static f => f.IsCreationTimeResolved && !f.CreationTime.HasValue
                    ? Visibility.Hidden
                    : Visibility.Visible).Init());
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

        var deviceId = Data.DevicesObject?.Current?.ID;
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
        }

        SubscribePermissionDevice();
        UpdateCanEditPermissions();
    }

    private static string FormatArchiveLocation(FileClass file)
        => ArchivePath.IsArchivePath(file.ParentPath, Data.DevicesObject?.Current?.ID)
            ? ArchivePath.FormatDetailsLocation(file.ParentPath, Data.DevicesObject?.Current?.ID)
            : file.ParentPath;

    private static string? TryGetArchiveSummaryPath(FileClass file)
    {
        var deviceId = Data.DevicesObject?.Current?.ID;
        if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, deviceId)
            && string.IsNullOrEmpty(internalPath))
        {
            return archivePath;
        }

        if (file.Type is AbstractFile.FileType.File
            && file.SpecialType.HasFlag(AbstractFile.SpecialFileType.Archive)
            && !ArchivePath.IsArchivePath(file.FullPath, deviceId))
        {
            return file.FullPath;
        }

        return null;
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

    private void UpdateIsEditorFocused()
    {
        IsEditorFocused = IsEditingPermissions
            || EditorTextBox.IsKeyboardFocusWithin
            || EditorTextBox.IsContextMenuOpen;
    }

    private void UpdateCanEditPermissions()
    {
        var allowed = DriveHelper.GetEditableUnixChanges(File, Data.DevicesObject.Current);
        CanEditPermissions = allowed.Any;
        if (!CanEditPermissions && IsEditingPermissions)
            IsEditingPermissions = false;
    }

    private void SubscribePermissionDevice()
    {
        var device = Data.DevicesObject.Current;
        if (_permissionDevice == device)
            return;

        UnsubscribePermissionDevice();
        _permissionDevice = device;
        if (device is not null)
            device.PropertyChanged += OnPermissionDeviceChanged;
    }

    private void UnsubscribePermissionDevice()
    {
        if (_permissionDevice is null)
            return;

        _permissionDevice.PropertyChanged -= OnPermissionDeviceChanged;
        _permissionDevice = null;
    }

    private void OnPermissionDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LogicalDeviceViewModel.HasRootShell)
            and not nameof(LogicalDeviceViewModel.Mounts))
            return;

        App.SafeInvoke(UpdateCanEditPermissions);
    }

    private void BeginPermissionsEdit()
    {
        if (File is not { } file)
        {
            IsEditingPermissions = false;
            return;
        }

        var device = Data.DevicesObject.Current;
        var allowed = DriveHelper.GetEditableUnixChanges(file, device);
        if (!allowed.Any)
        {
            IsEditingPermissions = false;
            return;
        }

        PermissionsEdit.BeginEdit(file, allowed);
        LoadKnownIdentities(file, device);
    }

    private void LoadKnownIdentities(FileClass file, LogicalDeviceViewModel? device)
    {
        if (device is null)
        {
            IEnumerable<string> users = file.User is null ? [] : [file.User];
            IEnumerable<string> groups = file.Group is null ? [] : [file.Group];
            PermissionsEdit.SetKnownIdentities(users, groups, file.User, file.Group);
            return;
        }

        device.RecordUnixIdentity(file.User, file.Group);
        PermissionsEdit.SetKnownIdentities(
            device.GetKnownUsersOrdered(file.User),
            device.GetKnownGroupsOrdered(file.Group),
            file.User,
            file.Group);

        _ = Task.Run(() =>
        {
            device.EnsureKnownIdentities();
            App.SafeInvoke(() =>
            {
                if (!IsEditingPermissions || !ReferenceEquals(File, file))
                    return;

                PermissionsEdit.SetKnownIdentities(
                    device.GetKnownUsersOrdered(file.User),
                    device.GetKnownGroupsOrdered(file.Group),
                    PermissionsEdit.SelectedUser ?? file.User,
                    PermissionsEdit.SelectedGroup ?? file.Group);
            });
        });
    }

    private async Task SavePermissionsAsync()
    {
        if (!IsEditingPermissions || File is not { } file)
            return;

        var device = Data.DevicesObject.Current;
        if (device is null)
            return;

        var token = _cancellationToken?.Token ?? default;
        var error = await PermissionsEdit.ApplyAsync(file, device.ID, token);

        IsEditingPermissions = false;
        await file.UpdateExtraInfoAsync(token);
        UpdateCanEditPermissions();

        if (!string.IsNullOrEmpty(error))
        {
            DialogService.ShowMessage(
                error,
                Strings.Resources.S_FILE_PERMISSIONS,
                DialogService.DialogIcon.Critical,
                copyToClipboard: true,
                error: DialogError.ChangePermissionsFailed);
        }
    }
}
