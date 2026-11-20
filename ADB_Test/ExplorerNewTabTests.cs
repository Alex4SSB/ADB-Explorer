#nullable enable

using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vanara.PInvoke;

namespace ADB_Test;

/// <summary>A device dropped onto File Explorer opens in a new tab of it, through File Explorer's undocumented new tab command.</summary>
[TestClass]
public class ExplorerNewTabTests
{
    [TestMethod]
    [Timeout(30_000)]
    public void OpenInNewTab_AddsATabAtThePath()
    {
        var first = Path.Combine(Path.GetTempPath(), $"adbx-tab-a-{Guid.NewGuid():N}");
        var second = Path.Combine(Path.GetTempPath(), $"adbx-tab-b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", first);
            Assert.IsTrue(WaitFor(() => WindowsAt(first).Count > 0, 10), "File Explorer didn't open");
            var handle = new HWND(new IntPtr(Convert.ToInt64(((dynamic)WindowsAt(first)[0]).HWND)));
            Assert.IsTrue(DeviceMount.IsExplorerWindow(handle), "the window should be taken for File Explorer's");

            var openInNewTab = typeof(DeviceMount).GetMethod("OpenInNewTab", BindingFlags.NonPublic | BindingFlags.Static)!;
            var tab = openInNewTab.Invoke(null, [handle, second]);

            if (tab is null && Environment.OSVersion.Version.Build < 22621)
                Assert.Inconclusive("File Explorer has no tabs before Windows 11 22H2.");

            Assert.IsNotNull(tab, "no new tab came");
            var atSecond = WindowsAt(second);
            Assert.AreEqual(1, atSecond.Count, "the new tab should show the path");
            Assert.AreEqual(handle.DangerousGetHandle(), new IntPtr(Convert.ToInt64(((dynamic)atSecond[0]).HWND)), "the path should open in a tab of the same window, not a window of its own");
        }
        finally
        {
            foreach (var window in WindowsAt(first).Concat(WindowsAt(second)))
            {
                try
                {
                    ((dynamic)window).Quit();
                }
                catch (Exception)
                { }
            }

            Directory.Delete(first, true);
            Directory.Delete(second, true);
        }
    }

    private static List<object> WindowsAt(string folder)
    {
        List<object> result = [];
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        foreach (var window in shell.Windows())
        {
            string? path = null;
            try
            {
                path = ((dynamic)window).Document.Folder.Self.Path;
            }
            catch (Exception)
            { }

            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
                result.Add(window);
        }

        return result;
    }

    private static bool WaitFor(Func<bool> condition, int seconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds)
                return false;

            System.Threading.Thread.Sleep(200);
        }

        return true;
    }
}
