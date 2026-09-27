using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    /// <summary>
    /// Copies top-level members of the selected archive onto the clipboard (context menu only).
    /// </summary>
    public static void CopyArchiveContents()
    {
        if (!TryGetSelectedNavigableArchive(out var archive) || archive is null)
            return;

        var device = Data.ActiveDevice;
        var archivePath = archive.FullPath;
        var token = Data.DeviceCts.Token;

        Task.Run(() =>
        {
            List<FileClass> members;
            try
            {
                members = [.. ArchiveListing.ListEntries(device.ID, archivePath, "", token)
                    .Select(FileClass.GenerateAndroidFile)];
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Copy archive contents failed: {e.Message}");
#endif
                return;
            }

            if (members.Count == 0)
                return;

            App.SafeInvoke(() =>
            {
                CutFiles(members, isCopy: true);
                UpdateFileActions();
            });
        }, token);
    }

    /// <summary>
    /// Extracts top-level members of a selected archive, or of a clipboard archive file, into the current folder.
    /// </summary>
    public static void ExtractArchiveHere()
    {
        if (ActionDevice is not { } device)
            return;

        string? archivePath = null;
        var targetFolder = ActionPath;

        if (TryGetSelectedNavigableArchive(out var selectedArchive))
        {
            archivePath = selectedArchive.FullPath;
            targetFolder = ActionPath;
        }
        else if (IsClipboardSingleArchiveFileCopy())
        {
            archivePath = Data.CopyPaste.Files[0];
            targetFolder = GetUiPasteTargetPath();
        }

        if (archivePath is null
            || ArchivePath.IsArchivePath(targetFolder, device.ID)
            || !DriveHelper.IsModificationAllowedAt(targetFolder, device.ID))
            return;

        var token = Data.DeviceCts.Token;

        Task.Run(() =>
        {
            List<FileClass> members;
            try
            {
                members = [.. ArchiveListing.ListEntries(device.ID, archivePath, "", token)
                    .Select(FileClass.GenerateAndroidFile)];
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Extract archive here failed: {e.Message}");
#endif
                return;
            }

            if (members.Count == 0)
                return;

            App.SafeInvoke(() => Data.CopyPaste.VerifyAndPaste(
                DragDropEffects.Copy,
                targetFolder,
                members,
                App.AppDispatcher,
                device,
                Data.CurrentPath));
        }, token);
    }

    /// <summary>
    /// True when the self clipboard holds member paths from <see cref="CopyArchiveContents"/>
    /// for the currently selected archive.
    /// </summary>
    private static bool IsClipboardContentsOfSelectedArchive()
    {
        if (!TryGetSelectedNavigableArchive(out var archive)
            || archive is null
            || !Data.CopyPaste.IsSelf
            || Data.CopyPaste.PasteState is not DragDropEffects.Copy
            || Data.CopyPaste.Files.Length == 0
            || ActionDevice is not { } device)
            return false;

        var selectedArchive = archive.FullPath;

        foreach (var path in Data.CopyPaste.Files)
        {
            if (!ArchivePath.TryParse(path, out var archivePath, out var internalPath, device.ID)
                || string.IsNullOrEmpty(internalPath)
                || !string.Equals(archivePath, selectedArchive, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool TryGetSelectedNavigableArchive(out FileClass? archive)
    {
        archive = null;

        if (ActionDevice is not { } device
            || ActionFlags.IsAppDrive
            || ActionFlags.IsRecycleBin
            || !ActionFlags.IsRegularItem
            || Data.SelectedFiles.Count() != 1
            || Data.SelectedFiles.First() is not { } selected)
            return false;

        if (!ArchiveHelper.CanNavigateIntoArchive(
                selected.FullPath,
                selected.FullName,
                device.ID,
                ActionFlags.IsArchive))
            return false;

        archive = selected;
        return true;
    }

    private static bool CanExtractSelectedArchiveHere()
    {
        if (ActionDevice is not { } device
            || !TryGetSelectedNavigableArchive(out _))
            return false;

        var target = ActionPath;
        return !ArchivePath.IsArchivePath(target, device.ID)
            && DriveHelper.IsModificationAllowedAt(target, device.ID);
    }

    private static bool CanExtractClipboardArchiveHere()
    {
        if (!IsClipboardSingleArchiveFileCopy()
            || ActionDevice is not { } device)
            return false;

        var target = GetUiPasteTargetPath();
        return !ArchivePath.IsArchivePath(target, device.ID)
            && DriveHelper.IsModificationAllowedAt(target, device.ID);
    }

    /// <summary>
    /// True when the self clipboard holds one archive <em>file</em> (ordinary Copy), not member paths.
    /// </summary>
    private static bool IsClipboardSingleArchiveFileCopy()
    {
        if (!Data.CopyPaste.IsSelf
            || Data.CopyPaste.PasteState is not DragDropEffects.Copy
            || Data.CopyPaste.Files.Length != 1
            || ActionDevice is not { } device)
            return false;

        var path = Data.CopyPaste.Files[0];
        if (ArchivePath.IsArchivePath(path, device.ID))
            return false;

        return ArchiveHelper.IsNavigableArchive(FileHelper.GetFullName(path), device.ID);
    }
}
