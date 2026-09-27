using ICSharpCode.AvalonEdit.Highlighting;

using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ADB_Explorer.Controls;

public partial class DetailsPane
{
    private LogicalDeviceViewModel? _previewMountDevice;

    private string? _previewFileExtension;

    private byte[]? _previewBytes;

    private bool _updatingSyntaxSelection;

    private void ShowPreview(IEnumerable<IBrowserItem> files)
    {
        NoPreviewTextBlock.Text = files.Any()
            ? Strings.Resources.S_PREVIEW_INVALID
            : Strings.Resources.S_PREVIEW_EMPTY_SELECTION;

        if (Data.FileActions.IsRecycleBin || SingleFile(files) is not { } file)
        {
            NoPreviewTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (ThumbnailService.IsPhotoPaneThumbnailCandidate(file))
        {
            ShowPhotoPreview(file);
            return;
        }

        if (file.Type is not AbstractFile.FileType.File
            || AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
            || file.IsApk
            || ArchiveHelper.GetFamily(file.FullName) is not ArchiveFamily.None
            || file.IsLink
            || !(file.Size / 1000 < Data.Settings.MaxPreviewFileSize))
        {
            NoPreviewTextBlock.Visibility = Visibility.Visible;
            return;
        }

        NoPreviewTextBlock.Visibility = Visibility.Collapsed;

        var device = Data.ActiveDevice;
        SubscribePreviewMounts(device);
        IsEditorReadOnly = FileHelper.IsPreviewTextReadOnly(file, device);
        _previewFileExtension = file.Extension;
        UpdateSyntaxSelectorVisibility();
        ApplyPreviewSyntaxHighlighting();

        var cts = new CancellationTokenSource();
        _cancellationToken = cts;

        if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            IsSyntaxSelectorVisible = false;
            _ = Task.Run(() => LoadPdfPreviewAsync(device, file.FullPath, cts), cts.Token);
        }
        else
            _ = Task.Run(() => LoadTextPreviewAsync(device, file.FullPath, cts), cts.Token);
    }

    private async Task LoadPdfPreviewAsync(LogicalDeviceViewModel? device, string fullPath, CancellationTokenSource cts)
    {
        var stream = await AdbHelper.ReadFileAsStreamAsync(device, fullPath, cts.Token);
        if (stream is null || cts.IsCancellationRequested) return;

        var memStream = new MemoryStream();
        await stream.CopyToAsync(memStream, cts.Token);
        if (cts.IsCancellationRequested) return;

        App.SafeInvoke(() => _pdfMemoryStream = memStream);

        await RenderPdfAsync(this, memStream, password: null, cts);
    }

    private async Task LoadTextPreviewAsync(LogicalDeviceViewModel? device, string fullPath, CancellationTokenSource cts)
    {
        var stream = await AdbHelper.ReadFileAsStreamAsync(device, fullPath, cts.Token);
        if (stream is null || cts.IsCancellationRequested) return;

        var bytes = stream.ToArray();
        if (cts.IsCancellationRequested) return;

        App.SafeInvoke(() =>
        {
            // A NUL byte means binary content (e.g. raw PCM in a .wav). AvalonEdit chokes on one huge
            // single-line "document" regardless of word-wrap or hex/text mode, so it isn't previewed.
            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                NoPreviewTextBlock.Text = Strings.Resources.S_PREVIEW_INVALID;
                NoPreviewTextBlock.Visibility = Visibility.Visible;
                return;
            }

            _previewBytes = bytes;
            ApplyPreviewContent();
        });
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

    private MemoryStream? _pdfMemoryStream;

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
        PhotoPreviewImage.Visibility = Visibility.Collapsed;
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
        PhotoPreviewImage.Visibility = Visibility.Visible;
        PhotoPreviewImage.Source = image;
    }

    private void ClearPhotoPreview()
    {
        PhotoPreviewImage.Source = null;
        PhotoPreviewImage.Visibility = Visibility.Collapsed;
    }

    private void ApplyPreviewContent()
    {
        if (_previewBytes is null)
            return;

        if (SelectedSyntax?.IsHex is true)
            EditorText = HexText.Format(_previewBytes);
        else
            EditorText = FileHelper.DecodeText(_previewBytes);
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

        IsEditorReadOnly = FileHelper.IsPreviewTextReadOnly(file, Data.ActiveDevice);
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
}
