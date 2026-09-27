namespace ADB_Explorer.Models;

[Flags]
public enum AccessMask
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    All = Read | Write | Execute,
}

public enum DevicePathKind
{
    Unknown,
    Directory,
    RegularFile,
}

public readonly record struct LocationInfo(
    string? User,
    string? Group,
    int? OwnerUid,
    int? OwnerGid,
    UnixFileMode? Permissions,
    AccessMask ProbedAccess,
    DateTimeOffset? AccessTime,
    DateTimeOffset? ModifiedTime);
