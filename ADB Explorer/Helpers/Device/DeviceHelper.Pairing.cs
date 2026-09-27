namespace ADB_Explorer.Helpers;

public static partial class DeviceHelper
{
    public static async void ListServices(IEnumerable<ServiceSnapshot> snapshots, CancellationToken cancellationToken)
    {
        if (snapshots is null)
            return;

        if (!Data.DevicesObject.ServicesChanged(snapshots))
            return;

        var viewModels = snapshots.Select(s => new ServiceDeviceViewModel(ServiceDevice.From(s)));

        Data.DevicesObject.UpdateServices(viewModels);

        var qrClass = Data.MdnsService?.QrClass;
        if (qrClass is null)
            return;

        var qrServices = Data.DevicesObject.ServiceDeviceViewModels.Where(service =>
            service.MdnsType == ServiceDevice.PairingMode.QrCode
            && service.ID == qrClass.ServiceName);

        if (qrServices.Any())
        {
            var qrService = qrServices.First();
            if (!qrService.IsPairingInProgress)
                await PairService(qrService, cancellationToken);
        }
    }

    public static async Task<bool> PairService(ServiceDeviceViewModel service, CancellationToken cancellationToken)
    {
        var code = service.MdnsType == ServiceDevice.PairingMode.QrCode
            ? Data.MdnsService?.QrClass?.Password
            : service.PairingCode;

        if (string.IsNullOrEmpty(code) || service.IsPairingInProgress)
            return false;

        App.SafeInvoke(service.BeginPairing);

        var success = false;
        string? error = null;

        try
        {
            await Task.Run(() => AdbService.PairNetworkDevice(service.ID, code, cancellationToken), cancellationToken);
            success = true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            App.SafeInvoke(() => service.EndPairing(success, error));
        }

        return success;
    }

    public static async void PairNewDevice()
    {
        var dev = Data.DevicesObject.DeviceToConnect;
        if (dev is null)
            return;

        await Task.Run(() =>
        {
            try
            {
                AdbService.PairNetworkDevice(dev.PairingAddress, dev.PairingCode, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                App.SafeInvoke(() => DialogService.ShowMessage(ex.Message,
                                                               Strings.Resources.S_PAIR_ERR_TITLE,
                                                               DialogService.DialogIcon.Critical,
                                                               copyToClipboard: true,
                                                               error: DialogError.PairingFailed));
                return false;
            }
        }).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            App.SafeInvoke(() =>
            {
                if (t.Result)
                    ConnectNewDevice();

                Data.DevicesObject.DeviceToConnect = null;
                Data.DevicesObject.IsManualPairingInProgress = false;
            });
        });
    }

    public static async void ConnectNewDevice()
    {
        var dev = Data.DevicesObject.DeviceToConnect;
        if (dev is null)
            return;

        await Task.Run(() =>
        {
            try
            {
                AdbService.ConnectNetworkDevice(dev.ConnectAddress, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                if (AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(dev.IpAddress))
                    return true;

                if (ex.Message.Contains("failed to connect to " + dev.ConnectAddress)
                    && !dev.IsPairingEnabled)
                {
                    Data.DevicesObject.CurrentNewDevice.EnablePairing();
                }
                else
                    App.SafeInvoke(() => DialogService.ShowMessage(ex.Message,
                                                                   Strings.Resources.S_FAILED_CONN_TITLE,
                                                                   DialogService.DialogIcon.Critical,
                                                                   copyToClipboard: true,
                                                                   error: DialogError.ConnectionFailed));

                return false;
            }
        }).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            App.SafeInvoke(() =>
            {
                if (t.Result)
                {
                    string newDeviceAddress = "";
                    var newDevice = Data.DevicesObject.DeviceToConnect ?? Data.DevicesObject.CurrentNewDevice;

                    if (newDevice.Type is DeviceType.New && !AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(newDevice.IpAddress))
                    {
                        if (Data.Settings.SaveDevices)
                            Data.DevicesObject.AddHistoryDevice(HistoryDeviceViewModel.FromNewDevice(dev));

                        newDeviceAddress = dev.ConnectAddress;
                        ((NewDeviceViewModel)newDevice).ClearDevice();
                    }
                    else if (newDevice.Type is DeviceType.History)
                    {
                        newDeviceAddress = ((HistoryDeviceViewModel)newDevice).ConnectAddress;

                        // In case user has changed the port of the history device
                        if (Data.Settings.SaveDevices)
                            Data.DevicesObject.StoreHistoryDevices();
                    }

                    DeviceListSetup(newDeviceAddress);
                }

                Data.DevicesObject.DeviceToConnect = null;
                Data.DevicesObject.IsManualPairingInProgress = false;
            });
        });
    }
}
