namespace ADB_Explorer.Models;

public record struct FileStat(string FullName,
                       string FullPath,
                       AbstractFile.FileType Type,
                       bool IsLink,
                       long? Size,
                       DateTime? ModifiedTime,
                       UnixFileMode? Permissions,
                       long? CompressedSize = null,
                       string? CompressionMethod = null,
                       string? CompressionRatio = null,
                       string? Crc32 = null) : IBaseFile, IFileStat;
