namespace ADB_Explorer.Models;

public enum DialogError
{
    UnhandledException = 1000,
    CrashReportSendFailed = 1001,

    ListDirectoryFailed = 1100,
    NavigationFailed = 1101,
    CreateFileFailed = 1102,
    DestinationPathFailed = 1103,
    WriteFileFailed = 1104,
    ReadFileFailed = 1105,
    WinRootIllegalPath = 1106,

    SideloadFailed = 1200,
    DisconnectFailed = 1201,
    RootForbidden = 1202,
    EmulatorLaunchFailed = 1203,
    PairingFailed = 1204,
    ConnectionFailed = 1205,

    InvalidDefaultFolder = 1300,
    InvalidAppDataLocation = 1301,
}
