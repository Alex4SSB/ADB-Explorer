using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    private static string? PendingClipboardImageStagingPath { get; set; }

    private static BitmapSource? PendingClipboardImageThumbnail { get; set; }

    private static FileClass? PendingClipboardImageTemp { get; set; }

    public static bool IsPendingClipboardImage { get; private set; }

    public static BitmapSource? GetPendingClipboardImageThumbnail() => PendingClipboardImageThumbnail;

    /// <summary>
    /// True when the Windows clipboard holds an image and the current Explorer location
    /// can accept a new pasted item (same gating as New Folder/New File).
    /// </summary>
    public static bool CanPasteClipboardImage() =>
        Data.CopyPaste.HasClipboardImage
        && Data.FileActions.NewEnabled
        && !Data.FileActions.IsExplorerEditing
        && Data.ActiveDevice is not null;

    /// <summary>
    /// Selection gating for "Paste as image" - the same single-target-selection rule
    /// <see cref="EnableUiPaste"/>/<see cref="EnableKeyboardPaste"/> apply to other files,
    /// including keyboard paste treating a multi-selection as none.
    /// </summary>
    public static bool CanPasteClipboardImageAtSelection(bool isKeyboard = false)
    {
        if (!CanPasteClipboardImage())
            return false;

        var selected = Data.SelectedFiles;
        var count = selected.Count();

        if (isKeyboard && count > 1)
            count = 0;

        if (count == 0)
            return true;

        return count == 1 && ArchiveHelper.IsPasteTargetContainer(selected.First(), ActionDevice?.ID ?? "");
    }

    public static void BeginPasteClipboardImage()
    {
        if (Clipboard.GetImage() is not BitmapSource image)
            return;

        var device = ActionDevice;
        if (device is null)
            return;

        var stagingDir = Path.Combine(Path.GetTempPath(), "ADB Explorer", "ClipboardPaste");
        Directory.CreateDirectory(stagingDir);
        var stagingPath = Path.Combine(stagingDir, $"{Guid.NewGuid()}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var fs = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None))
            encoder.Save(fs);

        var targetFolder = ResolveClipboardImagePasteFolder(device.ID);
        if (!NavigationTreeNode.PathsEqual(targetFolder, Data.CurrentPath))
        {
            // Target is a selected folder other than the one being browsed - there's no visible
            // row to rename inline there, so push it directly under an auto-generated name.
            PushClipboardImageToFolder(stagingPath, targetFolder, device);
            return;
        }

        var maxDimension = (int)ThumbnailService.ThumbnailSize.Drag * 2;
        PendingClipboardImageThumbnail = ThumbnailService.CreateScaledPreview(image, maxDimension);
        PendingClipboardImageStagingPath = stagingPath;
        PendingClipboardImageTemp = null;
        IsPendingClipboardImage = true;

        Data.RequestExplorer(ExplorerRequest.PasteClipboardImage);
    }

    /// <summary>
    /// Same single-target-selection resolution regular file paste uses: a single selected
    /// valid container is the target, else the current folder.
    /// </summary>
    private static string ResolveClipboardImagePasteFolder(string deviceId)
    {
        var selected = Data.SelectedFiles;
        if (selected.Count() == 1 && selected.First() is { } item && ArchiveHelper.IsPasteTargetContainer(item, deviceId))
        {
            var path = item.IsLink ? item.LinkTarget : item.FullPath;
            return ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
        }

        return Data.CurrentPath;
    }

    private static async void PushClipboardImageToFolder(string stagingPath, string targetFolder, LogicalDeviceViewModel device)
    {
        var caseSensitive = DriveHelper.GetRestrictions(targetFolder, device).CaseInsensitiveNames is not true;
        var comparer = caseSensitive ? StringComparer.InvariantCulture : StringComparer.InvariantCultureIgnoreCase;

        var existingNames = await Task.Run(()
            => (IEnumerable<string>?)FileMergeHelper.TryListAndroidDirByName(device.ID, targetFolder, comparer)?.Keys ?? []);

        var fileName = FileHelper.DuplicateFile(existingNames, GetClipboardImageFileName());
        var file = new FileClass(fileName, FileHelper.ConcatPaths(targetFolder, fileName), FileType.File);

        PushClipboardImageFile(file, stagingPath, device);
    }

    /// <summary>
    /// Same naming convention as Windows' own screenshot tools, e.g. "Screenshot 2025-11-22 223048.png".
    /// </summary>
    public static string GetClipboardImageFileName() =>
        $"{Strings.Resources.S_SCREENSHOT} {DateTime.Now:yyyy-MM-dd HHmmss}.png";

    public static void SetPendingClipboardImageTemp(FileClass file) => PendingClipboardImageTemp = file;

    public static void CancelPendingClipboardImage(FileClass? file = null)
    {
        if (!IsPendingClipboardImage)
            return;

        if (file is not null
            && PendingClipboardImageTemp is not null
            && !ReferenceEquals(file, PendingClipboardImageTemp))
            return;

        DeleteClipboardImageStagingFile(PendingClipboardImageStagingPath);

        IsPendingClipboardImage = false;
        PendingClipboardImageStagingPath = null;
        PendingClipboardImageThumbnail = null;
        PendingClipboardImageTemp = null;
    }

    private static bool TryConsumePendingClipboardImage(FileClass file, out string stagingPath)
    {
        stagingPath = "";
        if (!IsPendingClipboardImage
            || !ReferenceEquals(file, PendingClipboardImageTemp)
            || PendingClipboardImageStagingPath is null)
            return false;

        stagingPath = PendingClipboardImageStagingPath;
        IsPendingClipboardImage = false;
        PendingClipboardImageStagingPath = null;
        PendingClipboardImageThumbnail = null;
        PendingClipboardImageTemp = null;
        return true;
    }

    private static void DeleteClipboardImageStagingFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        { }
    }

    /// <summary>
    /// Decodes off the UI thread (a lazy COM callback here could deadlock this app and the pasting
    /// app), then writes via raw Win32 clipboard calls - DataObject.SetImage also advertises a
    /// CF_BITMAP handle that some readers choke on, breaking every other format too.
    /// </summary>
    public static async void CopyAsImage()
    {
        var file = Data.SelectedFiles.First();
        var device = ActionDevice;
        if (device is null)
            return;

        var bitmap = await Task.Run(() => ProduceClipboardBitmap(device, file));
        if (bitmap is null)
            return;

        NativeMethods.SetClipboardImage(bitmap);

        // Unlike Clipboard.SetDataObject, a raw SetClipboardData write doesn't run through this
        // app's own clipboard-changed handling until the async WM_CLIPBOARDUPDATE round-trip
        // completes. Refresh state and force a command requery now instead of waiting for it.
        Data.CopyPaste.GetClipboardPasteItems();
        CommandManager.InvalidateRequerySuggested();
    }

    private static BitmapSource? ProduceClipboardBitmap(LogicalDeviceViewModel device, FileClass file)
    {
        try
        {
            using var stream = AdbHelper.ReadFileAsStreamAsync(device, file.FullPath, CancellationToken.None).GetAwaiter().GetResult();
            if (stream is null)
                return null;

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            // A decoder-backed frame keeps native WIC affinity to this thread even once frozen,
            // throwing "different thread owns it" when the UI thread later touches it. Copying
            // into a plain BitmapSource fully detaches the pixel data before handing it off.
            var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);

            var detached = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, frame.DpiX, frame.DpiY, frame.Format, frame.Palette, pixels, stride);
            detached.Freeze();

            return detached;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pushes a confirmed clipboard-image staging file to <paramref name="file"/>'s final (renamed) path,
    /// then deletes the staging file and seeds a custom thumbnail once the transfer completes.
    /// </summary>
    private static void PushClipboardImageFile(FileClass file, string stagingPath, LogicalDeviceViewModel device)
    {
        // Reserved for the whole push so a concurrent details-pane/icon-view thumbnail request can't
        // pull the still-in-flight target path and win the cache entry over the correct seeded image.
        var seedReserved = Data.Settings.MaxCustomThumbWeight > 0
            && ThumbnailService.TryReserveCustomThumbnailSeed(device.SerialNumber, file);

        var source = new SyncFile(ShellItem.Open(stagingPath));
        var target = new SyncFile(file.FullPath, FileType.File) { Size = source.Size };

        var pushOperation = FileSyncOperation.PushFile(source, target, device, App.AppDispatcher);
        pushOperation.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(FileOperation.Status))
                return;

            var op = (FileSyncOperation)s!;
            if (op.Status is FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress)
                return;

            if (op.Status is FileOperation.OperationStatus.Completed && Data.Settings.MaxCustomThumbWeight > 0)
                SeedClipboardImageThumbnail(device, file, stagingPath);

            if (seedReserved)
                ThumbnailService.ReleaseCustomThumbnailSeed(device.SerialNumber, file);

            DeleteClipboardImageStagingFile(stagingPath);
        };

        Data.FileOpQ.AddOperation(pushOperation);
    }

    private static void SeedClipboardImageThumbnail(LogicalDeviceViewModel device, FileClass file, string stagingPath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(stagingPath);
            bitmap.EndInit();
            bitmap.Freeze();

            ThumbnailService.SeedCustomThumbnail(device, file, bitmap);
        }
        catch
        { }
    }
}
