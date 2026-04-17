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

        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off
            || Data.DevicesObject.Current is null)
            return;

        var logicalDeviceId = Data.DevicesObject.Current.LogicalID;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            if (!ThumbnailService.IsInitialized(logicalDeviceId))
                ThumbnailService.ForceLoad(Data.DevicesObject.Current);

            if (token.IsCancellationRequested)
                return;

            var thumbnail = ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, _file.FullPath, size);

            if (thumbnail is null)
                ThumbnailService.TryPullCustomThumbnail(Data.DevicesObject.Current, _file);

            if (token.IsCancellationRequested)
                return;

            App.SafeBeginInvoke(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                if (thumbnail is ThumbnailService.Thumbnail thumb)
                {
                    _cachedThumbnail = thumb.Image;
                    _cachedSize = size;
                    OnPropertyChanged(nameof(LargeIcon));
                    UpdateVideoOverlay(thumb);
                    UpdateTooltip(thumb);
                }
                else
                {
                    _currentlyLoadingSize = _cachedSize;
                    UpdateTooltipNoThumbnail();
                }
            }, DispatcherPriority.Background);
        }, token);

        _thumbnailUpdatedHandler = (updatedDeviceId, updatedFilePath) =>
        {
            if (updatedDeviceId != logicalDeviceId || updatedFilePath != _file.FullPath)
                return;

            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                var thumbnail = ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, _file.FullPath, size);
                if (thumbnail is not ThumbnailService.Thumbnail thumb)
                    return;

                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    _cachedThumbnail = thumb.Image;
                    _cachedSize = size;
                    OnPropertyChanged(nameof(LargeIcon));
                    UpdateVideoOverlay(thumb);
                    UpdateTooltip(thumb);
                }, DispatcherPriority.Background);
            }, token);
        };

        ThumbnailService.ThumbnailUpdated += _thumbnailUpdatedHandler;
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

            if (thumb.Info.Duration is TimeSpan duration)
                result.Add($"{Strings.Resources.S_VIDEO_DURATION}: {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
        }
        else if (thumb.Info.Type is ThumbnailService.MediaType.images)
        {
            if (thumb.Info.Resolution is Size res)
                result.Add($"{Strings.Resources.S_PICTURE_DIMENSIONS}: {$"{res.Width} \u00D7 {res.Height}"}");

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

                // explicit hexcode to avoid saving file as unicode
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
