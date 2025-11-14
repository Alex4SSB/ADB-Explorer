// Virtual File Data Object library by David Anson
// https://dlaa.me/blog/post/9913083
// Used and modified under the MIT license

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using System.Runtime.InteropServices.ComTypes;
using Vanara.Extensions;
using static ADB_Explorer.Services.FileDescriptor;
using static Vanara.PInvoke.Shell32;

namespace ADB_Explorer.Services;

/// <summary>
/// Handles everything related to Shell Drag & Drop (including clipboard) 
/// </summary>
public sealed class VirtualFileDataObject : ViewModelBase, System.Runtime.InteropServices.ComTypes.IDataObject, IAsyncOperation
{
    public static FileGroup SelfFileGroup { get; private set; }
    public static IEnumerable<FileClass> SelfFiles { get; private set; }
    public static string DummyFileName { get; private set; }

    /// <summary>
    /// In-order list of registered data objects.
    /// </summary>
    private readonly List<DataObject> dataObjects = [];

    /// <summary>
    /// Tracks whether an asynchronous operation is ongoing.
    /// </summary>
    private bool inOperation;

    public DataObjectMethod Method { get; private set; }

    public VirtualFileDataObject(DragDropEffects preferredDropEffect = DragDropEffects.Copy | DragDropEffects.Move, DataObjectMethod method = DataObjectMethod.DragDrop)
    {
        PreferredDropEffect = preferredDropEffect;
        Method = method;
    }

    #region IDataObject Members
    // Explicit interface implementation hides the technical details from users of VirtualFileDataObject.

    /// <summary>
    /// Creates a connection between a data object and an advisory sink.
    /// </summary>
    /// <param name="pFormatetc">A FORMATETC structure that defines the format, target device, aspect, and medium that will be used for future notifications.</param>
    /// <param name="advf">One of the ADVF values that specifies a group of flags for controlling the advisory connection.</param>
    /// <param name="adviseSink">A pointer to the IAdviseSink interface on the advisory sink that will receive the change notification.</param>
    /// <param name="connection">When this method returns, contains a pointer to a DWORD token that identifies this connection.</param>
    /// <returns>HRESULT success code.</returns>
    int System.Runtime.InteropServices.ComTypes.IDataObject.DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
    {
        NativeMethods.ThrowExceptionForHR(NativeMethods.HResult.OLE_E_ADVISENOTSUPPORTED);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Destroys a notification connection that had been previously established.
    /// </summary>
    /// <param name="connection">A DWORD token that specifies the connection to remove.</param>
    void System.Runtime.InteropServices.ComTypes.IDataObject.DUnadvise(int connection)
    {
        NativeMethods.ThrowExceptionForHR(NativeMethods.HResult.OLE_E_ADVISENOTSUPPORTED);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates an object that can be used to enumerate the current advisory connections.
    /// </summary>
    /// <param name="enumAdvise">When this method returns, contains an IEnumSTATDATA that receives the interface pointer to the new enumerator object.</param>
    /// <returns>HRESULT success code.</returns>
    int System.Runtime.InteropServices.ComTypes.IDataObject.EnumDAdvise(out IEnumSTATDATA enumAdvise)
    {
        NativeMethods.ThrowExceptionForHR(NativeMethods.HResult.OLE_E_ADVISENOTSUPPORTED);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates an object for enumerating the FORMATETC structures for a data object.
    /// </summary>
    /// <param name="direction">One of the DATADIR values that specifies the direction of the data.</param>
    /// <returns>IEnumFORMATETC interface.</returns>
    IEnumFORMATETC System.Runtime.InteropServices.ComTypes.IDataObject.EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET)
            throw new NotImplementedException();

        if (0 == dataObjects.Count)
        {
            // Note: SHCreateStdEnumFmtEtc fails for a count of 0; throw helpful exception
            throw new InvalidOperationException("VirtualFileDataObject requires at least one data object to enumerate.");
        }

        // Create enumerator and return it
        var res = NativeMethods.SHCreateStdEnumFmtEtc(dataObjects.Count, dataObjects.Select(d => d.FORMATETC), out IEnumFORMATETC enumerator);
        if (res is NativeMethods.HResult.Ok)
        {
            return enumerator;
        }

        // Returning null here can cause an AV in the caller; throw instead
        NativeMethods.ThrowExceptionForHR(res);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Provides a standard FORMATETC structure that is logically equivalent to a more complex structure.
    /// </summary>
    /// <param name="formatIn">A pointer to a FORMATETC structure that defines the format, medium, and target device that the caller would like to use to retrieve data in a subsequent call such as GetData.</param>
    /// <param name="formatOut">When this method returns, contains a pointer to a FORMATETC structure that contains the most general information possible for a specific rendering, making it canonically equivalent to formatetIn.</param>
    /// <returns>HRESULT success code.</returns>
    int System.Runtime.InteropServices.ComTypes.IDataObject.GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Obtains data from a source data object.
    /// </summary>
    /// <param name="format">A pointer to a FORMATETC structure that defines the format, medium, and target device to use when passing the data.</param>
    /// <param name="medium">When this method returns, contains a pointer to the STGMEDIUM structure that indicates the storage medium containing the returned data through its tymed member, and the responsibility for releasing the medium through the value of its pUnkForRelease member.</param>
    void System.Runtime.InteropServices.ComTypes.IDataObject.GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        var formatCopy = format;
        medium = new();

        var hr = (NativeMethods.HResult)((System.Runtime.InteropServices.ComTypes.IDataObject)this).QueryGetData(ref format);

#if !DEPLOY
        AdbDataFormat adbDataFormat = AdbDataFormats.GetFormat(formatCopy.cfFormat);

        // Unknown formats are not printed
        if (adbDataFormat is not null)
            DebugLog.PrintLine($"Query: {adbDataFormat.Name}");
#endif

        if (hr is NativeMethods.HResult.Ok)
        {
            // Find the best match
            var dataObject = dataObjects.FirstOrDefault(d =>
                    d.FORMATETC.cfFormat == formatCopy.cfFormat
                    && d.FORMATETC.dwAspect == formatCopy.dwAspect
                    && 0 != (d.FORMATETC.tymed & formatCopy.tymed)
                    && d.FORMATETC.lindex == formatCopy.lindex);

            if (dataObject is not null)
            {
#if !DEPLOY
                var index = adbDataFormat == AdbDataFormats.FileContents
                        ? $"[{dataObject.FORMATETC.lindex}]"
                        : "";

                // Unknown formats are not printed
                if (adbDataFormat is not null)
                    DebugLog.PrintLine($"Get data: {adbDataFormat.Name}{index}");
#endif

                // Populate the STGMEDIUM
                medium.tymed = dataObject.FORMATETC.tymed;
                var result = dataObject.GetData(); // Possible call to user code

                hr = result.Item2;
                if (hr is NativeMethods.HResult.Ok)
                {
                    medium.unionmember = result.Item1;
                }
            }
            else
            {
                // Couldn't find a match
                hr = NativeMethods.HResult.DV_E_FORMATETC;
            }
        }

        // Not redundant; hr gets updated in the block above
        if (hr is NativeMethods.HResult.Ok)
            return;

        // We seem unable to send data to File Explorer in DEBUG, even when not debugging.
        // This might be because of the FileContents data format, which is an async stream
        // compared to all other data formats which are just byte arrays on HGlobal, already populated with the data
        var ex = Marshal.GetExceptionForHR((int)hr);
#if DEBUG
        Trace.WriteLine(ex?.Message);
#else
        throw ex;
#endif
    }

    /// <summary>
    /// Obtains data from a source data object.
    /// </summary>
    /// <param name="format">A pointer to a FORMATETC structure that defines the format, medium, and target device to use when passing the data.</param>
    /// <param name="medium">A STGMEDIUM that defines the storage medium containing the data being transferred.</param>
    void System.Runtime.InteropServices.ComTypes.IDataObject.GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
    {
        throw new NotImplementedException();
    }
    
    /// <summary>
    /// Determines whether the data object is capable of rendering the data described in the FORMATETC structure.
    /// </summary>
    /// <param name="format">A pointer to a FORMATETC structure that defines the format, medium, and target device to use for the query.</param>
    /// <returns>HRESULT success code.</returns>
    int System.Runtime.InteropServices.ComTypes.IDataObject.QueryGetData(ref FORMATETC format)
    {
        NativeMethods.HResult GetError(FORMATETC format)
        {
            var formatCopy = format; // Cannot use ref or out parameter inside an anonymous method, lambda expression, or query expression
            var formatMatches = dataObjects.Where(d => d.FORMATETC.cfFormat == formatCopy.cfFormat);
            if (!formatMatches.Any())
            {
                return NativeMethods.HResult.DV_E_FORMATETC;
            }
            var tymedMatches = formatMatches.Where(d => 0 != (d.FORMATETC.tymed & formatCopy.tymed));
            if (!tymedMatches.Any())
            {
                return NativeMethods.HResult.DV_E_TYMED;
            }
            var aspectMatches = tymedMatches.Where(d => d.FORMATETC.dwAspect == formatCopy.dwAspect);
            if (!aspectMatches.Any())
            {
                return NativeMethods.HResult.DV_E_DVASPECT;
            }
            return NativeMethods.HResult.Ok;
        }

        return (int)GetError(format);
    }

    /// <summary>
    /// Transfers data to the object that implements this method.
    /// </summary>
    /// <param name="formatIn">A FORMATETC structure that defines the format used by the data object when interpreting the data contained in the storage medium.</param>
    /// <param name="medium">A STGMEDIUM structure that defines the storage medium in which the data is being passed.</param>
    /// <param name="release">true to specify that the data object called, which implements SetData, owns the storage medium after the call returns.</param>
    void System.Runtime.InteropServices.ComTypes.IDataObject.SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        var handled = false;
        if (medium.tymed == formatIn.tymed
            && formatIn is {
                dwAspect: DVASPECT.DVASPECT_CONTENT, 
                tymed: TYMED.TYMED_HGLOBAL
            })
        {
            // Supported format; capture the data
            var ptr = NativeMethods.MGlobalLock(medium.unionmember);
            if (IntPtr.Zero != ptr)
            {
                try
                {
                    var length = NativeMethods.MGlobalSize(ptr).ToInt32();
                    var data = new byte[length];
                    Marshal.Copy(ptr, data, 0, length);
                    
                    // Store it in our own format
                    var format = AdbDataFormats.GetFormat(formatIn.cfFormat) ?? new AdbDataFormat(formatIn.cfFormat);
                    SetData(format, data);
                    handled = true;
                }
                finally
                {
                    NativeMethods.MGlobalUnlock(medium.unionmember);
                }
            }

            // Release memory if we now own it
            if (release)
            {
                Marshal.FreeHGlobal(medium.unionmember);
            }
        }

        // Throw if unhandled
        if (!handled)
        {
            throw new NotImplementedException();
        }
    }

#endregion

    public string[] GetFormats()
        => [.. dataObjects.Select(o => AdbDataFormats.GetFormatName(o.FORMATETC.cfFormat))];

    public static FORMATETC CreateFormat(AdbDataFormat dataFormat, int index = -1) => new()
    {
        cfFormat = dataFormat,
        ptd = IntPtr.Zero,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = index,
        tymed = dataFormat.tymed,
    };

    public void UpdateData(AdbDataFormat dataFormat, IEnumerable<byte> data)
    {
        var query = dataObjects.Where(dataObject => dataObject.FORMATETC.cfFormat == dataFormat);
        if (!query.Any())
        {
            SetData(dataFormat, data);
            return;
        }

        var dataObject = query.First();
        Marshal.FreeHGlobal(dataObject.GetData().Item1);

        dataObject.GetData = () =>
        {
            var dataArray = data.ToArray();
            var ptr = Marshal.AllocHGlobal(dataArray.Length);
            Marshal.Copy(dataArray, 0, ptr, dataArray.Length);
            return (ptr, NativeMethods.HResult.Ok);
        };
    }

    /// <summary>
    /// Provides data for the specified data format (HGLOBAL).
    /// </summary>
    /// <param name="dataFormat">Data format.</param>
    /// <param name="data">Sequence of data.</param>
    public void SetData(AdbDataFormat dataFormat, IEnumerable<byte> data)
        => dataObjects.Add(new()
    {
        FORMATETC = CreateFormat(dataFormat),
        GetData = () =>
        {
            var dataArray = data.ToArray();
            var ptr = Marshal.AllocHGlobal(dataArray.Length);
            Marshal.Copy(dataArray, 0, ptr, dataArray.Length);
            return (ptr, NativeMethods.HResult.Ok);
        },
    });

    public void UpdateData(AdbDataFormat dataFormat, IEnumerable<StreamContents> dataStreams)
    {
        // Remove all previous streams
        dataObjects.RemoveAll(d => d.FORMATETC.cfFormat == dataFormat);

        // Set n CFSTR_FILECONTENTS
        var index = 0;
        foreach (var stream in dataStreams)
        {
            SetData(dataFormat, index, stream);
            index++;
        }
    }

    /// <summary>
    /// Provides data for the specified data format and index (ISTREAM).
    /// </summary>
    /// <param name="dataFormat">Data format.</param>
    /// <param name="index">Index of data.</param>
    /// <param name="streamData">Action generating the data.</param>
    /// <remarks>
    /// Uses Stream instead of IEnumerable(T) because Stream is more likely
    /// to be natural for the expected scenarios.
    /// </remarks>
    public void SetData(AdbDataFormat dataFormat, int index, StreamContents streamData)
        => dataObjects.Add(new()
    {
        FORMATETC = CreateFormat(dataFormat, index),
        GetData = () =>
        {
            var iStream = streamData();
            var ptr = Marshal.GetComInterfaceForObject(iStream, typeof(IStream));
            Marshal.ReleaseComObject(iStream);

            return (ptr, NativeMethods.HResult.Ok);
        },
    });

    public void SetFileDescriptors(IEnumerable<FileDescriptor> fileDescriptors, bool includeContent = true)
    {
        FileGroup group = new(fileDescriptors);
        SelfFileGroup = group;

        UpdateData(AdbDataFormats.FileDescriptor, group.GroupDescriptorBytes);

        if (includeContent)
            UpdateData(AdbDataFormats.FileContents, group.DataStreams);
    }

    public void SetAdbDrag(IEnumerable<FileClass> files, ADBService.AdbDevice device)
    {
        SelfFiles = [.. files];
        NativeMethods.ADBDRAGLIST adbDrag = new(device, files);
        SetData(AdbDataFormats.AdbDrop, adbDrag.Bytes);
    }

    public void SetFileDrop(params IEnumerable<string> files)
        => SetData(AdbDataFormats.FileDrop, new NativeMethods.CFHDROP(files).Bytes);

    public List<FileSyncOperation> Operations { get; private set; }

    private DragDropEffects currentEffect = DragDropEffects.None;
    public DragDropEffects CurrentEffect
    {
        get => currentEffect;
        set
        {
            if (Set(ref currentEffect, value))
                Data.CopyPaste.CurrentDropEffect = value & ~DragDropEffects.Scroll;
        }
    }

    public DragDropEffects? PasteSucceeded
    {
        get => GetDropEffect(AdbDataFormats.PasteSucceeded);
        set => SetData(AdbDataFormats.PasteSucceeded, BitConverter.GetBytes((UInt32)value));
    }

    public DragDropEffects? PerformedDropEffect
    {
        get => GetDropEffect(AdbDataFormats.PerformedDropEffect);
        set => SetData(AdbDataFormats.PerformedDropEffect, BitConverter.GetBytes((UInt32)value));
    }

    public DragDropEffects? PreferredDropEffect
    {
        get => GetDropEffect(AdbDataFormats.PreferredDropEffect);
        set => UpdateData(AdbDataFormats.PreferredDropEffect, BitConverter.GetBytes((UInt32)value));
    }

    public static DragDropEffects GetPreferredDropEffect(System.Windows.IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(AdbDataFormats.PreferredDropEffect)
            || dataObject.GetData(AdbDataFormats.PreferredDropEffect) is not MemoryStream stream)
            return DragDropEffects.None;

        return (DragDropEffects)BitConverter.ToInt32(stream.ToArray());
    }

    public static void SetPreferredDropEffect(System.Windows.IDataObject dataObject, DragDropEffects dropEffects)
        => dataObject.SetData(AdbDataFormats.PreferredDropEffect, BitConverter.GetBytes((UInt32)dropEffects));

    /// <summary>
    /// Gets the DragDropEffects value (if any) previously set on the object.
    /// </summary>
    /// <param name="format">Clipboard format.</param>
    /// <returns>DragDropEffects value or null.</returns>
    private DragDropEffects? GetDropEffect(short format)
    {
        // Get the most recent setting
        var dataObject = dataObjects.LastOrDefault(d =>
            format == d.FORMATETC.cfFormat
            && d.FORMATETC is {
                dwAspect: DVASPECT.DVASPECT_CONTENT,
                tymed: TYMED.TYMED_HGLOBAL
            });

        if (dataObject is not null)
        {
            // Read the value and return it
            var result = dataObject.GetData();
            if (result.Item2 is NativeMethods.HResult.Ok)
            {
                var ptr = NativeMethods.MGlobalLock(result.Item1);
                if (IntPtr.Zero != ptr)
                {
                    try
                    {
                        var length = NativeMethods.MGlobalSize(ptr).ToInt32();
                        if (4 == length)
                        {
                            var data = new byte[length];
                            Marshal.Copy(ptr, data, 0, length);
                            return (DragDropEffects)(BitConverter.ToUInt32(data, 0));
                        }
                    }
                    finally
                    {
                        NativeMethods.MGlobalUnlock(result.Item1);
                    }
                }
            }
        }
        return null;
    }

    #region IAsyncOperation Members
    // Explicit interface implementation hides the technical details from users of VirtualFileDataObject.

    /// <summary>
    /// Called by a drop source to specify whether the data object supports asynchronous data extraction.
    /// </summary>
    /// <param name="fDoOpAsync">A Boolean value that is set to VARIANT_TRUE to indicate that an asynchronous operation is supported, or VARIANT_FALSE otherwise.</param>
    void IAsyncOperation.SetAsyncMode(int fDoOpAsync)
    {
        // Synchronous mode is no longer supported
    }

    /// <summary>
    /// Called by a drop target to determine whether the data object supports asynchronous data extraction.
    /// </summary>
    /// <param name="pfIsOpAsync">A Boolean value that is set to VARIANT_TRUE to indicate that an asynchronous operation is supported, or VARIANT_FALSE otherwise.</param>
    void IAsyncOperation.GetAsyncMode(out int pfIsOpAsync)
    {
        // Synchronous mode is no longer supported
        pfIsOpAsync = NativeMethods.VARIANT_TRUE;
    }

    /// <summary>
    /// Called by a drop target to indicate that asynchronous data extraction is starting.
    /// </summary>
    /// <param name="pbcReserved">Reserved. Set this value to NULL.</param>
    void IAsyncOperation.StartOperation(IBindCtx pbcReserved)
    {
        inOperation = true;
    }

    /// <summary>
    /// Called by the drop source to determine whether the target is extracting data asynchronously.
    /// </summary>
    /// <param name="pfInAsyncOp">Set to VARIANT_TRUE if data extraction is being handled asynchronously, or VARIANT_FALSE otherwise.</param>
    void IAsyncOperation.InOperation(out int pfInAsyncOp)
    {
        pfInAsyncOp = inOperation ? NativeMethods.VARIANT_TRUE : NativeMethods.VARIANT_FALSE;
    }

    /// <summary>
    /// Notifies the data object that that asynchronous data extraction has ended.
    /// </summary>
    /// <param name="hResult">An HRESULT value that indicates the outcome of the data extraction. Set to S_OK if successful, or a COM error code otherwise.</param>
    /// <param name="pbcReserved">Reserved. Set to NULL.</param>
    /// <param name="dwEffects">A DROPEFFECT value that indicates the result of an optimized move. This should be the same value that would be passed to the data object as a CFSTR_PERFORMEDDROPEFFECT format with a normal data extraction operation.</param>
    void IAsyncOperation.EndOperation(int hResult, IBindCtx pbcReserved, uint dwEffects)
    {
        inOperation = false;
    }

    #endregion

    /// <summary>
    /// Class representing the result of a SetData call.
    /// </summary>
    private class DataObject
    {
        /// <summary>
        /// FORMATETC structure for the data.
        /// </summary>
        public FORMATETC FORMATETC { get; init; }

        /// <summary>
        /// Func returning the data as an IntPtr and an HRESULT success code.
        /// </summary>
        public Func<(HANDLE, NativeMethods.HResult)> GetData { get; set; }
    }

    public static VirtualFileDataObject PrepareTransfer(IEnumerable<Package> packages,
                                                        DataObjectMethod method = DataObjectMethod.DragDrop)
    {
        Data.FileActions.IsSelectionIllegalOnWindows =
        Data.FileActions.IsSelectionConflictingOnFuse = false;

        CopyPasteService.ClearTempFolder();
        VirtualFileDataObject vfdo = new(DragDropEffects.Copy, method);

        var files = FileHelper.GetFilesFromTree(FileHelper.GetFolderTree(packages.Select(p => p.Path), false)).ToList();
        vfdo.Operations = [.. files.Select(f => f.PrepareDescriptors(vfdo))];
        vfdo.SetFileDescriptors(files.SelectMany(f => f.Descriptors));
        vfdo.SetAdbDrag(files, Data.CurrentADBDevice);

        return vfdo;
    }

    public static VirtualFileDataObject PrepareTransfer(IEnumerable<FileClass> files,
                                                        DragDropEffects preferredEffect = DragDropEffects.Copy,
                                                        DataObjectMethod method = DataObjectMethod.DragDrop)
    {
        CopyPasteService.ClearTempFolder();

        Data.FileActions.IsSelectionIllegalOnWindows = !FileHelper.FileNameLegal(Data.SelectedFiles, FileHelper.RenameTarget.Windows);
        Data.FileActions.IsSelectionConflictingOnFuse = Data.SelectedFiles.Select(f => f.FullName)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .Count() != Data.SelectedFiles.Count();

        VirtualFileDataObject vfdo = new(preferredEffect, method);

        var includeContent =
            !Data.FileActions.IsSelectionIllegalOnWindows
            && !Data.FileActions.IsSelectionConflictingOnFuse
            && !Data.FileActions.IsRecycleBin;

        if (includeContent)
        {
            Data.RuntimeSettings.MainCursor = Cursors.AppStarting;
            Task.Run(() =>
            {
                // Prepare file ops recursively for folders
                return files.Select(f => f.PrepareDescriptors(vfdo)).ToList();
            }).ContinueWith(t =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    vfdo.SetFileDescriptors(files.SelectMany(f => f.Descriptors));
                    Data.RuntimeSettings.MainCursor = Cursors.Arrow;
                });

                vfdo.Operations = t.Result;
            });

            // We add these empty formats as placeholders, the data will be replaced once it is ready.
            // This is done even when no folders are selected and we have all files beforehand.
            // When sending data to the clipboard, if all data is available immediately,
            // File Explorer will read the file contents to memory as soon as they appear in the clipboard.
            vfdo.SetData(AdbDataFormats.FileDescriptor, []);
            vfdo.SetData(AdbDataFormats.FileContents, []);
            SelfFileGroup = new([]);
        }
        else // When the selection is illegal for Windows
        {
            // Next we provide the real file descriptors and file contents.
            // File Explorer isn't supposed to use them, but since it's already implemented,
            // might as well leave it for any other app to use.
            files.ForEach(f => f.PrepareDescriptors(vfdo, false));
            vfdo.SetFileDescriptors(files.SelectMany(f => f.Descriptors), false);
        }

        // Finally we provide the ADB drag data, which only we recongize
        vfdo.SetAdbDrag(files, Data.CurrentADBDevice);

        return vfdo;
    }

    public enum DataObjectMethod
    {
        DragDrop,
        Clipboard,
    }

    public void SendObjectToShell(DataObjectMethod method, DependencyObject dragSource = null, DragDropEffects allowedEffects = DragDropEffects.None)
    {
        Method = method;

        try
        {
            if (method is DataObjectMethod.DragDrop)
            {
                DoDragDrop(dragSource, allowedEffects);
            }
            else if (method is DataObjectMethod.Clipboard)
            {
                CurrentEffect = allowedEffects;
                PerformedDropEffect = allowedEffects;
                Clipboard.SetDataObject(this);
            }
            else
                throw new NotSupportedException();
        }
        catch (COMException)
        {
            // Failure; no way to recover
        }
    }

    /// <summary>
    /// Initiates a drag-and-drop operation.
    /// </summary>
    /// <param name="dragSource">A reference to the dependency object that is the source of the data being dragged.</param>
    /// <param name="allowedEffects">One of the DragDropEffects values that specifies permitted effects of the drag-and-drop operation.</param>
    /// <returns>One of the DragDropEffects values that specifies the final effect that was performed during the drag-and-drop operation.</returns>
    /// <remarks>
    /// Call this method instead of System.Windows.DragDrop.DoDragDrop because this method handles IDataObject better.
    /// </remarks>
    public void DoDragDrop(DependencyObject dragSource, DragDropEffects allowedEffects)
    {
        Action<DragDropEffects> dragFeedback = value =>
        {
            if (value.HasFlag(DragDropEffects.Move)
                && !Data.RuntimeSettings.DragModifiers.HasFlag(DragDropKeyStates.ShiftKey))
            {
                // Override default since Windows gives Move as default
                value = DragDropEffects.Copy;
            }

            CurrentEffect = value;
        };

        try
        {
            NativeMethods.MDoDragDrop(this, new DropSource(dragFeedback), allowedEffects);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Exception in DoDragDrop: {e.Message}");
#endif
        }
    }

    /// <summary>
    /// Contains the methods for generating visual feedback to the end user and for cancelling or completing the drag-and-drop operation.
    /// </summary>
    private class DropSource(Action<DragDropEffects> onFeedback = null) : NativeMethods.IDropSource
    {
        /// <summary>
        /// Determines whether a drag-and-drop operation should continue.
        /// </summary>
        /// <param name="fEscapePressed">Indicates whether the Esc key has been pressed since the previous call to QueryContinueDrag or to DoDragDrop if this is the first call to QueryContinueDrag. A TRUE value indicates the end user has pressed the escape key; a FALSE value indicates it has not been pressed.</param>
        /// <param name="grfKeyState">The current state of the keyboard modifier keys on the keyboard. Possible values can be a combination of any of the flags MK_CONTROL, MK_SHIFT, MK_ALT, MK_BUTTON, MK_LBUTTON, MK_MBUTTON, and MK_RBUTTON.</param>
        /// <returns>This method returns S_OK/DRAGDROP_S_DROP/DRAGDROP_S_CANCEL on success.</returns>
        public int QueryContinueDrag(int fEscapePressed, uint grfKeyState)
        {
            var escapePressed = (0 != fEscapePressed);
            Data.RuntimeSettings.DragModifiers = (DragDropKeyStates)grfKeyState;
            
            var res = escapePressed switch
            {
                true => NativeMethods.HResult.DRAGDROP_S_CANCEL,
                false when Data.RuntimeSettings.DragModifiers.HasFlag(DragDropKeyStates.RightMouseButton) => NativeMethods.HResult.DRAGDROP_S_CANCEL,
                false when !Data.RuntimeSettings.DragModifiers.HasFlag(DragDropKeyStates.LeftMouseButton) => NativeMethods.HResult.DRAGDROP_S_DROP,
                _ => NativeMethods.HResult.Ok,
            };

            if (res is not NativeMethods.HResult.Ok)
            {
                Data.RuntimeSettings.DragBitmap = null;
                Data.CopyPaste.DragStatus = CopyPasteService.DragState.None;
                IpcService.NotifyDropCancel(res);
            }
            Data.CopyPaste.DragResult = res;

            return (int)res;
        }

        /// <summary>
        /// Gives visual feedback to an end user during a drag-and-drop operation.
        /// </summary>
        /// <param name="dwEffect">The DROPEFFECT value returned by the most recent call to IDropTarget::DragEnter, IDropTarget::DragOver, or IDropTarget::DragLeave. </param>
        public int GiveFeedback(uint dwEffect)
        {
            var dragDropEffects = (DragDropEffects)dwEffect & ~DragDropEffects.Scroll;
            onFeedback?.Invoke(dragDropEffects);

#if !DEPLOY
            DebugLog.PrintLine($"GiveFeedback dwEffect: {dragDropEffects}");
#endif

            if (dragDropEffects is DragDropEffects.None)
                return (int)NativeMethods.HResult.DRAGDROP_S_USEDEFAULTCURSORS;

            // Set default cursor when cursor control is manual
            Mouse.SetCursor(Cursors.Arrow);
            return (int)NativeMethods.HResult.Ok;
        }
    }

    public static IStream GetFileContents(System.Windows.IDataObject dataObject, int index)
        => GetFileContents((System.Runtime.InteropServices.ComTypes.IDataObject)dataObject, index);

    public static IStream GetFileContents(System.Runtime.InteropServices.ComTypes.IDataObject dataObject, int index)
    {
        var fmtEtc = CreateFormat(AdbDataFormats.FileContents, index);

        dataObject.GetData(ref fmtEtc, out STGMEDIUM medium);

        var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
        return stream;
    }
}
