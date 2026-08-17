using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

/// <summary>
/// Device-free checks for <see cref="ADBService.EscapeAdbShellString"/>.
/// Live <c>adb shell</c> coverage is in <see cref="ShellEscapeEmulatorTests"/>.
/// </summary>
[TestClass]
public class ShellEscapeTests
{
    private const string IncrementalAppDir =
        "/data/app/~~YpQ0dzQS2vI-67ptyyetpg==/com.runbuddy.prod-929EwMlUK25eumI8WactXw==";

    [TestMethod]
    public void EscapeAdbShellString_DoesNotBackslashTildeInIncrementalAppDir()
    {
        var escaped = ADBService.EscapeAdbShellString(IncrementalAppDir);

        Assert.DoesNotContain(@"\~", escaped);
        StringAssert.Contains(escaped, "~~YpQ0dzQS2vI-67ptyyetpg==");
        Assert.StartsWith("\"", escaped);
        Assert.EndsWith("\"", escaped);
    }

    [TestMethod]
    public void EscapeAdbShellString_ShC_DoesNotBackslashTildeInTarCScript()
    {
        var script = AppBackupHelper.BuildCreateArchiveScript(
            "tar",
            "/data/local/tmp/guid.tar.gz",
            IncrementalAppDir,
            ["base.apk"],
            null);
        var shC = ADBService.EscapeAdbShellString(script);

        Assert.DoesNotContain(@"\~", script);
        Assert.DoesNotContain(@"\~", shC);
        StringAssert.Contains(script, IncrementalAppDir);
    }

    [TestMethod]
    public void EscapeAdbShellString_StillEscapesDollarAndBacktick()
    {
        var escaped = ADBService.EscapeAdbShellString("a$b`c");

        StringAssert.Contains(escaped, @"\$");
        StringAssert.Contains(escaped, @"\`");
    }
}
