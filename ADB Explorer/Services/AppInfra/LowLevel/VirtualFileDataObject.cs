// Virtual File Data Object library by David Anson
// https://dlaa.me/blog/post/9913083
// Used and modified under the MIT license

using ADB_Explorer.Models;
using System.Runtime.InteropServices.ComTypes;

namespace ADB_Explorer.Services;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

using HANDLE = IntPtr;

public sealed class VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject, IAsyncOperation
{
    /// <summary>
    /// Gets or sets a value indicating whether the data object can be used asynchronously.
    /// </summary>
    public bool IsAsynchronous { get; set; }

    /// <summary>
    /// Identifier for CFSTR_FILECONTENTS.
    /// </summary>
    private static readonly short FILECONTENTS = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_FILECONTENTS).Id);

    /// <summary>
    /// Identifier for CFSTR_FILEDESCRIPTORW.
    /// </summary>
    private static readonly short FILEDESCRIPTORW = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_FILEDESCRIPTORW).Id);

    /// <summary>
    /// Identifier for CFSTR_PASTESUCCEEDED.
    /// </summary>
    private static readonly short PASTESUCCEEDED = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_PASTESUCCEEDED).Id);

    /// <summary>
    /// Identifier for CFSTR_PERFORMEDDROPEFFECT.
    /// </summary>
    private static readonly short PERFORMEDDROPEFFECT = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_PERFORMEDDROPEFFECT).Id);

    /// <summary>
    /// Identifier for CFSTR_PREFERREDDROPEFFECT.
    /// </summary>
    private static readonly short PREFERREDDROPEFFECT = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_PREFERREDDROPEFFECT).Id);

    /// <summary>
    /// In-order list of registered data objects.
    /// </summary>
    private readonly List<DataObject> _dataObjects = new List<DataObject>();

    /// <summary>
    /// Tracks whether an asynchronous operation is ongoing.
    /// </summary>
    private bool _inOperation;

    /// <summary>
    /// Stores the user-specified start action.
    /// </summary>
    public readonly Action<VirtualFileDataObject> _startAction;

    /// <summary>
    /// Stores the user-specified end action.
    /// </summary>
    private readonly Action<VirtualFileDataObject> _endAction;

    /// <summary>
    /// Initializes a new instance of the VirtualFileDataObject class.
    /// </summary>
    /// <param name="startAction">Optional action to run at the start of the data transfer.</param>
    /// <param name="endAction">Optional action to run at the end of the data transfer.</param>
    public VirtualFileDataObject(Action<VirtualFileDataObject> startAction, Action<VirtualFileDataObject> endAction)
    {
        IsAsynchronous = true;

        _startAction = startAction;
        _endAction = endAction;
    }

    public VirtualFileDataObject() : this((vfdo) => { }, (vfdo) => { })
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
        Marshal.ThrowExceptionForHR(NativeMethods.OLE_E_ADVISENOTSUPPORTED);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Destroys a notification connection that had been previously established.
    /// </summary>
    /// <param name="connection">A DWORD token that specifies the connection to remove.</param>
    void System.Runtime.InteropServices.ComTypes.IDataObject.DUnadvise(int connection)
    {
        Marshal.ThrowExceptionForHR(NativeMethods.OLE_E_ADVISENOTSUPPORTED);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates an object that can be used to enumerate the current advisory connections.
    /// </summary>
    /// <param name="enumAdvise">When this method returns, contains an IEnumSTATDATA that receives the interface pointer to the new enumerator object.</param>
    /// <returns>HRESULT success code.</returns>
    int System.Runtime.InteropServices.ComTypes.IDataObject.EnumDAdvise(out IEnumSTATDATA enumAdvise)
    {
        Marshal.ThrowExceptionForHR(NativeMethods.OLE_E_ADVISENOTSUPPORTED);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates an object for enumerating the FORMATETC structures for a data object.
    /// </summary>
    /// <param name="direction">One of the DATADIR values that specifies the direction of the data.</param>
    /// <returns>IEnumFORMATETC interface.</returns>
    IEnumFORMATETC System.Runtime.InteropServices.ComTypes.IDataObject.EnumFormatEtc(DATADIR direction)
    {
        if (direction == DATADIR.DATADIR_GET)
        {
            if (0 == _dataObjects.Count)
            {
                // Note: SHCreateStdEnumFmtEtc fails for a count of 0; throw helpful exception
                throw new InvalidOperationException("VirtualFileDataObject requires at least one data object to enumerate.");
            }

            // Create enumerator and return it
            if (NativeMethods.SUCCEEDED(NativeMethods.SHCreateStdEnumFmtEtc((uint)(_dataObjects.Count), _dataObjects.Select(d => d.FORMATETC).ToArray(), out IEnumFORMATETC enumerator)))
            {
                return enumerator;
            }

            // Returning null here can cause an AV in the caller; throw instead
            Marshal.ThrowExceptionForHR(NativeMethods.E_FAIL);
        }
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
        medium = new STGMEDIUM();
        var hr = ((System.Runtime.InteropServices.ComTypes.IDataObject)this).QueryGetData(ref format);
        if (NativeMethods.SUCCEEDED(hr))
        {
            // Find the best match
            var formatCopy = format; // Cannot use ref or out parameter inside an anonymous method, lambda expression, or query expression
            var dataObject = _dataObjects.FirstOrDefault(d =>
                    (d.FORMATETC.cfFormat == formatCopy.cfFormat)
                    && (d.FORMATETC.dwAspect == formatCopy.dwAspect)
                    && 0 != (d.FORMATETC.tymed & formatCopy.tymed)
                    && (d.FORMATETC.lindex == formatCopy.lindex));
            if (dataObject != null)
            {
                if (!IsAsynchronous && (FILEDESCRIPTORW == dataObject.FORMATETC.cfFormat) && !_inOperation)
                {
                    // Enter the operation and call the start action
                    _inOperation = true;
                    _startAction?.Invoke(this);
                }

                // Populate the STGMEDIUM
                medium.tymed = dataObject.FORMATETC.tymed;
                var result = dataObject.GetData(); // Possible call to user code
                hr = result.Item2;
                if (NativeMethods.SUCCEEDED(hr))
                {
                    medium.unionmember = result.Item1;
                }
            }
            else
            {
                // Couldn't find a match
                hr = NativeMethods.DV_E_FORMATETC;
            }
        }
        if (!NativeMethods.SUCCEEDED(hr)) // Not redundant; hr gets updated in the block above
        {
            Marshal.ThrowExceptionForHR(hr);
        }
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
        var formatCopy = format; // Cannot use ref or out parameter inside an anonymous method, lambda expression, or query expression
        var formatMatches = _dataObjects.Where(d => d.FORMATETC.cfFormat == formatCopy.cfFormat);
        if (!formatMatches.Any())
        {
            return NativeMethods.DV_E_FORMATETC;
        }
        var tymedMatches = formatMatches.Where(d => 0 != (d.FORMATETC.tymed & formatCopy.tymed));
        if (!tymedMatches.Any())
        {
            return NativeMethods.DV_E_TYMED;
        }
        var aspectMatches = tymedMatches.Where(d => d.FORMATETC.dwAspect == formatCopy.dwAspect);
        if (!aspectMatches.Any())
        {
            return NativeMethods.DV_E_DVASPECT;
        }
        return NativeMethods.S_OK;
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
        if ((formatIn.dwAspect == DVASPECT.DVASPECT_CONTENT) &&
            (formatIn.tymed == TYMED.TYMED_HGLOBAL) &&
            (medium.tymed == formatIn.tymed))
        {
            // Supported format; capture the data
            var ptr = NativeMethods.GlobalLock(medium.unionmember);
            if (IntPtr.Zero != ptr)
            {
                try
                {
                    var length = NativeMethods.GlobalSize(ptr).ToInt32();
                    var data = new byte[length];
                    Marshal.Copy(ptr, data, 0, length);
                    // Store it in our own format
                    SetData(formatIn.cfFormat, data);
                    handled = true;
                }
                finally
                {
                    NativeMethods.GlobalUnlock(medium.unionmember);
                }
            }

            // Release memory if we now own it
            if (release)
            {
                Marshal.FreeHGlobal(medium.unionmember);
            }
        }

        // Handle synchronous mode
        if (!IsAsynchronous && (PERFORMEDDROPEFFECT == formatIn.cfFormat) && _inOperation)
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
            return (ptr, NativeMethods.S_OK);
        };
    }

    /// <summary>
    /// Provides data for the specified data format (HGLOBAL).
    /// </summary>
    /// <param name="dataFormat">Data format.</param>
    /// <param name="data">Sequence of data.</param>
    public void SetData(short dataFormat, IEnumerable<byte> data)
    {
        _dataObjects.Add(
            new DataObject
            {
                FORMATETC = new FORMATETC
                {
                    cfFormat = dataFormat,
                    ptd = IntPtr.Zero,
                    dwAspect = DVASPECT.DVASPECT_CONTENT,
                    lindex = -1,
                    tymed = TYMED.TYMED_HGLOBAL
                },
                GetData = () =>
                {
                    var dataArray = data.ToArray();
                    var ptr = Marshal.AllocHGlobal(dataArray.Length);
                    Marshal.Copy(dataArray, 0, ptr, dataArray.Length);
                    return (ptr, NativeMethods.S_OK);
                },
            });
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
    {
        _dataObjects.Add(
            new DataObject
            {
                FORMATETC = new FORMATETC
                {
                    cfFormat = dataFormat,
                    ptd = IntPtr.Zero,
                    dwAspect = DVASPECT.DVASPECT_CONTENT,
                    lindex = index,
                    tymed = TYMED.TYMED_ISTREAM
                },
                GetData = () =>
                {
                    // Create IStream for data
                    var ptr = IntPtr.Zero;
                    var iStream = NativeMethods.CreateStreamOnHGlobal(IntPtr.Zero, true);
                    if (streamData != null)
                    {
                        // Wrap in a .NET-friendly Stream and call provided code to fill it
                        using (var stream = new IStreamWrapper(iStream))
                        {
                            streamData(stream);
                        }
                    }
                    // Return an IntPtr for the IStream
                    ptr = Marshal.GetComInterfaceForObject(iStream, typeof(IStream));
                    Marshal.ReleaseComObject(iStream);
                    return (ptr, NativeMethods.S_OK);
                },
            });
    }

    /// <summary>
    /// Provides data for the specified data format (FILEGROUPDESCRIPTOR/FILEDESCRIPTOR)
    /// </summary>
    /// <param name="fileDescriptors">Collection of virtual files.</param>
    public void SetData(IEnumerable<FileDescriptor> fileDescriptors)
    {
        // Prepare buffer
        List<byte> bytes = new();

        // Add FILEGROUPDESCRIPTOR header
        bytes.AddRange(StructureBytes(new NativeMethods.FILEGROUPDESCRIPTOR { cItems = (uint)fileDescriptors.Count() }));

        // Add n FILEDESCRIPTORs
        foreach (var fileDescriptor in fileDescriptors)
        {
            // Set required fields
            var FILEDESCRIPTOR = new NativeMethods.FILEDESCRIPTOR
            {
                cFileName = fileDescriptor.Name,
            };

            // Set optional directory flag
            if (fileDescriptor.IsDirectory)
            {
                FILEDESCRIPTOR.dwFlags |= NativeMethods.FD_FLAGS.FD_ATTRIBUTES;
                FILEDESCRIPTOR.dwFileAttributes |= NativeMethods.FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
            }

            // Set optional timestamp
            if (fileDescriptor.ChangeTimeUtc.HasValue)
            {
                FILEDESCRIPTOR.dwFlags |= NativeMethods.FD_FLAGS.FD_CREATETIME | NativeMethods.FD_FLAGS.FD_WRITESTIME;
                var changeTime = fileDescriptor.ChangeTimeUtc.Value.ToLocalTime().ToFileTime();
                var changeTimeFileTime = new FILETIME
                {
                    dwLowDateTime = (int)(changeTime & 0xffffffff),
                    dwHighDateTime = (int)(changeTime >> 32),
                };
                FILEDESCRIPTOR.ftLastWriteTime = changeTimeFileTime;
                FILEDESCRIPTOR.ftCreationTime = changeTimeFileTime;
            }

            // Set optional length
            if (fileDescriptor.Length.HasValue)
            {
                FILEDESCRIPTOR.dwFlags |= NativeMethods.FD_FLAGS.FD_FILESIZE;
                FILEDESCRIPTOR.nFileSizeLow = (uint)(fileDescriptor.Length & 0xffffffff);
                FILEDESCRIPTOR.nFileSizeHigh = (uint)(fileDescriptor.Length >> 32);
            }

            // Add structure to buffer
            bytes.AddRange(StructureBytes(FILEDESCRIPTOR));
        }

        // Update file descriptors to preserve the pointer if already set
        UpdateData(FILEDESCRIPTORW, bytes);

        // Remove all previous streams
        _dataObjects.RemoveAll(d => d.FORMATETC.cfFormat == FILECONTENTS);

        // Set n CFSTR_FILECONTENTS
        var index = 0;
        foreach (var fileDescriptor in fileDescriptors)
        {
            SetData(FILECONTENTS, index, fileDescriptor.StreamContents);
            index++;
        }
    }

    /// <summary>
    /// Gets or sets the CFSTR_PASTESUCCEEDED value for the object.
    /// </summary>
    public DragDropEffects? PasteSucceeded
    {
        get => GetDropEffect(PASTESUCCEEDED);
        set => SetData(PASTESUCCEEDED, BitConverter.GetBytes((UInt32)value));
    }

    /// <summary>
    /// Gets or sets the CFSTR_PERFORMEDDROPEFFECT value for the object.
    /// </summary>
    public DragDropEffects? PerformedDropEffect
    {
        get => GetDropEffect(PERFORMEDDROPEFFECT);
        set => SetData(PERFORMEDDROPEFFECT, BitConverter.GetBytes((UInt32)value));
    }

    /// <summary>
    /// Gets or sets the CFSTR_PREFERREDDROPEFFECT value for the object.
    /// </summary>
    public DragDropEffects? PreferredDropEffect
    {
        get => GetDropEffect(PREFERREDDROPEFFECT);
        set => SetData(PREFERREDDROPEFFECT, BitConverter.GetBytes((UInt32)value));
    }

    /// <summary>
    /// Gets the DragDropEffects value (if any) previously set on the object.
    /// </summary>
    /// <param name="format">Clipboard format.</param>
    /// <returns>DragDropEffects value or null.</returns>
    private DragDropEffects? GetDropEffect(short format)
    {
        // Get the most recent setting
        var dataObject = _dataObjects
            .Where(d =>
                (format == d.FORMATETC.cfFormat) &&
                (DVASPECT.DVASPECT_CONTENT == d.FORMATETC.dwAspect) &&
                (TYMED.TYMED_HGLOBAL == d.FORMATETC.tymed))
            .LastOrDefault();
        if (null != dataObject)
        {
            // Read the value and return it
            var result = dataObject.GetData();
            if (NativeMethods.SUCCEEDED(result.Item2))
            {
                var ptr = NativeMethods.GlobalLock(result.Item1);
                if (IntPtr.Zero != ptr)
                {
                    try
                    {
                        var length = NativeMethods.GlobalSize(ptr).ToInt32();
                        if (4 == length)
                        {
                            var data = new byte[length];
                            Marshal.Copy(ptr, data, 0, length);
                            return (DragDropEffects)(BitConverter.ToUInt32(data, 0));
                        }
                    }
                    finally
                    {
                        NativeMethods.GlobalUnlock(result.Item1);
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
        IsAsynchronous = !(NativeMethods.VARIANT_FALSE == fDoOpAsync);
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

    /// <summary>
    /// Returns the in-memory representation of an interop structure.
    /// </summary>
    /// <param name="source">Structure to return.</param>
    /// <returns>In-memory representation of structure.</returns>
    private static IEnumerable<byte> StructureBytes(object source)
    {
        // Set up for call to StructureToPtr
        var size = Marshal.SizeOf(source.GetType());
        var ptr = Marshal.AllocHGlobal(size);
        var bytes = new byte[size];
        try
        {
            Marshal.StructureToPtr(source, ptr, false);
            // Copy marshalled bytes to buffer
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return bytes;
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
    }

    /// <summary>
    /// Class representing the result of a SetData call.
    /// </summary>
    private class DataObject
    {
        /// <summary>
        /// FORMATETC structure for the data.
        /// </summary>
        public FORMATETC FORMATETC { get; set; }

        /// <summary>
        /// Func returning the data as an IntPtr and an HRESULT success code.
        /// </summary>
        public Func<(HANDLE, int)> GetData { get; set; }
    }

    /// <summary>
    /// Simple class that exposes a write-only IStream as a Stream.
    /// </summary>
    private class IStreamWrapper : Stream
    {
        /// <summary>
        /// IStream instance being wrapped.
        /// </summary>
        private readonly IStream _iStream;

        /// <summary>
        /// Initializes a new instance of the IStreamWrapper class.
        /// </summary>
        /// <param name="iStream">IStream instance to wrap.</param>
        public IStreamWrapper(IStream iStream)
        {
            _iStream = iStream;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override long Position
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
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

    public enum SendMethod
    {
        DragDrop,
        Clipboard,
    }

    public static void SendObjectToSystem(VirtualFileDataObject vfdo, SendMethod method, DependencyObject dragSource = null, DragDropEffects allowedEffects = DragDropEffects.None)
    {

#if DEBUG
        Trace.WriteLine("Sending VFDO to the system is not allowed in DEBUG (DragDrop / Clipboard)");
        return;

#elif RELEASE

        try
        {
            if (method == SendMethod.DragDrop)
                DoDragDrop(dragSource, vfdo, allowedEffects);
            else if (method == SendMethod.Clipboard)
                Clipboard.SetDataObject(vfdo);
            else
                throw new NotSupportedException();
        }
        catch (COMException)
        {
            // Failure; no way to recover
        }

#endif
    }

    public static VirtualFileDataObject PrepareTransfer(IEnumerable<FileClass> files)
    {
        if (files.Any(f => f.Descriptors is null))
            return null;

        try
        {
            Directory.Delete(Data.RuntimeSettings.TempDragPath, true);
        }
        catch
        { }

        Directory.CreateDirectory(Data.RuntimeSettings.TempDragPath);

        VirtualFileDataObject vfdo = new() { PreferredDropEffect = DragDropEffects.Copy };

        vfdo.SetData(files.SelectMany(f => f.Descriptors));

        return vfdo;
    }

    /// <summary>
    /// Initiates a drag-and-drop operation.
    /// </summary>
    /// <param name="dragSource">A reference to the dependency object that is the source of the data being dragged.</param>
    /// <param name="dataObject">A data object that contains the data being dragged.</param>
    /// <param name="allowedEffects">One of the DragDropEffects values that specifies permitted effects of the drag-and-drop operation.</param>
    /// <returns>One of the DragDropEffects values that specifies the final effect that was performed during the drag-and-drop operation.</returns>
    /// <remarks>
    /// Call this method instead of System.Windows.DragDrop.DoDragDrop because this method handles IDataObject better.
    /// </remarks>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, System.Runtime.InteropServices.ComTypes.IDataObject dataObject, DragDropEffects allowedEffects)
    {
        int[] finalEffect = new int[1];
        try
        {
            NativeMethods.DoDragDrop(dataObject, new DropSource(), (int)allowedEffects, finalEffect);
        }
        finally
        {
            if ((dataObject is VirtualFileDataObject virtualFileDataObject) && !virtualFileDataObject.IsAsynchronous && virtualFileDataObject._inOperation)
            {
                // Call the end action and exit the operation
                virtualFileDataObject._endAction?.Invoke(virtualFileDataObject);
                virtualFileDataObject._inOperation = false;
            }
        }
        return (DragDropEffects)(finalEffect[0]);
    }

    /// <summary>
    /// Contains the methods for generating visual feedback to the end user and for canceling or completing the drag-and-drop operation.
    /// </summary>
    private class DropSource : NativeMethods.IDropSource
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
            var keyStates = (DragDropKeyStates)grfKeyState;
            if (escapePressed)
            {
                return NativeMethods.DRAGDROP_S_CANCEL;
            }
            else if (DragDropKeyStates.None == (keyStates & DragDropKeyStates.LeftMouseButton))
            {
                return NativeMethods.DRAGDROP_S_DROP;
            }
            return NativeMethods.S_OK;
        }

        /// <summary>
        /// Gives visual feedback to an end user during a drag-and-drop operation.
        /// </summary>
        /// <param name="dwEffect">The DROPEFFECT value returned by the most recent call to IDropTarget::DragEnter, IDropTarget::DragOver, or IDropTarget::DragLeave. </param>
        /// <returns>This method returns S_OK on success.</returns>
        public int GiveFeedback(uint dwEffect)
        {
            return NativeMethods.DRAGDROP_S_USEDEFAULTCURSORS;
        }
    }

    /// <summary>
    /// Provides access to Win32-level constants, structures, and functions.
    /// </summary>
    private static class NativeMethods
    {
        public const int DRAGDROP_S_DROP = 0x00040100;
        public const int DRAGDROP_S_CANCEL = 0x00040101;
        public const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
        public const int DV_E_DVASPECT = -2147221397;
        public const int DV_E_FORMATETC = -2147221404;
        public const int DV_E_TYMED = -2147221399;
        public const int E_FAIL = -2147467259;
        public const int OLE_E_ADVISENOTSUPPORTED = -2147221501;
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int VARIANT_FALSE = 0;
        public const int VARIANT_TRUE = -1;

        public const string CFSTR_FILECONTENTS = "FileContents";
        public const string CFSTR_FILEDESCRIPTORW = "FileGroupDescriptorW";
        public const string CFSTR_PASTESUCCEEDED = "Paste Succeeded";
        public const string CFSTR_PERFORMEDDROPEFFECT = "Performed DropEffect";
        public const string CFSTR_PREFERREDDROPEFFECT = "Preferred DropEffect";

        // https://github.com/dahall/Vanara/blob/master/PInvoke/Shell32/Clipboard.cs
        /// <summary>An array of flags that indicate which of the <see cref="FILEDESCRIPTOR"/> structure members contain valid data.</summary>
        [Flags]
        public enum FD_FLAGS : uint
        {
            /// <summary>The <c>clsid</c> member is valid.</summary>
            FD_CLSID = 0x00000001,

            /// <summary>The <c>sizel</c> and <c>pointl</c> members are valid.</summary>
            FD_SIZEPOINT = 0x00000002,

            /// <summary>The <c>dwFileAttributes</c> member is valid.</summary>
            FD_ATTRIBUTES = 0x00000004,

            /// <summary>The <c>ftCreationTime</c> member is valid.</summary>
            FD_CREATETIME = 0x00000008,

            /// <summary>The <c>ftLastAccessTime</c> member is valid.</summary>
            FD_ACCESSTIME = 0x00000010,

            /// <summary>The <c>ftLastWriteTime</c> member is valid.</summary>
            FD_WRITESTIME = 0x00000020,

            /// <summary>The <c>nFileSizeHigh</c> and <c>nFileSizeLow</c> members are valid.</summary>
            FD_FILESIZE = 0x00000040,

            /// <summary>A progress indicator is shown with drag-and-drop operations.</summary>
            FD_PROGRESSUI = 0x00004000,

            /// <summary>Treat the operation as a shortcut.</summary>
            FD_LINKUI = 0x00008000,

            /// <summary><c>Windows Vista and later</c>. The descriptor is Unicode.</summary>
            FD_UNICODE = 0x80000000,
        }

        // https://github.com/dahall/Vanara/blob/master/PInvoke/Shared/WinNT/FileFlagsAndAttributes.cs
        /// <summary>
        /// File attributes are metadata values stored by the file system on disk and are used by the system and are available to developers via
        /// various file I/O APIs.
        /// </summary>
        [Flags]
        public enum FileFlagsAndAttributes : uint
        {
            /// <summary>
            /// A file that is read-only. Applications can read the file, but cannot write to it or delete it. This attribute is not honored on
            /// directories.
            /// </summary>
            FILE_ATTRIBUTE_READONLY = 0x00000001,

            /// <summary>The file or directory is hidden. It is not included in an ordinary directory listing.</summary>
            FILE_ATTRIBUTE_HIDDEN = 0x00000002,

            /// <summary>A file or directory that the operating system uses a part of, or uses exclusively.</summary>
            FILE_ATTRIBUTE_SYSTEM = 0x00000004,

            /// <summary>The handle that identifies a directory.</summary>
            FILE_ATTRIBUTE_DIRECTORY = 0x00000010,

            /// <summary>
            /// A file or directory that is an archive file or directory. Applications typically use this attribute to mark files for backup or
            /// removal.
            /// </summary>
            FILE_ATTRIBUTE_ARCHIVE = 0x00000020,

            /// <summary>This value is reserved for system use.</summary>
            FILE_ATTRIBUTE_DEVICE = 0x00000040,

            /// <summary>A file that does not have other attributes set. This attribute is valid only when used alone.</summary>
            FILE_ATTRIBUTE_NORMAL = 0x00000080,

            /// <summary>
            /// A file that is being used for temporary storage. File systems avoid writing data back to mass storage if sufficient cache memory is
            /// available, because typically, an application deletes a temporary file after the handle is closed. In that scenario, the system can
            /// entirely avoid writing the data. Otherwise, the data is written after the handle is closed.
            /// </summary>
            FILE_ATTRIBUTE_TEMPORARY = 0x00000100,

            // The following flags are omitted
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILEGROUPDESCRIPTOR
        {
            public UInt32 cItems;
            // Followed by 0 or more FILEDESCRIPTORs
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct FILEDESCRIPTOR
        {
            public FD_FLAGS dwFlags;
            public Guid clsid;
            public Int32 sizelcx;
            public Int32 sizelcy;
            public Int32 pointlx;
            public Int32 pointly;
            public FileFlagsAndAttributes dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public UInt32 nFileSizeHigh;
            public UInt32 nFileSizeLow;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
        }

        [ComImport]
        [Guid("00000121-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDropSource
        {
            [PreserveSig]
            int QueryContinueDrag(int fEscapePressed, uint grfKeyState);
            [PreserveSig]
            int GiveFeedback(uint dwEffect);
        }

        [DllImport("shell32.dll")]
        public static extern int SHCreateStdEnumFmtEtc(uint cfmt, FORMATETC[] afmt, out IEnumFORMATETC ppenumFormatEtc);

        [return: MarshalAs(UnmanagedType.Interface)]
        [DllImport("ole32.dll", PreserveSig = false)]
        public static extern IStream CreateStreamOnHGlobal(HANDLE hGlobal, [MarshalAs(UnmanagedType.Bool)] bool fDeleteOnRelease);

        [DllImport("ole32.dll", CharSet = CharSet.Auto, ExactSpelling = true, PreserveSig = false)]
        public static extern void DoDragDrop(System.Runtime.InteropServices.ComTypes.IDataObject dataObject, IDropSource dropSource, int allowedEffects, int[] finalEffect);

        [DllImport("kernel32.dll")]
        public static extern HANDLE GlobalLock(HANDLE hMem);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(HANDLE hMem);

        [DllImport("kernel32.dll")]
        public static extern HANDLE GlobalSize(HANDLE handle);

        /// <summary>
        /// Returns true if the HRESULT is a success code.
        /// </summary>
        /// <param name="hr">HRESULT to check.</param>
        /// <returns>True if a success code.</returns>
        public static bool SUCCEEDED(int hr)
        {
            return 0 <= hr;
        }
    }
}

/// <summary>
/// Definition of the IAsyncOperation COM interface.
/// </summary>
/// <remarks>
/// Pseudo-public because VirtualFileDataObject implements it.
/// </remarks>
[ComImport]
[Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAsyncOperation
{
    void SetAsyncMode([In] Int32 fDoOpAsync);
    void GetAsyncMode([Out] out Int32 pfIsOpAsync);
    void StartOperation([In] IBindCtx pbcReserved);
    void InOperation([Out] out Int32 pfInAsyncOp);
    void EndOperation([In] Int32 hResult, [In] IBindCtx pbcReserved, [In] UInt32 dwEffects);
}
