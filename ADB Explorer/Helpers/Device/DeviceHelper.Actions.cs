namespace ADB_Explorer.Helpers;

public static partial class DeviceHelper
{
    public static void BrowseDeviceAction(LogicalDeviceViewModel device)
    {
        // Opening runs from DeviceToOpen's change handler, which doesn't fire for the same device -
        // e.g. browsing the device the selected tab already shows must still return to Explorer.
        if (Data.DevicesObject.DeviceToOpen?.ID == device.ID)
            OpenDevice(device);
        else
            Data.DevicesObject.DeviceToOpen = device;
    }

    public static void SideloadDeviceAction(LogicalDeviceViewModel device)
    {
        OpenFileDialog dialog = new()
        {
            Title = Strings.Resources.S_SIDELOAD_ROM_TITLE,
            Filter = $"{Strings.Resources.S_ROM_FILE}|*.zip",
            Multiselect = false,
        };

        if (dialog.ShowDialog() is not true)
            return;

        var res = AdbService.ExecuteDeviceAdbCommand(device.ID, "sideload", out string stdout, out string stderr, CancellationToken.None, AdbService.EscapeAdbString(dialog.FileName));
        DialogService.ShowMessage(string.Join('\n', stdout, stderr),
                                  Strings.Resources.S_REBOOT_SIDELOAD,
                                  res == 0 ? DialogService.DialogIcon.Informational
                                           : DialogService.DialogIcon.Critical,
                                  error: res == 0 ? null : DialogError.SideloadFailed);
    }

    private static async void RemoveDeviceAction(DeviceViewModel device)
    {
        var message = device.Type is DeviceType.Emulator
            ? Strings.Resources.S_KILL_EMULATOR
            : Strings.Resources.S_REM_DEVICE;

        var name = device switch
        {
            HistoryDeviceViewModel dev when string.IsNullOrEmpty(dev.DeviceName) => dev.IpAddress,
            HistoryDeviceViewModel dev => dev.DeviceName,
            LogicalDeviceViewModel dev => dev.Name,
            _ => throw new NotImplementedException(),
        };

        var title = device.Type is DeviceType.Emulator
            ? Strings.Resources.S_KILL_EMULATOR_TITLE
            : Strings.Resources.S_REM_DEVICE_TITLE;

        var dialogTask = await DialogService.ShowConfirmation(message, string.Format(title, name));
        if (dialogTask.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
            return;

        if (device.Type is DeviceType.Emulator)
        {
            try
            {
                AdbService.KillEmulator(device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message,
                                          Strings.Resources.S_DISCONN_FAILED_TITLE,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.DisconnectFailed);
                return;
            }
        }
        else if (device.Type is DeviceType.Remote)
        {
            try
            {
                AdbService.DisconnectNetworkDevice(device.ID, CancellationToken.None);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message,
                                          Strings.Resources.S_DISCONN_FAILED_TITLE,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.DisconnectFailed);
                return;
            }
        }
        else if (device.Type is DeviceType.History)
        { } // No additional action is required
        else
        {
            throw new NotImplementedException();
        }

        RemoveDevice(device);
    }

    public static DeviceAction RemoveDeviceCommand(DeviceViewModel device) => new(
            () => device.Type is DeviceType.History
                || !Data.DevicesObject.IsManualPairingInProgress
                    && device.Type is DeviceType.Remote or DeviceType.Emulator,
            () => RemoveDeviceAction(device),
            device.Type switch
            {
                DeviceType.Remote => Strings.Resources.S_REM_DEV,
                DeviceType.Emulator => Strings.Resources.S_REM_EMU,
                DeviceType.History => Strings.Resources.S_REM_HIST_DEV,
                _ => "",
            });

    public static DeviceAction ToggleRootDeviceCommand(LogicalDeviceViewModel device) => new(
        () => device.Root is not RootStatus.Forbidden
            && device.Status is DeviceStatus.Ok
            && device.Type is not DeviceType.Sideload and not DeviceType.Recovery,
        () => ToggleRootAction(device));

    private static async void ToggleRootAction(LogicalDeviceViewModel device)
    {
        bool rootEnabled = device.Root is RootStatus.Enabled;

        await Task.Run(() => device.EnableRoot(!rootEnabled));

        if (device.Root is RootStatus.Forbidden)
        {
            App.SafeInvoke(() => DialogService.ShowMessage(Strings.Resources.S_ROOT_FORBID,
                                                           Strings.Resources.S_ROOT_FORBID_TITLE,
                                                           DialogService.DialogIcon.Critical,
                                                           copyToClipboard: true,
                                                           error: DialogError.RootForbidden));
        }
    }

    public static DeviceAction ConnectDeviceCommand(NewDeviceViewModel device) => new(
        () => {
            if (!device.IsConnectPortValid)
                return false;

            if (!device.IsIpAddressValid && !device.IsHostNameValid)
                return false;

            return !device.IsPairingEnabled
                   || (device.IsPairingCodeValid && device.IsPairingPortValid);
        },
        () => Data.DevicesObject.DeviceToConnect = device);

    public static DeviceAction LaunchWsa(WsaPkgDeviceViewModel device) => new(
        () => device.Status is DeviceStatus.Ok,
        async () =>
        {
            if (Data.Settings.ShowLaunchWsaMessage)
            {
                var result = await DialogService.ShowConfirmation(Strings.Resources.S_WSA_LAUNCH,
                                                                  Strings.Resources.S_WSA_DIALOG_TITLE,
                                                                  primaryText: Strings.Resources.S_BUTTON_LAUNCH,
                                                                  checkBoxText: Strings.Resources.S_DONT_SHOW_AGAIN,
                                                                  icon: DialogService.DialogIcon.Exclamation,
                                                                  censorContent: false);

                Data.Settings.ShowLaunchWsaMessage = !result.Item2;

                if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                    return;
            }

            device.SetLastLaunch();
            device.SetStatus(DeviceStatus.Unauthorized);
            Process.Start($"{AdbExplorerConst.WSA_PROCESS_NAME}.exe");
        });

    public static DeviceAction LaunchEmulator(EmulatorPackageDeviceViewModel device) => new(
        () => device.Status is DeviceStatus.Ok,
        () =>
        {
            try
            {
                device.SetLastLaunch();
                device.SetStatus(DeviceStatus.Unauthorized);
                EmulatorHelper.LaunchAvd(device.AvdName);
            }
            catch (Exception ex)
            {
                device.SetStatus(DeviceStatus.Ok);
                DialogService.ShowMessage(ex.Message,
                                          Strings.Resources.S_EMULATOR_DIALOG_TITLE,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.EmulatorLaunchFailed);
            }
        });

    public static void ConnectDevice(NewDeviceViewModel device)
    {
        Data.DevicesObject.IsManualPairingInProgress = true;
        Data.DevicesObject.CurrentNewDevice = device;

        if (device.IsPairingEnabled)
            PairNewDevice();
        else
            ConnectNewDevice();
    }

    public static void RemoveDevice(DeviceViewModel device)
    {
        switch (device)
        {
            case LogicalDeviceViewModel logical:
                if (logical.IsOpen)
                {
                    Data.DeviceCts.Cancel();
                    Data.DeviceCts.Dispose();
                    Data.DeviceCts = new();

                    ThumbnailService.StopLoading();
                    ApkIconService.CancelPending();
                    DriveHelper.ClearDrives();
                    FileActionLogic.ClearExplorer();
                    Data.FileActions.IsExplorerVisible = false;
                    Data.DirList = null!;
                    Data.DevicesObject.DeviceToOpen = null;
                }

                Data.DevicesObject.UIList.Remove(device);
                DeviceListSetup();

                break;
            case HistoryDeviceViewModel hist:
                Data.DevicesObject.RemoveHistoryDevice(hist);

                break;
            default:
                throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Browses <paramref name="device"/> in the focused pane, which keeps its history - the way the
    /// Devices page's Browse button and the navigation tree's device nodes work.
    /// </summary>
    public static void OpenDevice(LogicalDeviceViewModel device)
        => SwitchTabToDevice(device);

    /// <inheritdoc cref="OpenDevice"/>
    public static void SwitchTabToDevice(LogicalDeviceViewModel device)
    {
        var instance = App.Services.GetService<ExplorerTabsViewModel>()?.EnsureFocusedPane();
        if (instance is null)
            return;

        NavigateTabToDevice(instance, device);
    }

    /// <summary>True while a tab is being pointed at a device - the tree's own selection callbacks must not start another switch.</summary>
    internal static bool IsSwitchingTabDevice { get; private set; }

    /// <summary>Points <paramref name="instance"/> (the active tab) at <paramref name="device"/> and opens it there.</summary>
    public static void NavigateTabToDevice(ExplorerInstance instance, LogicalDeviceViewModel device)
    {
        if (IsSwitchingTabDevice)
            return;

        IsSwitchingTabDevice = true;

        try
        {
            instance.Device = device;
            instance.TracksAppWideCurrentDevice = false;
            instance.FileList.DirList?.Stop();

            OpenDeviceCore(instance, device);
        }
        finally
        {
            IsSwitchingTabDevice = false;
        }
    }

    /// <summary>Opens <paramref name="device"/> in a new tab, at <paramref name="location"/> (or its drive view).</summary>
    public static void OpenDeviceInNewTab(LogicalDeviceViewModel device, AdbLocation? location = null)
    {
        var tabs = App.Services.GetService<ExplorerTabsViewModel>();
        if (tabs is null)
            return;

        IsSwitchingTabDevice = true;

        try
        {
            var instance = tabs.AddDeviceTab(device);
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = location;

            OpenDeviceCore(instance, device);
        }
        finally
        {
            IsSwitchingTabDevice = false;
        }
    }

    private static void OpenDeviceCore(ExplorerInstance? instance, LogicalDeviceViewModel device)
    {
        if (instance is null)
            return;

        Data.DeviceCts.Cancel();
        Data.DeviceCts.Dispose();
        Data.DeviceCts = new();
        ApkIconService.CancelPending();

        Devices.SetOpenDevice(device);
        Data.Files.Device = device;

        Data.CurrentPage.Value = typeof(ExplorerPage);
        Data.RequestExplorer(ExplorerRequest.InitLister);

        FileActionLogic.ClearExplorer();
        Data.FileActions.NotifyAppDriveThumbsLocked();

        InitDevice(instance);
    }
}
