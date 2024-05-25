// Virtual File Data Object library by David Anson
// https://dlaa.me/blog/post/9913083
// Used and modified under the MIT license

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System.Runtime.InteropServices.ComTypes;

namespace ADB_Explorer.Services;

using HANDLE = IntPtr;

/// <summary>
/// Handles everything related to Shell Drag & Drop (including clipboard) 
/// </summary>
public sealed class VirtualFileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject, IAsyncOperation
{
    /// <summary>
    /// Gets or sets a value indicating whether the data object can be used asynchronously.
    /// </summary>
    public bool IsAsynchronous { get; set; }

    private static readonly short FILECONTENTS = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_FILECONTENTS).Id);

    private static readonly short FILEDESCRIPTORW = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_FILEDESCRIPTORW).Id);

    private static readonly short PASTESUCCEEDED = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_PASTESUCCEEDED).Id);

    private static readonly short PERFORMEDDROPEFFECT = (short)(DataFormats.GetDataFormat(NativeMethods.CFSTR_PERFORMEDDROPEFFECT).Id);

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
            if (NativeMethods.SUCCEEDED(NativeMethods.SHCreateStdEnumFmtEtc(_dataObjects.Count, _dataObjects.Select(d => d.FORMATETC), out IEnumFORMATETC enumerator)))
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
                    var iStream = NativeMethods.MCreateStreamOnHGlobal(IntPtr.Zero, true);
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

        // Add FILEGROUPDESCRIPTOR header

        // Add n FILEDESCRIPTORs
        // Update file descriptors to preserve the pointer if already set
        
        FileGroup group = new(fileDescriptors);

        UpdateData(FILEDESCRIPTORW, group.GroupDescriptorBytes);

        UpdateData(FILECONTENTS, group.DataStreams);
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
            NativeMethods.DoDragDrop(dataObject, new DropSource(), allowedEffects, finalEffect);
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
        return (DragDropEffects)finalEffect[0];
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

    public class DragBitmap
    {
        public BitmapSource Bitmap { get; internal set; }

        public Point Offset { get; internal set; }

        public Color Background { get; internal set; }
    }

    public static DragBitmap GetBitmapFromShell(MemoryStream stream)
    {
        var shImage = NativeMethods.SHDRAGIMAGE.FromStream(stream, out var bitmap);

        // Remove opacity effects
        for (int i = 0; i < bitmap.Length; i += 4)
        {
            var bgra = bitmap[i..(i + 4)];

            // Opacity = 0%   ->  leave as is
            if (bgra[3] == 0)
                continue;

            // Color = Black   ->   Bump up opacity to 100%
            if (bgra[0..3].Max() == 0)
            {
                // if the opacity isn't 255 - 64, then it wasn't originally black - leave as is
                if (bgra[3] == 191)
                    bitmap[i + 3] = 255;

                continue;
            }

            var hsv = ColorHelper.BgrToHsv(bgra[0..3]);

            // Increase value by ~34%
            hsv[2] = Math.Clamp(hsv[2] / 0.745, 0, 1);
            var bgr = ColorHelper.HsvToBgr(hsv);

            // Increase Opacity by 25%
            bitmap[i + 3] = (byte)Math.Clamp(bgra[3] + 64, 0, 255);

            Array.Copy(bgr, 0, bitmap, i, 3);
        }

        var bitmapSource = BitmapSource.Create(shImage.sizeDragImage.Width,
                                               shImage.sizeDragImage.Height,
                                               shImage.sizeDragImage.Width,
                                               shImage.sizeDragImage.Height,
                                               PixelFormats.Bgra32,
                                               null,
                                               bitmap,
                                               shImage.sizeDragImage.Width * 4);

        return New(bitmapSource, shImage);


        static DragBitmap New(BitmapSource bitmapSource, NativeMethods.SHDRAGIMAGE shImage)
        {
            DragBitmap bitmap = new()
            {
                Bitmap = bitmapSource,
                Offset = shImage.ptOffset,
                Background = (shImage.sizeDragImage.Width == 96 && shImage.sizeDragImage.Height == 96) ? Colors.White : Colors.Transparent,
            };

            return bitmap;
        }
    }
}
