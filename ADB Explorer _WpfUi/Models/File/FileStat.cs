namespace ADB_Explorer.Models;

public record struct FileStat(string FullName,
                       string FullPath,
                       AbstractFile.FileType Type,
                       bool IsLink,
                       long? Size,
                       DateTime? ModifiedTime) : IBaseFile, IFileStat;
