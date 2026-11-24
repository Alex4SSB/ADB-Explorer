using System.Globalization;
using System.Linq;
using System.Reflection;
using ADB_Explorer.Helpers;
using ADB_Explorer.Platform;
using ADB_Explorer.Services;
using ADB_Explorer.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class FormattedStringsTests
{
    [TestMethod]
    public void Formatted_FillsFileExplorerName_InEveryLanguage()
    {
        var properties = typeof(Formatted).GetProperties(BindingFlags.Public | BindingFlags.Static);

        var original = Resources.Culture;
        try
        {
            foreach (var language in Plurals.Languages().Append("en"))
            {
                Resources.Culture = CultureInfo.GetCultureInfo(language);

                foreach (var property in properties)
                {
                    var text = (string)property.GetValue(null)!;

                    Assert.IsFalse(string.IsNullOrEmpty(text), $"{language}: {property.Name} is empty");
                    Assert.DoesNotContain("{0}", text, $"{language}: {property.Name} has an unfilled placeholder");
                }
            }

            Resources.Culture = CultureInfo.GetCultureInfo("en");
            Assert.AreEqual("Open in File Explorer", Formatted.S_MENU_OPEN_IN_WINDOWS_EXPLORER);
        }
        finally
        {
            Resources.Culture = original;
        }
    }

    [TestMethod]
    public void WindowsShell_ReadsFileExplorerName()
    {
        var name = WindowsPlatformShell.ReadFileManagerName();

        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
        Assert.IsNotNull(WindowsPlatformShell.ReadDisplayCulture());
    }

    [TestMethod]
    public void Formatted_UsesShellName_OnlyInMatchingLanguage()
    {
        var originalShell = PlatformServices.Shell;
        var originalCulture = Resources.Culture;
        try
        {
            PlatformServices.Shell = new FakeShell("Проводник", "ru-RU");

            Resources.Culture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.AreEqual("Проводник", Formatted.FileManagerName);

            Resources.Culture = CultureInfo.GetCultureInfo("ru");
            Assert.AreEqual("Проводник", Formatted.FileManagerName);

            Resources.Culture = CultureInfo.GetCultureInfo("en");
            Assert.AreEqual("File Explorer", Formatted.FileManagerName);
            Assert.AreEqual("Open in File Explorer", Formatted.S_MENU_OPEN_IN_WINDOWS_EXPLORER);

            PlatformServices.Shell = null;
            Assert.AreEqual("File Explorer", Formatted.FileManagerName);
        }
        finally
        {
            PlatformServices.Shell = originalShell;
            Resources.Culture = originalCulture;
        }
    }

    [TestMethod]
    public void IsSameOrParent_MatchesParentsNotSiblings()
    {
        static bool Match(string app, string name)
            => CultureHelper.IsSameOrParent(CultureInfo.GetCultureInfo(app), CultureInfo.GetCultureInfo(name));

        Assert.IsTrue(Match("en-US", "en-US"));
        Assert.IsTrue(Match("en", "en-GB"));
        Assert.IsTrue(Match("pt", "pt-PT"));
        Assert.IsFalse(Match("pt-BR", "pt-PT"));
        Assert.IsFalse(Match("zh-TW", "zh-CN"));
        Assert.IsFalse(Match("ru-RU", "ru"));
        Assert.IsFalse(Match("en", "ru-RU"));
    }

    /// <summary>Like Windows: knows the name in one language only.</summary>
    private sealed class FakeShell(string fileManagerName, string culture) : IPlatformShell
    {
        public string GetFileManagerName(CultureInfo wanted)
            => CultureHelper.IsSameOrParent(wanted, CultureInfo.GetCultureInfo(culture)) ? fileManagerName : null;

        public string AdbExecutableName => "adb";

        public void OpenFolder(string folderPath) { }

        public void RevealFile(string filePath) { }

        public void OpenWithDefaultApp(string path) { }

        public void KillProcesses(string processName) { }
    }
}
