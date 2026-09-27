namespace ADB_Explorer.ViewModels;

/// <summary>
/// Builds the info and permissions rows the details pane shows for a selected drive, package or file.
/// </summary>
public static class DetailsRows
{
    public static IEnumerable<IDetailsViewModel> ForDrive(DriveViewModel drive)
    {
        if (drive is not (LogicalDriveViewModel or VirtualDriveViewModel { Type: AbstractDrive.DriveType.Temp }))
            yield break;

        yield return new ItemDetailsViewModel<DriveViewModel>(drive, Strings.Resources.S_MOUNT_POINT, d => d.MountPoint is null ? "" : d.MountPoint, valueIsLtr: true).Init(nameof(DriveViewModel.MountPoint));

        yield return new ItemDetailsViewModel<DriveViewModel>(drive, Strings.Resources.S_FILE_SYSTEM, d => d.FileSystem is null ? "" : d.FileSystem.ToUpper(), valueIsLtr: true).Init(nameof(DriveViewModel.FileSystem));

        yield return new ItemDetailsViewModel<DriveViewModel>(drive, Strings.Resources.S_FILE_BLOCK, d => d.BlockDevice is null ? "" : d.BlockDevice, valueIsLtr: true).Init(nameof(DriveViewModel.BlockDevice));
    }

    public static IEnumerable<IDetailsViewModel> ForPackage(Package package)
    {
        yield return new ItemDetailsViewModel<Package>(package, Strings.Resources.S_PACKAGE_ID, p => p.Name, valueIsLtr: true);

        yield return new ItemDetailsViewModel<Package>(package, Strings.Resources.S_ITEM_LOCATION, f => FileHelper.GetParentPath(f.Path), valueIsLtr: true);

        yield return new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_TYPE, p => $"{p.Type}");

        yield return new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_USER_ID, p => $"{p.Uid}");
        yield return new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_VERSION, p => p.VersionName ?? $"{p.Version}").Init(nameof(Package.VersionName), nameof(Package.Version));

        yield return new ItemDetailsViewModel<Package>(package, Strings.Resources.S_COLUMN_DATE_MODIFIED, p => p.LastUpdateTime.HasValue ? p.LastUpdateTime.Value.ToString(Data.Settings.ActualFormatCulture) : "").Init(nameof(Package.LastUpdateTime));
    }

    /// <summary>Modified, accessed and creation are always listed; see <see cref="DateDetailsViewModel"/> for when a value is left blank.</summary>
    private static IEnumerable<IDetailsViewModel> ForDates(FileClass file)
    {
        var noAccessTime = DriveHelper.HasNoAccessTime(file.FullPath);

        yield return new DateDetailsViewModel(file, DateDetailsViewModel.DateKind.Modified, true);

        // Without noatime the access time moves on every read, and no creation time is available.
        yield return new DateDetailsViewModel(file, DateDetailsViewModel.DateKind.Accessed, !noAccessTime);

        yield return new DateDetailsViewModel(
            file,
            DateDetailsViewModel.DateKind.Created,
            noAccessTime && !Data.FileActions.IsRecycleBin && !ArchivePath.IsArchivePath(file.FullPath, Data.ActiveDevice?.ID));
    }

    public static IEnumerable<IDetailsViewModel> ForFile(FileClass file)
    {
        if (file.Type is not AbstractFile.FileType.Unknown)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_TYPE, f => f.FolderViewModel.TypeName);

        if (Data.FileActions.IsRecycleBin)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_ORIGINAL_LOCATION, f => f.TrashIndex!.OriginalPath, valueIsLtr: true);
        else
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_ITEM_LOCATION, ArchivePath.FormatParentLocation, valueIsLtr: true);

        var archiveSummaryPath = ArchiveHelper.GetSummaryPath(file);

        if (archiveSummaryPath is not null
            && ArchiveListing.TryGetArchiveSummary(archiveSummaryPath, out var archiveSummary))
        {
            yield return new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_COMPRESSED_SIZE, _ => archiveSummary.CompressedSize.BytesToSize(true), valueIsLtr: true);

            yield return new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_PACKED_SIZE, _ => archiveSummary.UncompressedSize.BytesToSize(true), valueIsLtr: true);

            yield return new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_COMPRESSION_RATIO, _ => archiveSummary.Ratio, valueIsLtr: true);

            yield return new ItemDetailsViewModel<FileClass>(
                file, Strings.Resources.S_MENU_FILES, _ => $"{archiveSummary.FileCount}", valueIsLtr: true);
        }
        else if (archiveSummaryPath is not null && ArchiveHelper.IsTarFamily(archiveSummaryPath))
        {
            if (file.ShellLsSize is >= 0 || file.Size.HasValue)
            {
                yield return new ItemDetailsViewModel<FileClass>(
                    file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString, valueIsLtr: true).Init(nameof(FileClass.Size), nameof(FileClass.ShellLsSize));
            }
        }
        else
        {
            if (file.Type is AbstractFile.FileType.File && file.Size.HasValue)
                yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString).Init(nameof(FileClass.Size), nameof(FileClass.ShellLsSize));

            if (file.CompressedSize is long compressedSize)
                yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COMPRESSED_SIZE, _ => compressedSize.BytesToSize(true), valueIsLtr: true);

            if (!string.IsNullOrEmpty(file.CompressionRatio))
                yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COMPRESSION_RATIO, f => f.CompressionRatio!, valueIsLtr: true);
        }

        if (archiveSummaryPath is null && !string.IsNullOrEmpty(file.CompressionMethod))
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COMPRESSION_METHOD, f => ArchiveHelper.GetZipMethodDisplayName(f.CompressionMethod!), valueIsLtr: true);

        if (!string.IsNullOrEmpty(file.Crc32))
            yield return new ItemDetailsViewModel<FileClass>(file, "CRC-32", f => f.Crc32!.ToUpperInvariant(), valueIsLtr: true, useConsoleFont: true);

        foreach (var row in ForDates(file))
            yield return row;

        if (Data.FileActions.IsRecycleBin)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_COLUMN_DATE_DELETED, f => f.TrashIndex!.ModifiedTimeString);

        if (file.IsLink)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_FILE_TYPE_LINK, f => f.LinkTarget, valueIsLtr: true);

        if (file.CacheThumbnail is not { } thumb)
            yield break;

        var info = thumb.Info;

        if (info.Resolution.HasValue)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_PICTURE_DIMENSIONS, f => f.CacheThumbnail!.Value.Info.ResolutionString, valueIsLtr: true);

        if (info.Duration.HasValue)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_VIDEO_DURATION, f => f.CacheThumbnail!.Value.Info.DurationString);

        if (info.FNumber.HasValue)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_IMAGE_F_STOP, f => f.CacheThumbnail!.Value.Info.FNumberString, valueIsLtr: true);

        if (info.ExposureTime.HasValue)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_IMAGE_EXPOSURE, f => f.CacheThumbnail!.Value.Info.ExposureTimeString);

        if (info.ISO.HasValue)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_IMAGE_ISO, f => f.CacheThumbnail!.Value.Info.ISOString, valueIsLtr: true);

        if (info.Bitrate.HasValue)
            yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_VIDEO_BITRATE, f => f.CacheThumbnail!.Value.Info.BitrateString, valueIsLtr: true);
    }

    public static IEnumerable<IDetailsViewModel> ForPermissions(FileClass file)
    {
        if (!file.Permissions.HasValue)
            yield break;

        yield return new ItemDetailsViewModel<FileClass>(file,
                                                         Strings.Resources.S_FILE_PERM_USER,
                                                         f => FormatOwner(f.User, f.FolderViewModel.UserPermissionsString),
                                                         valueIsLtr: true,
                                                         useConsoleFont: true).Init(nameof(FileClass.User), nameof(FileClass.Permissions));

        yield return new ItemDetailsViewModel<FileClass>(file,
                                                         Strings.Resources.S_FILE_PERM_GROUP,
                                                         f => FormatOwner(f.Group, f.FolderViewModel.GroupPermissionsString),
                                                         valueIsLtr: true,
                                                         useConsoleFont: true).Init(nameof(FileClass.Group), nameof(FileClass.Permissions));

        yield return new ItemDetailsViewModel<FileClass>(file, Strings.Resources.S_FILE_PERM_OTHER, f => $"{f.FolderViewModel.OtherPermissionsString}", valueIsLtr: true, useConsoleFont: true);
    }

    private static string FormatOwner(string? owner, string permission)
    {
        string name = owner is null ? "" : $"({owner})";

        return Data.RuntimeSettings.IsRTL
            ? $"{permission} {name}"
            : $"{name} {permission}";
    }
}
