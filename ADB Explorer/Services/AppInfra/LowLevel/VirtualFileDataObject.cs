// Virtual File Data Object library by David Anson
// https://dlaa.me/blog/post/9913083
// Used and modified under the MIT license

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using System.Runtime.InteropServices.ComTypes;

namespace ADB_Explorer.Services;

/// <summary>
/// Handles everything related to Shell Drag & Drop (including clipboard) 
/// </summary>
public sealed class VirtualFileDataObject : ViewModelBase, System.Runtime.InteropServices.ComTypes.IDataObject, IAsyncOperation
{
    /// <summary>
    /// Gets or sets a value indicating whether the data object can be used asynchronously.
    /// </summary>
    public bool IsAsynchronous { get; set; }

    /// <summary>
    /// In-order list of registered data objects.
    /// </summary>
    private readonly List<DataObject> _dataObjects = [];

    /// <summary>
    /// Tracks whether an asynchronous operation is ongoing.
    /// </summary>
    private bool _inOperation;

    /// <summary>
    /// Stores the user-specified start action.
    /// </summary>
    private readonly Action<VirtualFileDataObject> _startAction;

    /// <summary>
    /// Stores the user-specified end action.
    /// </summary>
    private readonly Action<VirtualFileDataObject> _endAction;

    /// <summary>
    /// Initializes a new instance of the VirtualFileDataObject class.
    /// </summary>
    /// <param name="startAction">Optional action to run at the start of the data transfer.</param>
    /// <param name="endAction">Optional action to run at the end of the data transfer.</param>
    public VirtualFileDataObject(Action<VirtualFileDataObject> startAction, Action<VirtualFileDataObject> endAction, DragDropEffects preferredDropEffect = DragDropEffects.All)
    {
        IsAsynchronous = true;

        _startAction = startAction;
        _endAction = endAction;

        PreferredDropEffect = preferredDropEffect;
    }

    public VirtualFileDataObject() : this(_ => { }, _ => { })
    {
        // Usually this would have Dispatcher.BeginInvoke() in each of the actions
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

        if (0 == _dataObjects.Count)
        {
            // Note: SHCreateStdEnumFmtEtc fails for a count of 0; throw helpful exception
            throw new InvalidOperationException("VirtualFileDataObject requires at least one data object to enumerate.");
        }

        // Create enumerator and return it
        var res = NativeMethods.SHCreateStdEnumFmtEtc(_dataObjects.Count, _dataObjects.Select(d => d.FORMATETC), out IEnumFORMATETC enumerator);
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
        AdbDataFormat adbDataFormat = null;
        var formatCopy = format;
        medium = new();

        var hr = (NativeMethods.HResult)((System.Runtime.InteropServices.ComTypes.IDataObject)this).QueryGetData(ref format);

#if !DEPLOY
        if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
        {
            adbDataFormat = AdbDataFormats.GetFormat(formatCopy.cfFormat);
            if (adbDataFormat is not null)
                File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | Query: {adbDataFormat.Name}\n");
        }
#endif

        if (hr is NativeMethods.HResult.Ok)
        {
            // Find the best match
            var dataObject = _dataObjects.FirstOrDefault(d =>
                    d.FORMATETC.cfFormat == formatCopy.cfFormat
                    && d.FORMATETC.dwAspect == formatCopy.dwAspect
                    && 0 != (d.FORMATETC.tymed & formatCopy.tymed)
                    && d.FORMATETC.lindex == formatCopy.lindex);
            if (dataObject != null)
            {
                if (!IsAsynchronous && dataObject.FORMATETC.cfFormat == AdbDataFormats.FileDescriptor && !_inOperation)
                {
                    // Enter the operation and call the start action
                    _inOperation = true;
                    _startAction?.Invoke(this);
                }

#if !DEPLOY
                if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
                    File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | Get data: {adbDataFormat?.Name}\n");
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
            var formatMatches = _dataObjects.Where(d => d.FORMATETC.cfFormat == formatCopy.cfFormat);
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
                    SetData(formatIn.cfFormat, data);
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

        // Handle synchronous mode
        if (!IsAsynchronous && (formatIn.cfFormat == AdbDataFormats.PerformedDropEffect) && _inOperation)
        {
            // Call the end action and exit the operation
            _endAction?.Invoke(this);
            _inOperation = false;
        }

        // Throw if unhandled
        if (!handled)
        {
            throw new NotImplementedException();
        }
    }

#endregion

    public string[] GetFormats()
        => [.. _dataObjects.Select(o => AdbDataFormats.GetFormatName(o.FORMATETC.cfFormat))];

    /// <summary>
    /// Creates a format on HGlobal, or as a stream if index is provided
    /// </summary>
    public static FORMATETC CreateFormat(short dataFormat, int index = -1) => new()
    {
        cfFormat = dataFormat,
        ptd = IntPtr.Zero,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = index,
        tymed = index == -1
            ? TYMED.TYMED_HGLOBAL
            : TYMED.TYMED_ISTREAM,
    };

    public void UpdateData(short dataFormat, IEnumerable<byte> data)
    {
        var query = _dataObjects.Where(dataObject => dataObject.FORMATETC.cfFormat == dataFormat);
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
    public void SetData(short dataFormat, IEnumerable<byte> data)
        => _dataObjects.Add(new()
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

    public void UpdateData(short dataFormat, IEnumerable<Action<Stream>> dataStreams)
    {
        // Remove all previous streams
        _dataObjects.RemoveAll(d => d.FORMATETC.cfFormat == dataFormat);

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
    public void SetData(short dataFormat, int index, Action<Stream> streamData)
        => _dataObjects.Add(new()
    {
        FORMATETC = CreateFormat(dataFormat, index),
        GetData = () =>
        {
            // Create IStream for data
            var ptr = IntPtr.Zero;
            var iStream = NativeMethods.MCreateStreamOnHGlobal(IntPtr.Zero, true);
            if (streamData != null)
            {
                // Wrap in a .NET-friendly Stream and call provided code to fill it
                using var stream = new IStreamWrapper(iStream);
                streamData(stream);
            }
            // Return an IntPtr for the IStream
            ptr = Marshal.GetComInterfaceForObject(iStream, typeof(IStream));
            Marshal.ReleaseComObject(iStream);
            return (ptr, NativeMethods.HResult.Ok);
        },
    });

    /// <summary>
    /// Provides data for the specified data format (FILEGROUPDESCRIPTOR/FILEDESCRIPTOR)
    /// </summary>
    /// <param name="fileDescriptors">Collection of virtual files.</param>
    public void SetData(IEnumerable<FileDescriptor> fileDescriptors, bool includeContent = true)
    {
        FileGroup group = new(fileDescriptors);

        UpdateData(AdbDataFormats.FileDescriptor, group.GroupDescriptorBytes);

        if (includeContent)
            UpdateData(AdbDataFormats.FileContents, group.DataStreams);
    }

    public void SetAdbDrag(IEnumerable<FileClass> files, ADBService.AdbDevice device)
    {
        NativeMethods.ADBDRAGLIST adbDrag = new(device, files);
        SetData(AdbDataFormats.AdbDrop, adbDrag.Bytes);
    }
    
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
        var dataObject = _dataObjects.LastOrDefault(d =>
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
        IsAsynchronous = NativeMethods.VARIANT_FALSE != fDoOpAsync;
    }

    /// <summary>
    /// Called by a drop target to determine whether the data object supports asynchronous data extraction.
    /// </summary>
    /// <param name="pfIsOpAsync">A Boolean value that is set to VARIANT_TRUE to indicate that an asynchronous operation is supported, or VARIANT_FALSE otherwise.</param>
    void IAsyncOperation.GetAsyncMode(out int pfIsOpAsync)
    {
        pfIsOpAsync = IsAsynchronous ? NativeMethods.VARIANT_TRUE : NativeMethods.VARIANT_FALSE;
    }

    /// <summary>
    /// Called by a drop target to indicate that asynchronous data extraction is starting.
    /// </summary>
    /// <param name="pbcReserved">Reserved. Set this value to NULL.</param>
    void IAsyncOperation.StartOperation(IBindCtx pbcReserved)
    {
        _inOperation = true;
        _startAction?.Invoke(this);
    }

    /// <summary>
    /// Called by the drop source to determine whether the target is extracting data asynchronously.
    /// </summary>
    /// <param name="pfInAsyncOp">Set to VARIANT_TRUE if data extraction is being handled asynchronously, or VARIANT_FALSE otherwise.</param>
    void IAsyncOperation.InOperation(out int pfInAsyncOp)
    {
        pfInAsyncOp = _inOperation ? NativeMethods.VARIANT_TRUE : NativeMethods.VARIANT_FALSE;
    }

    /// <summary>
    /// Notifies the data object that that asynchronous data extraction has ended.
    /// </summary>
    /// <param name="hResult">An HRESULT value that indicates the outcome of the data extraction. Set to S_OK if successful, or a COM error code otherwise.</param>
    /// <param name="pbcReserved">Reserved. Set to NULL.</param>
    /// <param name="dwEffects">A DROPEFFECT value that indicates the result of an optimized move. This should be the same value that would be passed to the data object as a CFSTR_PERFORMEDDROPEFFECT format with a normal data extraction operation.</param>
    void IAsyncOperation.EndOperation(int hResult, IBindCtx pbcReserved, uint dwEffects)
    {
        _endAction?.Invoke(this);
        _inOperation = false;
    }

    #endregion

    public class FileGroup
    {
        private readonly IEnumerable<FileDescriptor> FileDescriptors;

        private NativeMethods.FILEGROUPDESCRIPTOR GroupDescriptor;

        public IEnumerable<byte> GroupDescriptorBytes => GroupDescriptor.Bytes;

        public IEnumerable<Action<Stream>> DataStreams => FileDescriptors.Select(f => f.StreamContents);

        public FileGroup(IEnumerable<FileDescriptor> fileDescriptors)
        {
            FileDescriptors = fileDescriptors;

            GroupDescriptor = new(FileDescriptors);
        }
    }

    /// <summary>
    /// Class representing a virtual file for use by drag/drop or the clipboard.
    /// </summary>
    public class FileDescriptor
    {
        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the (optional) length of the file.
        /// </summary>
        public Int64? Length { get; set; }

        /// <summary>
        /// Gets or sets the (optional) change time of the file.
        /// </summary>
        public DateTime? ChangeTimeUtc { get; set; }

        /// <summary>
        /// Gets or sets an Action that returns the contents of the file.
        /// </summary>
        public Action<Stream> StreamContents { get; set; }

        public bool IsDirectory { get; set; }

        public string SourcePath { get; set; }
    }

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

    /// <summary>
    /// Simple class that exposes a write-only IStream as a Stream.
    /// </summary>
    /// <param name="iStream">IStream instance to wrap.</param>
    private class IStreamWrapper(IStream iStream) : Stream
    {
        /// <summary>
        /// IStream instance being wrapped.
        /// </summary>
        private readonly IStream _iStream = iStream;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length => throw new NotImplementedException();

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _iStream.Write(buffer[offset..], count, IntPtr.Zero);
        }
    }

    public static VirtualFileDataObject PrepareTransfer(IEnumerable<FileClass> files, DragDropEffects preferredEffect = DragDropEffects.Copy)
    {
        try
        {
            Directory.Delete(Data.RuntimeSettings.TempDragPath, true);
        }
        catch
        { }

        Directory.CreateDirectory(Data.RuntimeSettings.TempDragPath);

        Data.FileActions.IsSelectionIllegalOnWindows = !FileHelper.FileNameLegal(Data.SelectedFiles, FileHelper.RenameTarget.Windows);
        Data.FileActions.IsSelectionConflictingOnFuse = Data.SelectedFiles.Select(f => f.FullName)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .Count() != Data.SelectedFiles.Count();

        VirtualFileDataObject vfdo = new(_ => { }, _ => { }, preferredEffect);

        if (!Data.FileActions.IsSelectionIllegalOnWindows
            && !Data.FileActions.IsSelectionConflictingOnFuse
            && !Data.FileActions.IsRecycleBin)
        {
            Data.RuntimeSettings.MainCursor = Cursors.AppStarting;
            Task.Run(() =>
            {
                return files.Select(f => f.PrepareDescriptors(vfdo)).ToList();
            }).ContinueWith(t =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    vfdo.SetData(files.SelectMany(f => f.Descriptors));
                    Data.RuntimeSettings.MainCursor = Cursors.Arrow;
                });

                vfdo.Operations = t.Result;
            });

            // Add these empty formats as placeholders, the data will be replaced once it is ready.
            // This is done even when no folders are selected and we have all files beforehand.
            // When sending data to the clipboard, if all data is available immediately,
            // File Explorer will read the file contents to memory as soon as they appear in the clipboard.
            vfdo.SetData(AdbDataFormats.FileDescriptor, []);
            vfdo.SetData(AdbDataFormats.FileContents, []);
        }
        else
        {
            files.ForEach(f => f.PrepareDescriptors(vfdo, false));
            vfdo.SetData(files.SelectMany(f => f.Descriptors), false);
        }

        vfdo.SetAdbDrag(files, Data.CurrentADBDevice);

        return vfdo;
    }

    public enum SendMethod
    {
        DragDrop,
        Clipboard,
    }

    public void SendObjectToShell(SendMethod method, DependencyObject dragSource = null, DragDropEffects allowedEffects = DragDropEffects.None)
    {
        try
        {
            if (method is SendMethod.DragDrop)
                DoDragDrop(dragSource, allowedEffects);
            else if (method is SendMethod.Clipboard)
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
            if (PreferredDropEffect is not null
                && PreferredDropEffect.Value.HasFlag(DragDropEffects.Move)
                && PreferredDropEffect.Value.HasFlag(DragDropEffects.Copy))
            {
                if (value == DragDropEffects.Move
                    && !Data.RuntimeSettings.DragModifiers.HasFlag(DragDropKeyStates.ShiftKey))
                {
                    // Override default since Windows gives Move as default
                    CurrentEffect = DragDropEffects.Copy;
                    return;
                }
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
            if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
                File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | Exception in DoDragDrop: {e.Message}\n");
#endif
        }
    }

    public static void NotifyDropCancel()
        {
        if (!Data.RuntimeSettings.DragWithinSlave)
            return;

        var message = Enum.GetName(CopyPasteService.IpcMessage.DragCanceled) + '|';

        NativeMethods.COPYDATASTRUCT cds = new()
            {
            dwData = IntPtr.Zero,
            cbData = Encoding.ASCII.GetByteCount(message) + 1,
            lpData = message
        };

        NativeMethods.SendMessage(NativeMethods.InterceptMouse.WindowUnderMouse, NativeMethods.WindowsMessages.WM_COPYDATA, ref cds);
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
                false when !Data.RuntimeSettings.DragModifiers.HasFlag(DragDropKeyStates.LeftMouseButton) => NativeMethods.HResult.DRAGDROP_S_DROP,
                _ => NativeMethods.HResult.Ok,
            };

            if (res is not NativeMethods.HResult.Ok)
            {
                Data.RuntimeSettings.DragBitmap = null;
                Data.CopyPaste.DragStatus = CopyPasteService.DragState.None;
                NotifyDropCancel();
            }

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
            if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
                File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | GiveFeedback dwEffect: {dragDropEffects}\n");
#endif

            if (dragDropEffects is DragDropEffects.None)
                return (int)NativeMethods.HResult.DRAGDROP_S_USEDEFAULTCURSORS;

            // Set default cursor when cursor control is manual
            Mouse.SetCursor(Cursors.Arrow);
            return (int)NativeMethods.HResult.Ok;
        }
    }
}
