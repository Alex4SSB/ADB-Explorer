using Vanara.Windows.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ADB_Explorer.Services;

/// <summary>
/// Drags that hold only FileDrop (no shell IDs) come from apps with no shell namespace of their own,
/// and some create their files only when dropped: 7-Zip extracts them then, the new Outlook (Chromium) after.
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
        if (Data.FileOpQ is not { } queue || queue.HasIncompleteOperations)
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

        if (deletable.Count > 0)
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

    /// <returns>Whether the drag can be dropped here; it can't when its files appear only where a target-detect source sees it dropped.</returns>
    private bool PreviewFileDrop(IDataObject dataObject)
    {
        var paths = GetExistingDropFiles(dataObject);

        // 7-Zip drags out of an archive name only the empty folder it extracts to at the drop.
        // Not its format, which it also offers for files dragged from a regular folder.
        var isSevenZip = paths?.All(IsSevenZipTempFolder) is true;
        var isDelayed = paths is null && IsAsyncSource(dataObject);
        if (paths is null && !isDelayed)
            return false;

        SourceDevice = null;
        CurrentSource &= ~DataSource.Android;
        IsFileDropOnly = true;
        IsDelayedFileDrop = isDelayed;
        HasUnnamedFiles = isSevenZip || isDelayed;

        // Its files are created after the drop, so that's where they're read from
        if (isDelayed)
            CurrentSource |= DataSource.Virtual;

        if (HasUnnamedFiles)
        {
            Descriptors = [];
            DragFiles = [""];
        }
        else
        {
            Descriptors = [.. paths!.Select(path => new FileDescriptor(ShellItem.Open(path)))];
            DragFiles = paths!;
        }

        return true;
    }

    /// <summary>
    /// FileDrop's paths when they all exist. A target-detect drag (WinRAR) names files it only creates
    /// once it sees where it was dropped, and Chromium refuses to name them before the drop.
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

    /// <returns>The staging folder the files were moved to, if they were.</returns>
    private string? TakeDroppedFiles(IDataObject dataObject)
    {
        if (GetDroppedFiles(dataObject, out var staging) is not { } paths)
            return null;

        // Before DragFiles, which drops the files listed from the unnamed state
        HasUnnamedFiles = false;
        DragFiles = paths;

        return staging;
    }

    internal static string[]? GetDroppedFiles(IDataObject dataObject) => GetDroppedFiles(dataObject, out _);

    /// <summary>
    /// The files dropped, read again: 7-Zip extracts them only now, to a temp folder it removes as soon as
    /// the drop returns, so they're moved to ours (<paramref name="staging"/>) first. Other sources keep their files where they are.
    /// </summary>
    internal static string[]? GetDroppedFiles(IDataObject dataObject, out string? staging)
    {
        staging = null;
        if (GetExistingDropFiles(dataObject) is not { } paths)
            return null;

        // Only files 7-Zip extracted; one dragged from a regular folder is the user's own
        if (paths.All(IsInSevenZipTemp))
        {
            var folder = staging = NewDropStaging();
            paths = [.. paths.Select(path => MoveInto(path, folder))];
        }

        return paths;
    }

    /// <summary>Incoming paths that step up a folder are refused, whenever their names become known.</summary>
    private static string[] WithoutParentReferences(IEnumerable<string> paths)
        => [.. paths.Where(path => !FileHelper.HasParentReference(path))];

    // 7-Zip extracts a drag out of an archive to %TEMP%\7zE*
    private static bool IsInSevenZipTemp(string path) => IsSevenZipTempFolder(FileHelper.GetParentPath(path));

    private static bool IsSevenZipTempFolder(string path)
        => FileHelper.GetFullName(path).StartsWith("7zE", StringComparison.OrdinalIgnoreCase)
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
    internal static void PushDelayedFileDrop(IDataObject dataObject, string targetFolder, LogicalDeviceViewModel device)
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
