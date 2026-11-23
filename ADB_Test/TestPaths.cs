using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ADB_Test;

/// <summary>Per-machine paths for tests, read from gitignored <c>.path</c> files next to the csproj.</summary>
public static class TestPaths
{
    /// <summary>The adb.exe in an SDK folder such as <c>platform-tools_37.0.1</c>; skips the test when there's none.</summary>
    public static string Adb(string toolsFolder)
    {
        // Embedded at build time; see the AssemblyMetadata item in the csproj
        var pathFile = typeof(TestPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "AndroidSdkPathFile").Value!;

        if (!File.Exists(pathFile))
            Assert.Inconclusive($"Create {pathFile} with the path of your Android SDK folder.");

        var adb = Path.Combine(File.ReadAllText(pathFile).Trim(), toolsFolder, "adb.exe");
        if (!File.Exists(adb))
            Assert.Inconclusive($"{adb} not found.");

        return adb;
    }
}
