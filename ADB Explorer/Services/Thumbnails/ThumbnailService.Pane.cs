namespace ADB_Explorer.Services;

public static partial class ThumbnailService
{
    private sealed class PaneThumbnailState
    {
        public Thumbnail? Cache;
        public ThumbnailSize LoadedSize;
        public CancellationTokenSource? Cts;
        public Action<string, string>? UpdatedHandler;
    }

    private static readonly ConditionalWeakTable<FileClass, PaneThumbnailState> PaneThumbnails = new();

    public static bool IsPhotoPaneThumbnailCandidate(FileClass file) =>
        Data.Settings.ThumbsMode is not AppSettings.ThumbnailMode.Off
        && file.Type is AbstractFile.FileType.File
        && !file.IsLink
        && AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.InvariantCultureIgnoreCase);

    public static Thumbnail? GetPaneThumbnail(FileClass file) =>
        Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off
            ? null
            : PaneThumbnails.TryGetValue(file, out var state) ? state.Cache : null;

    /// <summary>
    /// Loads a thumbnail for the details/preview pane. Unlike icon-view / OnPhotoDir folder preloading,
    /// selection always forces acquisition when <see cref="AppSettings.ThumbsMode"/> is not Off
    /// (including <see cref="AppSettings.ThumbnailMode.IconViewOnly"/> and <see cref="AppSettings.ThumbnailMode.OnPhotoDir"/>).
    /// </summary>
    public static void BeginPaneThumbnailLoad(FileClass file, ThumbnailSize size, bool scaleWithDpi = true)
    {
        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off)
            return;

        if (Data.ActiveDevice is not { } device)
            return;

        var useCustomThumbs = Data.Settings.MaxCustomThumbWeight > 0;

        var state = PaneThumbnails.GetValue(file, static _ => new PaneThumbnailState());
        if (state.Cache?.Image is not null && state.LoadedSize >= size)
            return;

        if (state.Cts is not null)
            return;

        // Immediate path: CSV + local files, so details/preview does not wait for icon view or photo-dir ForceLoad.
        EnsureCsvCacheLoaded(device);
        if (LoadThumbnail(device, file, size, scaleWithDpi) is Thumbnail { Image: not null } immediate)
        {
            ApplyPaneThumbnail(file, immediate, size);
            return;
        }

        var serialNumber = device.SerialNumber;
        var path = file.ParsedFullPath;

        state.Cts = new CancellationTokenSource();
        var token = state.Cts.Token;

        state.UpdatedHandler = (updatedDeviceId, updatedFilePath) =>
        {
            if (updatedDeviceId != serialNumber
                || (updatedFilePath != path && updatedFilePath != file.FullPath))
                return;

            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                if (LoadThumbnail(device, file, size, scaleWithDpi) is not Thumbnail loaded
                    || loaded.Image is null)
                    return;

                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        ApplyPaneThumbnail(file, loaded, size);
                        ClearPaneLoadToken(file, token);
                        return;
                    }

                    CompletePaneThumbnailLoad(file, loaded, size);
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }, token);
        };

        ThumbnailUpdated += state.UpdatedHandler;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            // Pane selection forces acquisition for IconViewOnly / OnPhotoDir even if folder preload never ran.
            EnsureThumbnailsReady(device);

            if (token.IsCancellationRequested)
                return;

            var thumbnail = LoadThumbnail(device, file, size, scaleWithDpi);

            if ((thumbnail is null || thumbnail.Value.Image is null)
                && useCustomThumbs
                && IsCustomThumbnailCandidate(file))
            {
                TryPullCustomThumbnail(device, file);
                thumbnail = LoadThumbnail(device, file, size, scaleWithDpi);
            }

            if (token.IsCancellationRequested)
            {
                if (thumbnail is Thumbnail { Image: not null } cached)
                {
                    App.SafeBeginInvoke(() =>
                    {
                        ApplyPaneThumbnail(file, cached, size);
                        ClearPaneLoadToken(file, token);
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }
                else
                {
                    ClearPaneLoadToken(file, token);
                }

                return;
            }

            if (thumbnail is Thumbnail { Image: not null } loaded)
            {
                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        ApplyPaneThumbnail(file, loaded, size);
                        ClearPaneLoadToken(file, token);
                        return;
                    }

                    CompletePaneThumbnailLoad(file, loaded, size);
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                var waitingForPull = IsPendingCustomPull($"{serialNumber}|{path}")
                    || (!string.Equals(file.FullPath, path, StringComparison.Ordinal)
                        && IsPendingCustomPull($"{serialNumber}|{file.FullPath}"));

                if (!waitingForPull)
                {
                    ClearPaneLoadToken(file, token);
                    App.SafeBeginInvoke(() => file.NotifyPaneThumbnailChanged());
                }
                // else: waiting for async custom pull via ThumbnailUpdated; keep CTS/handler
            }
        }, token);
    }

    public static bool IsPaneThumbnailLoading(FileClass file) =>
        PaneThumbnails.TryGetValue(file, out var state) && state.Cts is not null;

    /// <summary>
    /// Ends an in-flight pane load with no image and notifies listeners (preview can show "unavailable").
    /// </summary>
    private static void NotifyPaneThumbnailUnavailable(FileClass file)
    {
        App.SafeBeginInvoke(() =>
        {
            CancelPaneThumbnailLoad(file);
            file.NotifyPaneThumbnailChanged();
        });
    }

    /// <summary>Loads <c>thumbnailInfo.csv</c> into memory without querying the device media DB (safe on UI thread).</summary>
    private static void EnsureCsvCacheLoaded(LogicalDeviceViewModel device)
    {
        if (GetDeviceThumbsInfo(device.SerialNumber) is null)
            return;

        var deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (deviceInfo.ThumbnailPathCache is not null)
            return;

        var cache = GetThumbsCacheFromCsv(deviceInfo);
        if (cache.Count == 0)
            return;

        deviceInfo.ThumbnailPathCache = cache;
        if (deviceInfo.HasThumbnailSupport)
        {
            deviceInfo = SetDeviceLocalDir(deviceInfo);
            Task.Run(() => MergeDeviceWithLocalCache(deviceInfo));
        }

        UpdateCache(deviceInfo);
    }

    /// <summary>Ensures device thumbnail metadata is available, forcing load if pane selection needs it before folder preload.</summary>
    private static void EnsureThumbnailsReady(LogicalDeviceViewModel device)
    {
        var info = GetCachedDeviceInfo(device.SerialNumber);
        if (!string.IsNullOrEmpty(info.DeviceId) && info.ThumbnailPathCache is not null)
            return;

        ForceLoad(device);
    }

    private static void ClearPaneLoadToken(FileClass file, CancellationToken token)
    {
        if (!PaneThumbnails.TryGetValue(file, out var state))
            return;

        if (state.Cts is null || state.Cts.Token != token)
            return;

        if (state.UpdatedHandler is not null)
        {
            ThumbnailUpdated -= state.UpdatedHandler;
            state.UpdatedHandler = null;
        }

        state.Cts.Dispose();
        state.Cts = null;
    }

    public static void CancelPaneThumbnailLoad(FileClass file)
    {
        if (!PaneThumbnails.TryGetValue(file, out var state))
            return;

        if (state.UpdatedHandler is not null)
        {
            ThumbnailUpdated -= state.UpdatedHandler;
            state.UpdatedHandler = null;
        }

        state.Cts?.Cancel();
        state.Cts?.Dispose();
        state.Cts = null;
    }

    public static void ApplyPaneThumbnail(FileClass file, Thumbnail thumb, ThumbnailSize size)
    {
        var state = PaneThumbnails.GetValue(file, static _ => new PaneThumbnailState());
        state.Cache = thumb;
        state.LoadedSize = size;
        file.NotifyPaneThumbnailChanged();
    }

    private static void CompletePaneThumbnailLoad(FileClass file, Thumbnail thumb, ThumbnailSize size)
    {
        if (!PaneThumbnails.TryGetValue(file, out var state))
            return;

        if (state.UpdatedHandler is not null)
        {
            ThumbnailUpdated -= state.UpdatedHandler;
            state.UpdatedHandler = null;
        }

        state.Cts?.Dispose();
        state.Cts = null;
        state.Cache = thumb;
        state.LoadedSize = size;
        file.NotifyPaneThumbnailChanged();
    }
}
