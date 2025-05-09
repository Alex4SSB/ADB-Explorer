using ADB_Explorer.Models;

namespace ADB_Explorer.Services.AppInfra;

public class IpcService
{
    public enum MessageType
    {
        DragCanceled,
        FileMoved,
    }

    public static void AcceptIpcMessage(string message)
    {
        string[] msgContent = message.Split('|');
        if (!Enum.TryParse(typeof(MessageType), msgContent[0], true, out var res))
            return;

        switch ((MessageType)res)
        {
            case MessageType.DragCanceled:
                if (Enum.TryParse(msgContent[1], out NativeMethods.HResult hr) && hr is NativeMethods.HResult.DRAGDROP_S_CANCEL)
                    Data.CopyPaste.ClearDrag();
                break;
            case MessageType.FileMoved:
                var content = msgContent[1].Split('\n');
                if (Data.CurrentADBDevice.ID != content[0])
                    return;

                FilePath file = new(content[1]);
                if (Data.CurrentPath != file.ParentPath)
                    return;

                Data.DirList.FileList.RemoveAll(f => f.FullPath == file.FullPath);

                break;
        }
    }

    public static bool SendIpcMessage(HANDLE hWnd, MessageType type, string content = "")
    {
        var message = $"{Enum.GetName(type)}|{content}";

        NativeMethods.COPYDATASTRUCT cds = new()
        {
            dwData = IntPtr.Zero,
            cbData = Encoding.Unicode.GetByteCount(message) + 2,
            lpData = message
        };

        return NativeMethods.SendMessage(hWnd, NativeMethods.WindowMessages.WM_COPYDATA, ref cds);
    }

    public static void NotifyDropCancel(NativeMethods.HResult hr)
    {
        if (Data.RuntimeSettings.DragWithinSlave)
            SendIpcMessage(NativeMethods.InterceptMouse.WindowUnderMouse, MessageType.DragCanceled, $"{hr}");
    }

    public static void NotifyFileMoved(int remotePid, ADBService.AdbDevice device, FilePath file)
    {
        var process = Process.GetProcessById(remotePid);

        SendIpcMessage(process.MainWindowHandle, MessageType.FileMoved, $"{device.ID}\n{file.FullPath}");
    }
}
