using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public partial class FileIconViewModel : ObservableObject
{
    private readonly FileClass _file;
    private CancellationTokenSource? _cts;
    private Action<string, string>? _thumbnailUpdatedHandler;

    private bool _isLoaded;

    private BitmapSource? _largeIcon;
    public BitmapSource? LargeIcon
    {
        get
        {
            if (!_isLoaded)
                BeginLoadThumbnail();

            return _largeIcon;
        }
        private set => SetProperty(ref _largeIcon, value);
    }

    [ObservableProperty]
    private BitmapSource? _largeIconOverlay;

    [ObservableProperty]
    private BitmapSource? _videoIconOverlay;

    [ObservableProperty]
    private string? _iconViewTooltip;

    public FileIconViewModel(FileClass file)
    {
        _file = file;
        _largeIcon = GetLargeFileIcon();
        UpdateOverlays();
    }

    private BitmapSource GetLargeFileIcon()
    {
        return FileToIconConverter.GetImage(_file, 120).First();
    }

    private void UpdateOverlays()
    {
        if (_file.IconOverlay is not null)
        {
            var icons = FileToIconConverter.GetImage(_file, 64);
            if (icons.Count() > 1 && icons.ElementAt(1) is BitmapSource icon2)
                LargeIconOverlay = icon2;
        }
    }

    private void BeginLoadThumbnail()
    {
        _isLoaded = true;

        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off
            || Data.CurrentADBDevice is null)
            return;

        var logicalDeviceId = Data.CurrentADBDevice.Device.LogicalID;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            if (!ThumbnailService.IsInitialized(logicalDeviceId))
                ThumbnailService.ForceLoad(Data.CurrentADBDevice);

            if (token.IsCancellationRequested)
                return;

            var thumbnail = ThumbnailService.LoadThumbnail(Data.CurrentADBDevice, _file.FullPath);

            if (token.IsCancellationRequested)
                return;

            App.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                if (thumbnail is ThumbnailService.Thumbnail thumb)
                {
                    LargeIcon = thumb.Image;
                    UpdateVideoOverlay(thumb);
                    UpdateTooltip(thumb);
                }
                else
                {
                    UpdateTooltipNoThumbnail();
                }
            });
        }, token);

        _thumbnailUpdatedHandler = (updatedDeviceId, updatedFilePath) =>
        {
            if (updatedDeviceId != logicalDeviceId || updatedFilePath != _file.FullPath)
                return;

            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                var thumbnail = ThumbnailService.LoadThumbnail(Data.CurrentADBDevice, _file.FullPath);
                if (thumbnail is not ThumbnailService.Thumbnail thumb)
                    return;

                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    LargeIcon = thumb.Image;
                    UpdateVideoOverlay(thumb);
                    UpdateTooltip(thumb);
                });
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
        string typeRow = Data.RuntimeSettings.IsRTL && !_file.TypeIsRtl
            ? $"{TextHelper.LTR_MARK}{Strings.Resources.S_COLUMN_TYPE}: {TextHelper.RTL_MARK}{_file.TypeName}{TextHelper.LTR_MARK}"
            : $"{Strings.Resources.S_COLUMN_TYPE}: {_file.TypeName}";
        
        result.Add(typeRow);

        if (thumb.Info.Type is ThumbnailService.MediaType.video)
        {
            result.Add($"{Strings.Resources.S_COLUMN_SIZE}: {_file.SizeString}");

            if (thumb.Info.Duration is TimeSpan duration)
                result.Add($"{Strings.Resources.S_VIDEO_DURATION}: {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
        }
        else if (thumb.Info.Type is ThumbnailService.MediaType.images)
        {
            if (thumb.Info.Resolution is Size res)
                result.Add($"{Strings.Resources.S_PICTURE_DIMENSIONS}: {$"{res.Width} × {res.Height}"}");

            result.Add($"{Strings.Resources.S_COLUMN_SIZE}: {_file.SizeString}");
        }

        IconViewTooltip = result.Count == 0 ? null : string.Join('\n', result);
    }

    private void UpdateTooltipNoThumbnail()
    {
        List<string> result = [];
        
        if (_file.IsLink)
        {
            if (!string.IsNullOrEmpty(_file.LinkTarget))
            {
                var type = _file.Type is AbstractFile.FileType.BrokenLink
                    ? Strings.Resources.S_FILE_BROKEN_LINK
                    : Strings.Resources.S_FILE_TYPE_LINK;

                result.Add($"{type} → {_file.LinkTarget}");
            }
        }
        else if (_file.Type is AbstractFile.FileType.Folder)
        {
            if (_file.ModifiedTime is not null)
                result.Add($"{Strings.Resources.S_COLUMN_DATE_MODIFIED}: {_file.ModifiedTimeString}");
        }
        else
        {
            if (_file.Type is not AbstractFile.FileType.Unknown)
            {
                string typeName = _file.TypeName;
                if (Data.RuntimeSettings.IsRTL && !_file.TypeIsRtl)
                    typeName = TextHelper.LTR_MARK + typeName;

                result.Add($"{Strings.Resources.S_COLUMN_TYPE}: {typeName}");
            }

            if (_file.Size is not null)
                result.Add($"{Strings.Resources.S_COLUMN_SIZE}: {_file.SizeString}");

            if (_file.ModifiedTime is not null)
                result.Add($"{Strings.Resources.S_COLUMN_DATE_MODIFIED}: {_file.ModifiedTimeString}");
        }

        IconViewTooltip = result.Count == 0 ? null : string.Join('\n', result);
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
    }
}
