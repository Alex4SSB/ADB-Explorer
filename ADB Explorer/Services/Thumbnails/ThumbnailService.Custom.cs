namespace ADB_Explorer.Services;

public static partial class ThumbnailService
{
    private const string CUSTOM_PHOTOS_SUBFOLDER = "CustomThumbs";

    /// <summary>
    /// Downscales <paramref name="source"/> so neither dimension exceeds <paramref name="maxDimension"/>,
    /// preserving aspect ratio. Returns the (frozen) source unchanged if it already fits.
    /// </summary>
    public static BitmapSource CreateScaledPreview(BitmapSource source, int maxDimension)
    {
        var largestSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (largestSide <= maxDimension)
        {
            if (source.CanFreeze && !source.IsFrozen)
                source.Freeze();

            return source;
        }

        var scale = (double)maxDimension / largestSide;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();

        return scaled;
    }

    /// <summary>
    /// Registers <paramref name="image"/> as the custom thumbnail for a just-created file, without
    /// pulling it from the device (the file was just pushed from this same image).
    /// </summary>
    public static void SeedCustomThumbnail(LogicalDeviceViewModel device, FileClass file, BitmapSource image)
    {
        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off || Data.Settings.MaxCustomThumbWeight <= 0)
            return;

        var deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        var targetDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CUSTOM_PHOTOS_SUBFOLDER);
        Directory.CreateDirectory(targetDir);

        string thumbId;
        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
        {
            deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
            if (deviceInfo.ThumbnailPathCache is null)
                return;

            thumbId = NewCustomThumbId(file, useOriginalExtension: true);
            RegisterCustomThumbCacheEntry(ref deviceInfo, file, thumbId);
            UpdateCache(deviceInfo);
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);
        }

        var localFile = Path.Combine(targetDir, thumbId);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using (var fs = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.Read))
            encoder.Save(fs);

        var resolution = new Size(image.PixelWidth, image.PixelHeight);
        var fileSize = new FileInfo(localFile).Length;

        MarkCustomThumbnailReady(device.SerialNumber, file, thumbId, resolution, fileSize);
    }

    /// <summary>Blocks a concurrent <see cref="TryPullCustomThumbnail"/> from racing an in-flight push.</summary>
    public static bool TryReserveCustomThumbnailSeed(string serialNumber, FileClass file)
    {
        var pullKey = $"{serialNumber}|{file.ParsedFullPath}";

        lock (PendingCustomPullsLock)
            return PendingCustomPulls.Add(pullKey);
    }

    public static void ReleaseCustomThumbnailSeed(string serialNumber, FileClass file)
    {
        var pullKey = $"{serialNumber}|{file.ParsedFullPath}";

        lock (PendingCustomPullsLock)
            PendingCustomPulls.Remove(pullKey);
    }

    public static bool IsCustomThumbnailCandidate(FileClass file) =>
        Data.Settings.ThumbsMode is not AppSettings.ThumbnailMode.Off
        && file.Type is AbstractFile.FileType.File
        && Data.Settings.MaxCustomThumbWeight > 0
        && file.Size is long size
        && size <= (long)Data.Settings.MaxCustomThumbWeight * 1024
        && AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.InvariantCultureIgnoreCase);

    private static readonly HashSet<string> PendingCustomPulls = [];

    private static readonly Lock PendingCustomPullsLock = new();

    private static readonly Lock CustomPullScheduleLock = new();

    private static readonly Queue<(string PullKey, Action StartPull)> CustomPullQueue = new();

    private static int _customPullsInFlight;

    /// <summary>
    /// Called when a thumbnail was requested but not found locally. Handles two cases:
    /// <list type="bullet">
    /// <item>The file is known in the device media DB (<see cref="ThumbnailInfo.LastUpdate"/> == <see cref="DateTime.MinValue"/>) but its local file is absent — pulls the original photo into the cached folder under its DB id.</item>
    /// <item>The file is not in the cache at all and qualifies for custom-weight pulling — pulls it into <see cref="CUSTOM_PHOTOS_SUBFOLDER"/> with a timestamp-based name.</item>
    /// </list>
    /// Fires <see cref="ThumbnailUpdated"/> upon completion so the caller can reload.
    /// </summary>
    public static void TryPullCustomThumbnail(LogicalDeviceViewModel device, FileClass file)
    {
        if (!IsCustomThumbnailCandidate(file))
            return;

        if (GetDeviceThumbsInfo(device.SerialNumber) is not DeviceThumbnailInfo deviceInfo)
            return;

        UpdateDeviceCache(deviceInfo);

        deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        var filePath = file.ParsedFullPath;
        var pullKey = $"{device.SerialNumber}|{filePath}";

        lock (PendingCustomPullsLock)
        {
            if (!PendingCustomPulls.Add(pullKey))
                return;
        }

        // Archive members: extract on device → sync-pull into CustomThumbs (no shell-stdout transfer).
        if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, device.ID)
            && !string.IsNullOrEmpty(internalPath))
        {
            QueueArchiveMemberThumbnailPull(pullKey, device, file, archivePath, internalPath, deviceInfo);
            return;
        }

        if (deviceInfo.ThumbnailPathCache.TryGetValue(filePath, out var cachedInfo)
            || (file.FullPath != filePath && deviceInfo.ThumbnailPathCache.TryGetValue(file.FullPath, out cachedInfo)))
        {
            var localDir = string.IsNullOrEmpty(cachedInfo.LocalFolder)
                ? GetLocalThumbPath(device)
                : Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, cachedInfo.LocalFolder);

            if (localDir is null)
            {
                CancelCustomPull(pullKey);
                return;
            }

            var localFile = Path.Combine(localDir, cachedInfo.Id);
            if (File.Exists(localFile))
            {
                CancelCustomPull(pullKey);
                var resolution = cachedInfo.Resolution ?? ReadImageResolution(localFile);
                long? fileSize = new FileInfo(localFile).Length;
                MarkCustomThumbnailReady(device.SerialNumber, file, cachedInfo.Id, resolution, fileSize);
                return;
            }

            QueueSyncPullToLocal(pullKey, file, localFile, device, cachedInfo.Id, cachedInfo.Resolution);
            return;
        }

        deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId))
        {
            CancelCustomPull(pullKey);
            return;
        }

        string newId;
        var targetDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CUSTOM_PHOTOS_SUBFOLDER);
        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
        {
            deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
            if (deviceInfo.ThumbnailPathCache is null)
            {
                CancelCustomPull(pullKey);
                return;
            }

            if (deviceInfo.ThumbnailPathCache.ContainsKey(filePath))
            {
                CancelCustomPull(pullKey);
                return;
            }

            Directory.CreateDirectory(targetDir);
            newId = NewCustomThumbId(file, useOriginalExtension: true);

            RegisterCustomThumbCacheEntry(ref deviceInfo, file, newId);
            UpdateCache(deviceInfo);
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);
        }

        QueueSyncPullToLocal(pullKey, file, Path.Combine(targetDir, newId), device, newId, knownResolution: null);
    }

    /// <summary>
    /// Extract archive member to device temp, sync-pull into <see cref="CUSTOM_PHOTOS_SUBFOLDER"/>, register cache, notify.
    /// </summary>
    private static void QueueArchiveMemberThumbnailPull(
        string pullKey,
        LogicalDeviceViewModel device,
        FileClass file,
        string archivePath,
        string internalPath,
        DeviceThumbnailInfo deviceInfo)
    {
        string thumbId;
        var targetDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CUSTOM_PHOTOS_SUBFOLDER);

        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
        {
            deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
            if (deviceInfo.ThumbnailPathCache is null)
            {
                CancelCustomPull(pullKey);
                return;
            }

            if (deviceInfo.ThumbnailPathCache.TryGetValue(file.FullPath, out var existing)
                || deviceInfo.ThumbnailPathCache.TryGetValue(file.ParsedFullPath, out existing))
            {
                thumbId = existing.Id;
                var existingLocal = Path.Combine(targetDir, thumbId);
                if (File.Exists(existingLocal))
                {
                    CancelCustomPull(pullKey);
                    MarkCustomThumbnailReady(
                        device.SerialNumber,
                        file,
                        thumbId,
                        existing.Resolution ?? ReadImageResolution(existingLocal),
                        new FileInfo(existingLocal).Length);

                    return;
                }
            }
            else
            {
                thumbId = NewCustomThumbId(file, useOriginalExtension: true);
                RegisterCustomThumbCacheEntry(ref deviceInfo, file, thumbId);
                UpdateCache(deviceInfo);
                WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);
            }
        }

        var localFile = Path.Combine(targetDir, thumbId);
        QueueCustomPull(pullKey, () =>
        {
            string? stagingRoot = null;
            var succeeded = false;
            try
            {
                Directory.CreateDirectory(targetDir);

                var (root, extractedPath, _) = ArchiveExtract.ExtractSelectionForPull(
                    device.ID,
                    archivePath,
                    internalPath,
                    isDirectory: false,
                    CancellationToken.None);
                stagingRoot = root;

                using (var service = new AdvancedSharpAdbClient.SyncService(device.DeviceData))
                using (var fs = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    service.Pull(extractedPath, fs);
                    SyncTransferTracker.AddPullBytes(fs.Length);
                }

                if (!File.Exists(localFile) || new FileInfo(localFile).Length == 0)
                    return;

                MarkCustomThumbnailReady(
                    device.SerialNumber,
                    file,
                    thumbId,
                    ReadImageResolution(localFile),
                    new FileInfo(localFile).Length);

                succeeded = true;
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Archive custom thumbnail pull failed: {e.Message}");
#endif
                try { if (File.Exists(localFile)) File.Delete(localFile); } catch { /* ignore */ }
            }
            finally
            {
                if (stagingRoot is not null)
                    ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);

                CompleteCustomPull(pullKey);
                if (!succeeded)
                    NotifyPaneThumbnailUnavailable(file);
            }
        });
    }

    private static string NewCustomThumbId(FileClass file, bool useOriginalExtension)
    {
        var noExt = file.NoExtName;
        var prefix = noExt[^Math.Min(5, noExt.Length)..];
        var ext = useOriginalExtension && !string.IsNullOrEmpty(file.Extension)
            ? file.Extension
            : ".jpg";

        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{prefix}{ext}";
    }

    private static void RegisterCustomThumbCacheEntry(ref DeviceThumbnailInfo deviceInfo, FileClass file, string thumbId)
    {
        var entry = new ThumbnailInfo(thumbId, MediaType.images, null, null, DateTime.MinValue, CUSTOM_PHOTOS_SUBFOLDER);
        deviceInfo.ThumbnailPathCache[file.FullPath] = entry;
        if (!string.Equals(file.ParsedFullPath, file.FullPath, StringComparison.Ordinal))
            deviceInfo.ThumbnailPathCache[file.ParsedFullPath] = entry;
    }

    /// <summary>
    /// Writes resolution/size into the cache for both FullPath and ParsedFullPath, then fires <see cref="ThumbnailUpdated"/>.
    /// </summary>
    private static void MarkCustomThumbnailReady(
        string serialNumber,
        FileClass file,
        string thumbId,
        Size? resolution,
        long? fileSize)
    {
        var deviceInfo = GetCachedDeviceInfo(serialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        // Update \ Insert
        void upsert(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (deviceInfo.ThumbnailPathCache.TryGetValue(key, out var existing) && existing.Id == thumbId)
            {
                deviceInfo.ThumbnailPathCache[key] = existing with
                {
                    LastUpdate = DateTime.Now,
                    Resolution = resolution ?? existing.Resolution,
                    FileSize = fileSize ?? existing.FileSize,
                };
            }
            else
            {
                deviceInfo.ThumbnailPathCache[key] = new ThumbnailInfo(
                    thumbId,
                    MediaType.images,
                    resolution,
                    null,
                    DateTime.Now,
                    CUSTOM_PHOTOS_SUBFOLDER,
                    FileSize: fileSize);
            }
        }

        upsert(file.FullPath);
        if (!string.Equals(file.ParsedFullPath, file.FullPath, StringComparison.Ordinal))
            upsert(file.ParsedFullPath);

        UpdateCache(deviceInfo);
        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);

        ThumbnailUpdated?.Invoke(serialNumber, file.FullPath);
        if (!string.Equals(file.ParsedFullPath, file.FullPath, StringComparison.Ordinal))
            ThumbnailUpdated?.Invoke(serialNumber, file.ParsedFullPath);
    }

    private static void QueueSyncPullToLocal(
        string pullKey,
        FileClass file,
        string localFile,
        LogicalDeviceViewModel device,
        string thumbId,
        Size? knownResolution)
    {
        var source = new SyncFile(file);
        var parent = Path.GetDirectoryName(localFile) ?? localFile;
        var target = SyncFile.MergeToWindowsPath(source, parent);
        target.UpdatePath(localFile);

        QueueCustomPull(pullKey, () =>
        {
            var op = FileSyncOperation.PullFile(source, target, device, App.AppDispatcher);
            op.MaxThreads = 1;

            op.WhenFinished(status =>
            {
                if (status is FileOperation.OperationStatus.Completed)
                {
                    var resolution = knownResolution ?? ReadImageResolution(localFile);
                    long? pulledSize = File.Exists(localFile) ? new FileInfo(localFile).Length : null;
                    MarkCustomThumbnailReady(device.SerialNumber, file, thumbId, resolution, pulledSize);
                    CompleteCustomPull(pullKey);
                }
                else
                {
                    CompleteCustomPull(pullKey);
                    NotifyPaneThumbnailUnavailable(file);
                }
            });

            op.Start();
        });
    }

    private static void QueueCustomPull(string pullKey, Action startPull)
    {
        lock (CustomPullScheduleLock)
        {
            CustomPullQueue.Enqueue((pullKey, startPull));
            TryStartNextCustomPull();
        }
    }

    private static void TryStartNextCustomPull()
    {
        var max = Data.Settings.ThumbAndIconConcurrency;
        while (_customPullsInFlight < max && CustomPullQueue.Count > 0)
        {
            var (pullKey, startPull) = CustomPullQueue.Dequeue();
            if (!IsPendingCustomPull(pullKey))
                continue;

            _customPullsInFlight++;
            _ = Task.Run(() => RunCustomPull(pullKey, startPull));
        }
    }

    private static void RunCustomPull(string pullKey, Action startPull)
    {
        try
        {
            if (!IsPendingCustomPull(pullKey))
            {
                CustomPullCompleted();
                return;
            }

            if (!IsPendingCustomPull(pullKey) || Data.DeviceCts.IsCancellationRequested)
            {
                CompleteCustomPull(pullKey);
                return;
            }

            startPull();
        }
        catch
        {
            CompleteCustomPull(pullKey);
        }
    }

    private static bool IsPendingCustomPull(string pullKey)
    {
        lock (PendingCustomPullsLock)
            return PendingCustomPulls.Contains(pullKey);
    }

    private static void CustomPullCompleted()
    {
        lock (CustomPullScheduleLock)
        {
            if (_customPullsInFlight > 0)
                _customPullsInFlight--;
            TryStartNextCustomPull();
        }
    }

    private static void CancelCustomPull(string pullKey)
    {
        lock (PendingCustomPullsLock)
            PendingCustomPulls.Remove(pullKey);
    }

    private static void CompleteCustomPull(string pullKey)
    {
        CancelCustomPull(pullKey);
        CustomPullCompleted();
    }
}
