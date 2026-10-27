using System.Text;
using System.Text.Json;
using Vanara.Windows.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ADB_Explorer.Services;

/// <summary>
/// Drags that hold only FileDrop (no shell IDs) come from apps with no shell namespace of their own,
/// and some create their files only when dropped: 7-Zip and WinRAR extract them then, the new Outlook (Chromium) after.
/// </summary>
public partial class CopyPasteService
{
    /// <summary>
    /// Where dropped files the source won't keep are held until pushed. Not the temp drag folder, which a new drag
    /// or copy clears while pushes may still be queued. Each is removed by <see cref="ReleaseDropStaging"/>, and all when the app exits.
    /// </summary>
    private static string NewDropStaging()
    {
        lock (_dropStagingLock)
        {
            if (_dropStagingRoot is null)
            {
                _dropStagingRoot = Directory.CreateTempSubdirectory("ADB Explorer drop ").FullName;
                var root = _dropStagingRoot;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteDropStaging(root);

                // The queue going idle, or a failed op being removed, may free a staging folder
                if (Data.FileOpQ is { } queue)
                {
                    queue.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is nameof(FileOperationQueue.IsActive))
                            App.SafeBeginInvoke(SweepDropStaging);
                    };
                    queue.Operations.CollectionChanged += (_, _) => App.SafeBeginInvoke(SweepDropStaging);
                }

                // As may the pushed files being read for their image data
                ThumbnailService.PushedMediaRead += () => App.SafeBeginInvoke(SweepDropStaging);
            }
        }

        return Directory.CreateDirectory(Path.Combine(_dropStagingRoot, Guid.NewGuid().ToString("N"))).FullName;
    }

    private static readonly Lock _dropStagingLock = new();
    private static string? _dropStagingRoot;
    private static readonly HashSet<string> _releasedDropStaging = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Once <paramref name="push"/> has queued its ops, <paramref name="staging"/> goes when they no longer need it.</summary>
    private static async void ReleaseDropStagingAfter(Task push, string staging)
    {
        try
        {
            await push;
        }
        finally
        {
            ReleaseDropStaging(staging);
        }
    }

    /// <summary>Nothing more will be queued from <paramref name="staging"/>; it's deleted once the ops already queued are done with it.</summary>
    private static void ReleaseDropStaging(string staging)
    {
        lock (_dropStagingLock)
            _releasedDropStaging.Add(staging);

        App.SafeBeginInvoke(SweepDropStaging);
    }

    /// <summary>
    /// Deletes released staging folders while no op is waiting or running (some, like package installs, don't name their source),
    /// keeping any a failed op could still retry from.
    /// </summary>
    private static void SweepDropStaging()
    {
        if (Data.FileOpQ is not { } queue || queue.HasIncompleteOperations || ThumbnailService.IsReadingPushedMedia)
            return;

        List<string> deletable;
        lock (_dropStagingLock)
        {
            deletable = [.. _releasedDropStaging.Where(staging => !queue.Operations.Any(op =>
                op.Status is FileOperation.OperationStatus.Failed
                && op.FilePath?.FullPath is { } source
                && (source.Equals(staging, StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith(staging + '\\', StringComparison.OrdinalIgnoreCase))))];

            _releasedDropStaging.ExceptWith(deletable);
        }

        if (deletable.Count == 0)
            return;

        deletable.ForEach(ThumbnailService.ForgetPushThumbnails);
        Task.Run(() => deletable.ForEach(DeleteDropStaging));
    }

    private static void DeleteDropStaging(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        { }
    }

    /// <summary>The drag holds only FileDrop, read again at the drop, when the source may have just created its files.</summary>
    public bool IsFileDropOnly { get; private set; }

    /// <summary>A FileDrop-only source that hands its files over after the drop (async extraction), e.g. the new Outlook.</summary>
    public bool IsDelayedFileDrop { get; private set; }

    /// <summary>
    /// The dragged files are named only at the drop, so neither how many there are nor their names are known
    /// while dragging (7-Zip names its empty temp folder, the new Outlook nothing).
    /// </summary>
    public bool HasUnnamedFiles
    {
        get;
        private set
        {
            // CurrentFiles are listed according to it
            if (field != value)
                _currentFiles = null;

            field = value;
        }
    }

    /// <summary>
    /// The dragged files exist only from the drop: unnamed until then (<see cref="HasUnnamedFiles"/>), named relative to
    /// the archive WinRAR extracts them from, or named in the new Outlook's description of its drag, listed by those names.
    /// </summary>
    public bool HasPendingFiles
    {
        get;
        private set
        {
            if (field != value)
                _currentFiles = null;

            field = value;
        }
    }

    // The pending names are known to be files (the new Outlook's attachments), not possibly folders (WinRAR's)
    private bool _pendingAreFiles;

    /// <summary>
    /// The thumbnail the new Outlook offers of what it drags (as FileContents), shown as the drag image
    /// and kept as the dropped image's thumbnail. Read once per drag, in the background.
    /// </summary>
    public BitmapSource? DragThumbnail { get; private set; }

    // The drag DragThumbnail was read for, held weakly like the cancelled drag
    private WeakReference<object>? _thumbnailDrag;

    /// <summary>Forgets the thumbnail of a drag other than <paramref name="dataObject"/>.</summary>
    private void ForgetOtherDragThumbnail(IDataObject dataObject)
    {
        object drag = (object?)GetSourceDataObject(dataObject) ?? dataObject;
        if (_thumbnailDrag?.TryGetTarget(out var current) is true && ReferenceEquals(current, drag))
            return;

        _thumbnailDrag = null;
        DragThumbnail = null;
    }

    /// <summary>
    /// Starts reading the thumbnail the drag offers, unless it's already read (previews run on every move).
    /// Read while dragging, not dropped, so only when it weighs no more than a custom thumbnail may.
    /// </summary>
    private void LoadDragThumbnail(IDataObject dataObject)
    {
        if (_thumbnailDrag is not null
            || !dataObject.GetDataPresent(AdbDataFormats.FileContents)
            || GetSourceDataObject(dataObject) is not { } source)
        {
            return;
        }

        var drag = new WeakReference<object>(source);
        _thumbnailDrag = drag;

        var maxBytes = (long)Data.Settings.MaxCustomThumbWeight * 1024;
        if (maxBytes <= 0 || GetDragThumbnailSize(dataObject) > maxBytes)
            return;

        // Its source may take a while to hand it over, which mustn't hold up the drag
        var takeSource = NativeMethods.MarshalToThread(source);
        Task.Run(() =>
        {
            var image = ReadDragThumbnail(takeSource(), maxBytes);
            if (image is not null)
                App.SafeBeginInvoke(() =>
                {
                    if (ReferenceEquals(_thumbnailDrag, drag))
                        DragThumbnail = image;
                });
        });
    }

    /// <summary>
    /// The size of the drag's thumbnail from its descriptor (which is the thumbnail's), or null when it doesn't say.
    /// Chromium's never does (it sets only FD_LINKUI), so its content's own size decides instead.
    /// </summary>
    internal static long? GetDragThumbnailSize(IDataObject dataObject)
        => FileDescriptor.GetDescriptors(dataObject) is [{ Length: { } size }, ..] ? size : null;

    /// <summary>
    /// The image in the drag's first FileContents, or null when it doesn't read as one,
    /// or its content doesn't state its size or states more than <paramref name="maxBytes"/>.
    /// </summary>
    internal static BitmapSource? ReadDragThumbnail(ComDataObject dataObject, long maxBytes)
    {
        try
        {
            using var content = ShellContentStream.FromDataObject(dataObject, 0);
            if (content.StatedLength is not { } size || size > maxBytes)
                return null;

            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            buffer.Position = 0;

            // Not a BitmapFrame: frozen, it still keeps its decoder, which only this thread may touch
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception e) when (e is COMException or IOException or NotSupportedException or ArgumentException
            or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Chromium apps (the new Outlook) put the data a page set for the drag here
    private const string ChromiumCustomDataFormat = "Chromium Web Custom MIME Data Format";

    /// <summary>
    /// The drag's Chromium custom data by type: null when it has none (not from a Chromium app),
    /// empty when it has some that can't be read.
    /// </summary>
    internal static Dictionary<string, string>? GetChromiumCustomData(IDataObject dataObject)
    {
        try
        {
            if (!dataObject.GetDataPresent(ChromiumCustomDataFormat))
                return null;

            if (dataObject.GetData(ChromiumCustomDataFormat) is not MemoryStream stream)
                return [];

            return ParseChromiumCustomData(stream.ToArray());
        }
        catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>
    /// Chromium's custom data pickle: its payload size, the number of entries, then each entry's type and data,
    /// each a UTF-16 string as its length in chars and the chars, padded to 4 bytes. Empty when it doesn't read as one.
    /// </summary>
    internal static Dictionary<string, string> ParseChromiumCustomData(byte[] data)
    {
        Dictionary<string, string> entries = [];
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            reader.ReadUInt32();
            var count = reader.ReadUInt32();
            for (var i = 0; i < count; i++)
            {
                var type = ReadString16(reader);
                entries[type] = ReadString16(reader);
            }
        }
        catch (EndOfStreamException)
        {
            return [];
        }

        return entries;

        static string ReadString16(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length * 2L > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new EndOfStreamException();

            var text = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
            if (length % 2 != 0)
                reader.ReadBytes(2);

            return text;
        }
    }

    /// <summary>
    /// A drag the new Outlook describes (its items under "attachment"). Its FileGroupDescriptor and FileContents, when there,
    /// hold only a thumbnail named by a random ID; the files themselves come only through FileDrop, after the drop.
    /// </summary>
    internal static bool IsOutlookDescribedDrag(IDataObject dataObject)
        => GetChromiumCustomData(dataObject)?.ContainsKey(OutlookDescriptionType) is true;

    private const string OutlookDescriptionType = "attachment";

    /// <summary>
    /// The files the new Outlook's drag describes as attachments, or null for anything else it drags
    /// (its whole emails are "multimaillistconversationrows"), or a description that doesn't read as expected.
    /// </summary>
    internal static string[]? GetChromiumAttachmentNames(IReadOnlyDictionary<string, string> customData)
    {
        if (!customData.TryGetValue(OutlookDescriptionType, out var json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object
                || !root.TryGetProperty("itemType", out var itemType)
                || itemType.ValueKind is not JsonValueKind.String
                || itemType.GetString() != "attachment"
                || !root.TryGetProperty("attachmentFiles", out var files)
                || files.ValueKind is not JsonValueKind.Array)
            {
                return null;
            }

            List<string> names = [];
            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind is not JsonValueKind.Object
                    || !file.TryGetProperty("name", out var name)
                    || name.ValueKind is not JsonValueKind.String)
                {
                    return null;
                }

                // A file name, never a path
                var text = name.GetString();
                if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\\', '/']) >= 0 || text is "." or "..")
                    return null;

                names.Add(text);
            }

            return names.Count > 0 ? [.. names] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <returns>Whether the drag can be dropped here; it can't when its files appear only where a target-detect source sees it dropped.</returns>
    internal bool PreviewFileDrop(IDataObject dataObject)
    {
        var paths = GetExistingDropFiles(dataObject);

        // 7-Zip drags out of an archive name only the empty folder it extracts to at the drop.
        // Not its format, which it also offers for files dragged from a regular folder.
        var isSevenZip = paths?.All(IsExtractorTempFolder) is true;

        string[]? relativeNames = null;
        string[]? describedNames = null;
        if (paths is null && GetChromiumCustomData(dataObject) is { } customData)
        {
            // A Chromium app names nothing in FileDrop until after the drop, and may write nothing even then (the new
            // Outlook's whole emails), so only a drag it describes as files is taken: as a copy, once it wrote them
            describedNames = GetChromiumAttachmentNames(customData);
            if (describedNames is null || !IsAsyncSource(dataObject))
                return false;
        }
        else if (paths is null)
        {
            // WinRAR names them relative to the archive root, and extracts them at the drop
            relativeNames = GetRelativeDropNames(dataObject);
        }

        var isDelayed = describedNames is not null || (paths is null && relativeNames is null && IsAsyncSource(dataObject));
        if (paths is null && !isDelayed && relativeNames is null)
            return false;

        SourceDevice = null;
        CurrentSource &= ~DataSource.Android;
        IsFileDropOnly = true;
        IsDelayedFileDrop = isDelayed;
        HasUnnamedFiles = isSevenZip || (isDelayed && describedNames is null);
        HasPendingFiles = HasUnnamedFiles || relativeNames is not null || describedNames is not null;
        _pendingAreFiles = describedNames is not null;

        if (describedNames is not null)
            LoadDragThumbnail(dataObject);

        // Its files are created after the drop, so that's where they're read from
        if (isDelayed)
            CurrentSource |= DataSource.Virtual;

        if (HasUnnamedFiles)
        {
            Descriptors = [];
            DragFiles = [""];
        }
        else if (relativeNames is not null)
        {
            // Dropped by these names, so the drag is shown and checked as any other
            Descriptors = [];
            DragFiles = [.. relativeNames.Select(name => FileHelper.GetFullName(name.Replace('/', '\\')))];
        }
        else if (describedNames is not null)
        {
            Descriptors = [];
            DragFiles = describedNames;
        }
        else
        {
            Descriptors = [.. paths!.Select(path => new FileDescriptor(ShellItem.Open(path)))];
            DragFiles = paths!;
        }

        return true;
    }

    /// <summary>
    /// FileDrop's paths when they all exist. A target-detect drag names files it only creates once it sees where it
    /// was dropped, WinRAR names them relative to its archive until the drop, and Chromium refuses to name them before it.
    /// </summary>
    internal static string[]? GetExistingDropFiles(IDataObject dataObject)
    {
        try
        {
            if (dataObject.GetDataPresent(AdbDataFormats.FileDrop)
                && dataObject.GetData(AdbDataFormats.FileDrop) is string[] { Length: > 0 } dropped)
            {
                // Read again at the drop for sources that name their files only then (7-Zip)
                var paths = WithoutParentReferences(dropped);
                if (paths.Length > 0 && paths.All(path => File.Exists(path) || Directory.Exists(path)))
                    return paths;
            }
        }
        catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException)
        {
            // The source refused the data (e.g. Chromium during the drag)
        }

        return null;
    }

    /// <summary>FileDrop's names when it holds no paths yet, only names relative to the archive they'll be extracted from (WinRAR).</summary>
    internal static string[]? GetRelativeDropNames(IDataObject dataObject)
    {
        try
        {
            if (dataObject.GetDataPresent(AdbDataFormats.FileDrop)
                && dataObject.GetData(AdbDataFormats.FileDrop) is string[] { Length: > 0 } names
                && names.All(name => !string.IsNullOrEmpty(name) && !Path.IsPathRooted(name) && !FileHelper.HasParentReference(name)))
            {
                return names;
            }
        }
        catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException)
        {
            // The source refused the data
        }

        return null;
    }

    /// <returns>The staging folder the files were moved to, if they were.</returns>
    private string? TakeDroppedFiles(IDataObject dataObject)
    {
        if (GetDroppedFiles(dataObject, out var staging) is not { } paths)
            return null;

        // Before DragFiles, which drops the files listed from the pending state
        HasUnnamedFiles = HasPendingFiles = false;
        DragFiles = paths;

        return staging;
    }

    internal static string[]? GetDroppedFiles(IDataObject dataObject) => GetDroppedFiles(dataObject, out _);

    /// <summary>
    /// The files dropped, read again: 7-Zip and WinRAR extract them only now, to a temp folder 7-Zip removes as soon as
    /// the drop returns (WinRAR when it closes), so they're moved to ours (<paramref name="staging"/>) first.
    /// Other sources keep their files where they are.
    /// </summary>
    internal static string[]? GetDroppedFiles(IDataObject dataObject, out string? staging)
    {
        staging = null;
        if (GetExistingDropFiles(dataObject) is not { } paths)
            return null;

        // Only files an archiver extracted; one dragged from a regular folder is the user's own
        if (paths.All(IsInExtractorTemp))
        {
            var folder = staging = NewDropStaging();
            paths = [.. paths.Select(path => MoveInto(path, folder))];
        }

        return paths;
    }

    /// <summary>Incoming paths that step up a folder are refused, whenever their names become known.</summary>
    private static string[] WithoutParentReferences(IEnumerable<string> paths)
        => [.. paths.Where(path => !FileHelper.HasParentReference(path))];

    // Where a drag out of an archive is extracted: %TEMP%\7zE* (7-Zip), %TEMP%\Rar$DR* (WinRAR)
    private static readonly string[] ExtractorTempPrefixes = ["7zE", "Rar$DR"];

    private static bool IsInExtractorTemp(string path) => IsExtractorTempFolder(FileHelper.GetParentPath(path));

    private static bool IsExtractorTempFolder(string path)
        => ExtractorTempPrefixes.Any(prefix => FileHelper.GetFullName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            && string.Equals(FileHelper.GetParentPath(path).TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // The drag the user cancelled with a right click, refused from then on. Known by the source's own object, as WPF
    // wraps each entry into the window anew, and held weakly so a drag that's over isn't kept alive.
    private WeakReference<object>? _canceledDrag;
    private WeakReference<object>? _currentDrag;

    /// <summary>Whether the drag being previewed was cancelled with a right click.</summary>
    public bool IsDragCanceled { get; private set; }

    /// <summary>
    /// A right click cancels an external drag, whose source cancels only on Esc, so that's pressed for it.
    /// The drag is also refused from then on, in case its source ignores Esc.
    /// </summary>
    public void CancelExternalDrag()
    {
        _canceledDrag = _currentDrag;
        NativeMethods.CancelForegroundDrag();
    }

    /// <returns>Whether <paramref name="dataObject"/> is the drag that was cancelled, noting it as the current one.</returns>
    private bool TrackDrag(IDataObject dataObject)
    {
        object drag = (object?)GetSourceDataObject(dataObject) ?? dataObject;
        _currentDrag = new(drag);
        IsDragCanceled = _canceledDrag?.TryGetTarget(out var canceled) is true && ReferenceEquals(drag, canceled);

        return IsDragCanceled;
    }

    private static string MoveInto(string path, string folder)
    {
        var target = Path.Combine(folder, Path.GetFileName(path));
        // Both are under %TEMP%, so on one volume
        if (Directory.Exists(path))
            Directory.Move(path, target);
        else
            File.Move(path, target);

        return target;
    }

    /// <summary>The source's own COM data object, behind WPF's wrapper, which doesn't pass on <see cref="IAsyncOperation"/>.</summary>
    internal static ComDataObject? GetSourceDataObject(IDataObject dataObject)
    {
        // DataObject wraps what OLE hands its drop target (new DataObject((IComDataObject)data)); there's no public way back
        try
        {
            var composition = typeof(DataObject).GetField("_innerData", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dataObject);

            var source = composition?.GetType().GetField("_runtimeDataObject", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(composition);

            // Data of our own has WPF's adapter there, which has no source behind it
            if (source is not ComDataObject comSource || source.GetType().Namespace?.StartsWith("System.", StringComparison.Ordinal) is true)
                return null;

            return comSource;
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsAsyncSource(IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(AdbDataFormats.FileDrop) || GetSourceDataObject(dataObject) is not IAsyncOperation asyncOperation)
            return false;

        try
        {
            asyncOperation.GetAsyncMode(out var isAsync);

            return isAsync != 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes files that the source creates after the drop: it's told now that we'll ask for them,
    /// and once the drag is over they're asked for (which waits until they're written), copied, and pushed.
    /// </summary>
    /// <param name="thumbnail">An image of the file with this name, kept as its thumbnail once it's pushed (if it's a photo).</param>
    internal static void PushDelayedFileDrop(IDataObject dataObject, string targetFolder, LogicalDeviceViewModel device, (string Name, BitmapSource Image)? thumbnail = null)
    {
        if (GetSourceDataObject(dataObject) is not { } source || source is not IAsyncOperation asyncOperation)
            return;

        asyncOperation.StartOperation(null!);
        var takeSource = NativeMethods.MarshalToThread(source);

        Task.Run(() =>
        {
            var worker = takeSource();
            var result = NativeMethods.HResult.Fail;
            string? staging = null;
            string[] staged = [];

            try
            {
                // Named only now that the source has written them
                var paths = WithoutParentReferences(GetDelayedPaths(worker));
                var folder = staging = NewDropStaging();

                // Copied, as the source may clean its files up once told we're done
                staged = [.. paths.Select(path => CopyInto(path, folder))];
                result = NativeMethods.HResult.Ok;
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Delayed FileDrop failed: {e.Message}");
#endif
            }
            finally
            {
                ((IAsyncOperation)worker).EndOperation((int)result, null!, (uint)DragDropEffects.Copy);
            }

            if (staging is null)
                return;

            if (staged.Length == 0)
            {
                ReleaseDropStaging(staging);
                return;
            }

            if (thumbnail is { } shot
                && staged.FirstOrDefault(path => Path.GetFileName(path).Equals(shot.Name, StringComparison.OrdinalIgnoreCase)) is { } thumbnailOf
                && AdbExplorerConst.COMMON_PHOTO_EXT.Contains(Path.GetExtension(thumbnailOf), StringComparer.InvariantCultureIgnoreCase))
            {
                ThumbnailService.RegisterPushThumbnail(thumbnailOf, shot.Image);
            }

            App.SafeInvoke(() => ReleaseDropStagingAfter(
                VerifyAndPushAsync(targetFolder, [.. staged.Select(path => new FileClass(ShellItem.Open(path)))], DragDropEffects.Copy, device),
                staging));
        });
    }

    /// <summary>Chromium refuses until its drag loop is over, which may be just after the drop returns.</summary>
    internal static string[] GetDelayedPaths(ComDataObject source)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return NativeMethods.GetFileDropPaths(source);
            }
            catch (COMException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static string CopyInto(string path, string folder)
    {
        var target = Path.Combine(folder, Path.GetFileName(path));
        if (!Directory.Exists(path))
        {
            File.Copy(path, target);
            return target;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(path, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        Directory.CreateDirectory(target);

        return target;
    }
}
