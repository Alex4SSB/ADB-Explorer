using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public partial class CopyPasteService
{
    private bool ShouldKeepSelfAndroidClipboard(IDataObject dataObject)
    {
        if (!CurrentSource.HasFlag(DataSource.Android) || Files.Length == 0)
            return false;

        // Only tolerate a transient OLE gap while nothing has touched the clipboard since our
        // own write - any other sequence number means a distinct, fully-formed update (an image,
        // a text copy, another self-copy...) that must be processed normally, not preserved.
        if (_selfCopyClipboardSequence != NativeMethods.MGetClipboardSequenceNumber())
            return false;

        if (dataObject.GetDataPresent(AdbDataFormats.AdbDrop)
            && dataObject.GetData(AdbDataFormats.AdbDrop) is MemoryStream)
            return false;

        // A new Windows/shell copy typically includes FileDrop. OLE can omit AdbDrop
        // on the first update while still holding our FileDescriptor payload.
        return !dataObject.GetDataPresent(AdbDataFormats.FileDrop);
    }

    private static bool IsAndroidAbsolutePath(string path)
        => !string.IsNullOrEmpty(path) && path[0] == '/';

    public void GetClipboardPasteItems()
    {
        var CPDO = Clipboard.GetDataObject();

#if !DEPLOY
        DebugLog.PrintLine($"Clipboard formats: {string.Join(", ", CPDO.GetFormats())}");
#endif

        if (ShouldKeepSelfAndroidClipboard(CPDO))
        {
            UpdateUI();
            return;
        }

        var allowedEffect = GetAllowedDragEffects(CPDO);
        if (allowedEffect is DragDropEffects.None)
        {
            PasteState = DragDropEffects.None;
            PasteSource = DataSource.None;
            Files = [];
            _currentFiles = [];
            HasClipboardImage = CPDO?.GetDataPresent(DataFormats.Bitmap) == true;

            UpdateUI();
            ArchiveExtract.BeginCleanupAllStaging();
            return;
        }

        HasClipboardImage = false;

        var prefDropEffect = VirtualFileDataObject.GetPreferredDropEffect(CPDO);

        // Link is only allowed depending on the target
        if (prefDropEffect.HasFlag(DragDropEffects.Link) && allowedEffect.HasFlag(DragDropEffects.Link))
            PasteState = DragDropEffects.Link;
        else if (prefDropEffect.HasFlag(DragDropEffects.Copy) && allowedEffect.HasFlag(DragDropEffects.Copy))
            PasteState = DragDropEffects.Copy;
        else if (prefDropEffect.HasFlag(DragDropEffects.Move) && allowedEffect.HasFlag(DragDropEffects.Move))
            PasteState = DragDropEffects.Move;
        else if (prefDropEffect is DragDropEffects.Move && allowedEffect is DragDropEffects.Copy)
            PasteState = DragDropEffects.Copy; // fallback to copy
        else
            PasteState = DragDropEffects.None;

        if (DragFiles.Length > 0 && DragFiles.All(IsAndroidAbsolutePath))
            Files = DragFiles;
        else if (!CurrentSource.HasFlag(DataSource.Android) || Files.Length == 0)
            Files = DragFiles;

        if (!string.IsNullOrEmpty(DragParent))
            ParentFolder = DragParent;

        UpdateUI();

        // External clipboard replaced a self archive copy — drop unused extract staging.
        if (!IsAndroid)
            ArchiveExtract.BeginCleanupAllStaging();
    }

    public void UpdateSelfVFDO(bool isDrag, DragDropEffects pasteEffect = DragDropEffects.None)
    {
        if (VirtualFileDataObject.SelfFiles is null || !VirtualFileDataObject.SelfFiles.Any())
            return;

        if (isDrag)
        {
            DragPasteSource = (DragPasteSource | DataSource.Android) & ~DataSource.None;
        }
        else
        {
            PasteSource = (PasteSource | DataSource.Android) & ~DataSource.None;
            if (pasteEffect is not DragDropEffects.None)
                PasteState = pasteEffect;
        }

        var copyDevice = Data.Active.Device ?? Data.ActiveDevice;
        if (copyDevice is not null
            && (SourceDevice is null || !ReferenceEquals(Data.Active, Data.Files)))
            SourceDevice = copyDevice;
        var transferParent = FileHelper.GetSearchTransferParent(VirtualFileDataObject.SelfFiles);
        if (Data.FileActions.IsSearchMode)
            Data.SearchTransferParent = transferParent;

        DragParent = transferParent;

        // FileDescriptors may still be the empty placeholder while PrepareDescriptors runs
        // (especially archive extract-to-tmp). Fall back to SelfFiles until they are ready.
        var descriptors = VirtualFileDataObject.SelfFileGroup?.FileDescriptors?.ToArray();
        if (descriptors is { Length: > 0 })
        {
            DragFiles = [.. descriptors.Select(d => d.Name)];
            Descriptors = descriptors;
        }
        else
        {
            DragFiles = [.. VirtualFileDataObject.SelfFiles.Select(FileHelper.GetSearchTransferName)];
            Descriptors = [];
        }

        if (!isDrag)
        {
            Files = [.. VirtualFileDataObject.SelfFiles.Select(f => f.FullPath)];
            ParentFolder = DragParent;
        }

        UpdateUI();
    }
}
