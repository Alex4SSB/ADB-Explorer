using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public enum ArchiveFamily
{
    None,
    Tar,
    Zip,
}

public static class ArchiveHelper
{
    private static readonly HashSet<string> TarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".TAR", ".TGZ", ".TPZ", ".TZST", ".TBZ", ".TBZ2", ".TXZ",
    };

    public static ArchiveFamily GetFamily(string fileName)
    {
        var ext = FileHelper.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
            return ArchiveFamily.None;

        var extUpper = ext.ToUpperInvariant();

        if (AdbExplorerConst.APK_NAMES.Contains(extUpper) || extUpper == ".ZIP")
            return ArchiveFamily.Zip;

        if (TarExtensions.Contains(extUpper) || extUpper.StartsWith(".TAR.", StringComparison.Ordinal))
            return ArchiveFamily.Tar;

        return ArchiveFamily.None;
    }

    public static bool IsTarFamily(string fileName) => GetFamily(fileName) is ArchiveFamily.Tar;

    public static bool IsZipFamily(string fileName) => GetFamily(fileName) is ArchiveFamily.Zip;

    public static bool IsCompressedTar(string fileName)
    {
        if (!IsTarFamily(fileName))
            return false;

        return !FileHelper.GetExtension(fileName).Equals(".tar", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanBrowse(string fileName, string deviceId) => GetFamily(fileName) switch
    {
        ArchiveFamily.Tar => ShellCommands.TarExists(deviceId),
        ArchiveFamily.Zip => ShellCommands.UnzipExists(deviceId),
        _ => false,
    };

    public static bool CanModify(string fileName, string deviceId) => GetFamily(fileName) switch
    {
        ArchiveFamily.Zip => ShellCommands.ZipExists(deviceId) && ShellCommands.UnzipExists(deviceId),
        ArchiveFamily.Tar => ShellCommands.TarExists(deviceId)
            && ShellCommands.TarAppendSupported(deviceId)
            && !IsCompressedTar(fileName),
        _ => false,
    };

    public static bool IsModificationAllowedAt(string path, string deviceId)
        => !ArchivePath.IsArchivePath(path, deviceId)
        || CanModify(FileHelper.GetFullName(ArchivePath.GetArchivePath(path, deviceId)), deviceId);

    /// <summary>
    /// Whether preview of an archive member should be read-only.
    /// True when the archive/drive is read-only, or when member update is unsupported (non-zip).
    /// Non-archive paths always return <see langword="false"/>.
    /// </summary>
    public static bool IsMemberPreviewReadOnly(string path, string deviceId)
    {
        if (!ArchivePath.TryParse(path, out var archivePath, out var internalPath, deviceId)
            || string.IsNullOrEmpty(internalPath))
            return false;

        if (!DriveHelper.IsModificationAllowedAt(path, deviceId))
            return true;

        // Zip can replace a member (<c>zip -u</c>); tar append cannot update in place.
        return GetFamily(archivePath) is not ArchiveFamily.Zip;
    }

    public static bool IsNavigableArchive(string fileName, string deviceId)
        => GetFamily(fileName) is not ArchiveFamily.None && CanBrowse(fileName, deviceId);

    public static bool CanNavigateIntoArchive(string fileFullPath, string fileName, string deviceId, bool isInsideArchive)
        => !isInsideArchive
        && !ArchivePath.IsArchivePath(fileFullPath, deviceId)
        && IsNavigableArchive(fileName, deviceId)
        && ArchivePath.IsBrowsableArchiveFile(fileFullPath, deviceId);

    public static string GetArchiveModificationTooltip(string fileName, string deviceId)
        => CanModify(fileName, deviceId)
            ? Strings.Resources.S_ARCHIVE_CAN_MODIFY
            : Strings.Resources.S_ARCHIVE_READ_ONLY;

    /// <summary>
    /// Whether archive members can be validated with the device's preferred hash mode
    /// (IEEE CRC-32 via <c>cksum -HNPL</c> if available, otherwise MD5).
    /// Zip uses TOC CRC-32 (needs shell <c>cksum</c> on Android dest); tar uses <c>--to-command</c> or <c>-O</c>.
    /// </summary>
    public static bool SupportsHashValidation(string archivePath, string deviceId)
        => SupportsHashValidation(archivePath, deviceId, androidDestination: true);

    /// <param name="androidDestination">
    /// When <see langword="false"/> (pull to Windows), zip TOC CRC can be checked without shell <c>cksum</c>.
    /// </param>
    public static bool SupportsHashValidation(string archivePath, string deviceId, bool androidDestination)
    {
        var mode = ShellCommands.GetValidationHashMode(deviceId);

        return GetFamily(archivePath) switch
        {
            ArchiveFamily.Zip when !ShellCommands.UnzipExists(deviceId) => false,
            // Zip TOC is CRC-32; Windows dest can CRC locally; Android dest needs shell cksum.
            ArchiveFamily.Zip => !androidDestination || mode is ValidationHashMode.Crc32,
            ArchiveFamily.Tar => mode is not ValidationHashMode.None
                && ShellCommands.TarExists(deviceId)
                && ShellCommands.TarHashPipelineSupported(deviceId),
            _ => false,
        };
    }

    /// <summary>Whether validation should use CRC-32 (device preference or zip TOC / Windows pull).</summary>
    public static bool UsesCrc32Validation(string archivePath, string deviceId, bool androidDestination = true)
    {
        if (ShellCommands.GetValidationHashMode(deviceId) is ValidationHashMode.Crc32)
            return true;

        // Zip pull to Windows: TOC CRC vs local CRC without shell cksum.
        return GetFamily(archivePath) is ArchiveFamily.Zip && !androidDestination;
    }

    /// <summary>Maps Info-ZIP <c>unzip -lv</c> method codes to readable labels.</summary>
    public static string GetZipMethodDisplayName(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return method ?? "";

        var raw = method.Trim();

        if (raw.Equals("Stored", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("Stor", StringComparison.OrdinalIgnoreCase))
            return "Stored";

        if (raw.StartsWith("Defl:", StringComparison.OrdinalIgnoreCase))
            return FormatDeflateLevel(raw[5..]);

        if (raw.Length == 4 && raw.StartsWith("def", StringComparison.OrdinalIgnoreCase))
            return FormatDeflateLevel(raw[3..]);

        if (raw.Equals("Shrunk", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("Shrk", StringComparison.OrdinalIgnoreCase))
            return "Shrunk";

        if (raw.Length >= 3 && raw.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
            return $"Reduced (level {raw[3..]})";

        if (raw.Length >= 4 && raw.StartsWith("i", StringComparison.OrdinalIgnoreCase)
            && char.IsDigit(raw[1]) && raw[2] == ':')
            return "Imploded";

        if (raw.Equals("Tokn", StringComparison.OrdinalIgnoreCase))
            return "Tokenized";

        if (raw.StartsWith("BZip", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("Bz", StringComparison.OrdinalIgnoreCase))
            return "BZip2";

        if (raw.StartsWith("LZMA", StringComparison.OrdinalIgnoreCase))
            return "LZMA";

        if (raw.StartsWith("Zstd", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ZStd", StringComparison.OrdinalIgnoreCase))
            return "Zstandard";

        return raw;
    }

    private static string FormatDeflateLevel(ReadOnlySpan<char> level) => level switch
    {
        "N" or "n" => "Deflate (normal)",
        "X" or "x" => "Deflate (maximum)",
        "F" or "f" => "Deflate (fast)",
        "S" or "s" => "Deflate (super fast)",
        _ => level.Length > 0 ? $"Deflate ({level})" : "Deflate",
    };
}
