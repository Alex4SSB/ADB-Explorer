using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public partial class FileIconViewModel : FileViewModelBase
{
    private CancellationTokenSource? _cts;
    private Action<string, string>? _thumbnailUpdatedHandler;

    private BitmapSource? _cachedThumbnail;
    private ThumbnailService.ThumbnailSize _cachedSize;
    private ThumbnailService.ThumbnailSize _currentlyLoadingSize;

    private BitmapSource LargeFileIcon => FileToIconConverter.GetImage(_file, (int)((int)Data.RuntimeSettings.ThumbsSize / Data.RuntimeSettings.MainWindowScalingFactor)).First();
    public BitmapSource? LargeIcon
    {
        get
        {
            if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off)
                return LargeFileIcon;

            var size = Data.RuntimeSettings.ThumbsSize;
            if (_cachedThumbnail is not null && _cachedSize >= size)
                return _cachedThumbnail;

            if (_currentlyLoadingSize < size)
                BeginLoadThumbnail(size);

            return _cachedThumbnail ?? LargeFileIcon;
        }
    }

    [ObservableProperty]
    public partial BitmapSource? LargeIconOverlay { get; set; }

    [ObservableProperty]
    public partial BitmapSource? VideoIconOverlay { get; set; }

    [ObservableProperty]
    public partial string? IconViewTooltip { get; set; }

    public FileIconViewModel(FileClass file) : base(file)
    {
        UpdateOverlays();
    }

    private void UpdateOverlays()
    {
        if (_file.SpecialType.HasFlag(AbstractFile.SpecialFileType.LinkOverlay))
        {
            var icons = FileToIconConverter.GetImage(_file, 64);
            LargeIconOverlay = icons.Skip(1).FirstOrDefault();
        }
        else
        {
            LargeIconOverlay = null;
        }
    }

    private void BeginLoadThumbnail(ThumbnailService.ThumbnailSize size)
    {
        CancelLoading();
        _currentlyLoadingSize = size;

        if (Data.DevicesObject.Current is null)
            return;

        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off)
            return;

        var useCustomThumbs = Data.Settings.MaxCustomThumbWeight > 0;

        var serialNumber = Data.DevicesObject.Current.SerialNumber;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _thumbnailUpdatedHandler = (updatedDeviceId, updatedFilePath) =>
        {
            if (updatedDeviceId != serialNumber
                || (updatedFilePath != _file.ParsedFullPath && updatedFilePath != _file.FullPath))
                return;

            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                if (ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, _file, size)
                    is not ThumbnailService.Thumbnail { Image: not null } thumb)
                {
                    App.SafeBeginInvoke(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        _currentlyLoadingSize = _cachedSize;
                    }, DispatcherPriority.Background);

                    return;
                }

                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    ApplyLoadedThumbnail(thumb, size);
                }, DispatcherPriority.Background);
            }, token);
        };

        ThumbnailService.ThumbnailUpdated += _thumbnailUpdatedHandler;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            if (!ThumbnailService.IsInitialized(serialNumber))
                ThumbnailService.ForceLoad(Data.DevicesObject.Current);

            if (token.IsCancellationRequested)
                return;

            var thumbnail = ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, _file, size);

            var requestedCustom = useCustomThumbs && ThumbnailService.IsCustomThumbnailCandidate(_file);
            if ((thumbnail is null || thumbnail.Value.Image is null) && requestedCustom)
            {
                ThumbnailService.TryPullCustomThumbnail(Data.DevicesObject.Current, _file);
                thumbnail = ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, _file, size);
            }

            if (token.IsCancellationRequested)
                return;

            if (thumbnail is ThumbnailService.Thumbnail { Image: not null } thumb)
            {
                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    ApplyLoadedThumbnail(thumb, size);
                }, DispatcherPriority.Background);
            }
            else if (!requestedCustom)
            {
                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    CancelLoading();
                    UpdateTooltipNoThumbnail();
                }, DispatcherPriority.Background);
            }
            else
            {
                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    _currentlyLoadingSize = _cachedSize;
                    UpdateTooltipNoThumbnail();
                }, DispatcherPriority.Background);
            }
        }, token);
    }

    private void ApplyLoadedThumbnail(ThumbnailService.Thumbnail thumb, ThumbnailService.ThumbnailSize size)
    {
        if (_thumbnailUpdatedHandler is not null)
        {
            ThumbnailService.ThumbnailUpdated -= _thumbnailUpdatedHandler;
            _thumbnailUpdatedHandler = null;
        }

        _cts?.Dispose();
        _cts = null;

        _cachedThumbnail = thumb.Image;
        _cachedSize = size;
        _currentlyLoadingSize = size;
        OnPropertyChanged(nameof(LargeIcon));
        UpdateVideoOverlay(thumb);
        UpdateTooltip(thumb);
        ThumbnailService.ApplyPaneThumbnail(_file, thumb);
    }

    private void UpdateVideoOverlay(ThumbnailService.Thumbnail thumb)
    {
        if (thumb.Info.Type is ThumbnailService.MediaType.video)
        {
            VideoIconOverlay = FileToIconConverter.GetImage(_file, 32).FirstOrDefault();
        }
    }

    private void UpdateTooltip(ThumbnailService.Thumbnail thumb)
    {
        List<string> result = [];

        result.Add(_file.DisplayName);

        result.Add(IconViewTypeString());

        if (thumb.Info.Type is ThumbnailService.MediaType.video)
        {
            result.Add($"{Strings.Resources.S_COLUMN_SIZE}: {SizeString}");
            if (thumb.Info.Duration is not null)
                result.Add($"{Strings.Resources.S_VIDEO_DURATION}: {thumb.Info.DurationString}");
        }
        else if (thumb.Info.Type is ThumbnailService.MediaType.images)
        {
            if (thumb.Info.Resolution is not null)
                result.Add($"{Strings.Resources.S_PICTURE_DIMENSIONS}: {thumb.Info.ResolutionString}");

            result.Add($"{Strings.Resources.S_COLUMN_SIZE}: {SizeString}");
        }

        IconViewTooltip = result.Count == 0 ? null : string.Join('\n', result);
    }

    private void UpdateTooltipNoThumbnail()
    {
        List<string> result = [];

        result.Add(_file.DisplayName);

        if (_file.IsLink)
        {
            if (!string.IsNullOrEmpty(_file.LinkTarget))
            {
                var type = _file.Type is AbstractFile.FileType.BrokenLink
                    ? Strings.Resources.S_FILE_BROKEN_LINK
                    : Strings.Resources.S_FILE_TYPE_LINK;

                result.Add($"{type} \u2192 {_file.LinkTarget}");
            }
        }
        else if (_file.Type is AbstractFile.FileType.Folder)
        {
            if (_file.ModifiedTime is not null)
                result.Add($"{Strings.Resources.S_COLUMN_DATE_MODIFIED}: {ModifiedTimeString}");
        }
        else
        {
            if (_file.Type is not AbstractFile.FileType.Unknown)
            {
                result.Add(IconViewTypeString());
            }

            if (_file.Size is not null)
                result.Add($"{Strings.Resources.S_COLUMN_SIZE}: {SizeString}");

            if (_file.ModifiedTime is not null)
                result.Add($"{Strings.Resources.S_COLUMN_DATE_MODIFIED}: {ModifiedTimeString}");
        }

        IconViewTooltip = result.Count == 0 ? null : string.Join('\n', result);
    }

    private string IconViewTypeString()
    {
        return Data.RuntimeSettings.IsRTL && !TypeIsRtl
            ? $"{TextHelper.LTR_MARK}{Strings.Resources.S_COLUMN_TYPE}: {TextHelper.RTL_MARK}{TypeName}{TextHelper.LTR_MARK}"
            : $"{Strings.Resources.S_COLUMN_TYPE}: {TypeName}";
    }

    public override void UpdateType()
    {
        base.UpdateType();
        UpdateOverlays();
        OnPropertyChanged(nameof(LargeIcon));
    }

    public new void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (_cachedThumbnail is null
            && _cts is null
            && ThumbnailService.IsCustomThumbnailCandidate(_file))
        {
            BeginLoadThumbnail(Data.RuntimeSettings.ThumbsSize);
        }
    }

    public void InvalidateThumbnail()
    {
        _currentlyLoadingSize = _cachedSize;
        OnPropertyChanged(nameof(LargeIcon));
    }

    public void CancelLoading()
    {
        if (_thumbnailUpdatedHandler is not null)
        {
            ThumbnailService.ThumbnailUpdated -= _thumbnailUpdatedHandler;
            _thumbnailUpdatedHandler = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _currentlyLoadingSize = _cachedSize;
    }

    public override void Dispose()
    {
        CancelLoading();

        _cachedThumbnail = null;
        LargeIconOverlay = null;
        VideoIconOverlay = null;
        IconViewTooltip = null;

        base.Dispose();
    }
}
