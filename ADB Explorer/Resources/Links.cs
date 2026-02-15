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
    public static readonly Uri MODERN_WPF = new("https://github.com/Kinnara/ModernWpf");
    public static readonly Uri ICONS8 = new("https://icons8.com");
    public static readonly Uri ADB_EXPLORER_PRIVACY = new("https://github.com/Alex4SSB/ADB-Explorer/blob/master/Privacy.md");
    public static readonly Uri ADB_EXPLORER_GITHUB = new("https://github.com/Alex4SSB/ADB-Explorer");
    public static readonly Uri LGPL3 = new("https://opensource.org/license/lgpl-3-0");
    public static readonly Uri SPONSOR = new("https://github.com/sponsors/Alex4SSB");
    public static readonly Uri WEBLATE = new("https://hosted.weblate.org/engage/adb-explorer/");
    public static readonly Uri ADVANCED_SHARP_ADB = new("https://github.com/SharpAdb/AdvancedSharpAdbClient");
    public static readonly Uri REPO_ADB_VER_LIST = new("https://raw.githubusercontent.com/Alex4SSB/ADB-Explorer/master/OFFICIAL_ADB_VERSIONS.md");
}
