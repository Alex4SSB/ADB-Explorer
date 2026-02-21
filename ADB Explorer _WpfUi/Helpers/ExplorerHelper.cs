using static Vanara.PInvoke.Shell32;

namespace ADB_Explorer.Helpers;

public class ExplorerHelper
{
    public static bool NotifyFileCreated(string path)
    {
        var hPath = (nuint)Marshal.StringToHGlobalUni(path);
        bool result = false;

        try
        {
            SHChangeNotify(SHCNE.SHCNE_CREATE, SHCNF.SHCNF_PATHW, hPath);
            result = true;
        }
        catch
        { }
        finally
        {
            Marshal.FreeHGlobal((nint)hPath);
        }

        return result;
    }
}
