#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using AdvancedSharpAdbClient.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

/// <summary>Open in new tab leaves the user where they are, as a browser does: the tab opens its device once switched to.</summary>
[TestClass]
public class BackgroundTabTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings ??= new AppSettings { KillAdbOnExit = false };
    }

    private static LogicalDeviceViewModel Device(string serial, DeviceStatus status)
        => new(LogicalDevice.From(new DeviceSnapshot(serial, "Model", status, DeviceType.Local, RootStatus.Unchecked, "",
                                                     new DeviceData { Serial = serial, State = DeviceState.Online })));

    [TestMethod]
    public void BackgroundTab_StaysBehindTheActiveTab()
    {
        var previous = Data.ActiveExplorerInstance;
        try
        {
            var tabs = new ExplorerTabsViewModel();
            var active = tabs.EnsureActiveTab();

            var device = Device("bg-tab-test", DeviceStatus.Ok);
            var background = tabs.AddDeviceTab(device, activate: false);
            background.DeferredLocation = new AdbLocation(Navigation.SpecialLocation.DriveView);

            Assert.AreSame(active, tabs.ActiveTab, "the tab the user is on should stay active");
            Assert.HasCount(2, tabs.Tabs);
            Assert.IsNull(background.History.Current, "nothing should be opened in it yet");
            Assert.AreEqual(device.Name, background.TabDisplayName, "it should be labeled by where it opens");
        }
        finally
        {
            Data.ActiveExplorerInstance = previous;
        }
    }

    [TestMethod]
    public void NewTab_OpensRightAfterTheActiveTab_OrWhereDropped()
    {
        var previous = Data.ActiveExplorerInstance;
        try
        {
            var tabs = new ExplorerTabsViewModel();
            var first = tabs.EnsureActiveTab();
            var device = Device("tab-order-test", DeviceStatus.Ok);
            var last = tabs.AddDeviceTab(device, activate: false, index: 1);

            var background = tabs.AddDeviceTab(device, activate: false);
            Assert.AreEqual(1, tabs.Tabs.IndexOf(background), "a tab opened from an item should follow the active one");
            Assert.AreSame(last, tabs.Tabs[^1]);

            var dropped = tabs.AddDeviceTab(device, activate: false, index: 0);
            Assert.AreEqual(0, tabs.Tabs.IndexOf(dropped), "a dropped tab should go where it was dropped");
            Assert.AreSame(first, tabs.ActiveTab);
        }
        finally
        {
            Data.ActiveExplorerInstance = previous;
        }
    }

    [TestMethod]
    public void PageTab_OpensInTheBackground_UnlessDropped()
    {
        var previous = Data.ActiveExplorerInstance;
        try
        {
            var tabs = new ExplorerTabsViewModel();
            var first = tabs.EnsureActiveTab();
            tabs.AddDeviceTab(Device("page-tab-test", DeviceStatus.Ok), activate: false);

            tabs.OpenPageInNewTab(typeof(ADB_Explorer.Views.Pages.SettingsPage));
            var settings = tabs.Tabs[1];
            Assert.AreSame(first, tabs.ActiveTab, "a page opened from its item should stay behind");
            Assert.AreEqual(typeof(ADB_Explorer.Views.Pages.SettingsPage), settings.History.Current?.PageType);
            Assert.HasCount(3, tabs.Tabs);

            tabs.OpenPageInNewTab(typeof(ADB_Explorer.Views.Pages.DevicesPage), index: 3);
            Assert.AreSame(tabs.Tabs[3], tabs.ActiveTab, "a dropped page should open where dropped, switched to");
            Assert.AreEqual(typeof(ADB_Explorer.Views.Pages.DevicesPage), tabs.ActiveTab?.History.Current?.PageType);
        }
        finally
        {
            Data.ActiveExplorerInstance = previous;
        }
    }

    [TestMethod]
    public void BackgroundTab_WaitsForItsDeviceWhenSwitchedTo()
    {
        var previous = Data.ActiveExplorerInstance;
        try
        {
            var tabs = new ExplorerTabsViewModel();
            tabs.EnsureActiveTab();

            // Gone before it was switched to: it keeps where it opens, for when the device is back
            var background = tabs.AddDeviceTab(Device("bg-tab-offline", DeviceStatus.Offline), activate: false);
            var location = new AdbLocation("/sdcard/DCIM");
            background.DeferredLocation = location;

            tabs.ActiveTab = background;

            Assert.AreSame(background, tabs.ActiveTab);
            Assert.AreSame(location, background.DeferredLocation, "an offline device shouldn't be opened");
        }
        finally
        {
            Data.ActiveExplorerInstance = previous;
        }
    }
}
