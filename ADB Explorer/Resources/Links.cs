using ADB_Explorer.Models;

namespace ADB_Explorer.Resources;

public static class Links
{
    public static readonly Uri L_ADB_PAGE = Data.RuntimeSettings.IsAppDeployed
        ? new("https://developer.android.com/studio/command-line/adb")
        : new("https://developer.android.com/tools/releases/platform-tools");
    public static readonly Uri L_CC_LIC = new("https://creativecommons.org/licenses/by-sa/3.0/");
    public static readonly Uri L_APACHE_LIC = new("https://www.apache.org/licenses/LICENSE-2.0");
    public static readonly Uri REPO_RELEASES_URL = new("https://api.github.com/repos/Alex4SSB/ADB-Explorer/releases");
}
