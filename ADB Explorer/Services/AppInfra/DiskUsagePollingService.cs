using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Windows;
using Microsoft.Extensions.Hosting;

namespace ADB_Explorer.Services;

public class DiskUsagePollingService(IServiceProvider serviceProvider) : BackgroundService
{
    private MainWindowViewModel _mainWindowVM;

    public static DateTime LastServerResponse { get; set; } = DateTime.Now;

    public static bool ServerUnresponsive => Data.Settings.PollDevices && DateTime.Now.Subtract(LastServerResponse) > AdbExplorerConst.SERVER_RESPONSE_TIMEOUT;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ADBService.CommandActiveChanged += OnCommandActiveChanged;
        stoppingToken.Register(() => ADBService.CommandActiveChanged -= OnCommandActiveChanged);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, stoppingToken);

                _mainWindowVM ??= serviceProvider.GetRequiredService<MainWindowViewModel>();

                // Pull = device→PC = PC disk write; Push = PC→device = PC disk read
                var (pullRate, pushRate) = SyncTransferTracker.Snapshot();

                App.SafeInvoke(() =>
                {
                    _mainWindowVM.ServerUnresponsive = ServerUnresponsive;
                    _mainWindowVM.LastResponse = $"{Strings.Resources.S_TOOLTIP_UNRESPONSIVE}: {UnitConverter.ToTime(DateTime.Now.Subtract(LastServerResponse).TotalSeconds, digits: 0)}";
                    
                    _mainWindowVM.AdbWriteRate = (pullRate is > 0 and < AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? pullRate : 0).BytesToSize(true) + "/s";
                    _mainWindowVM.AdbReadRate  = (pushRate is > 0 and < AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? pushRate : 0).BytesToSize(true) + "/s";

                    if (ServerUnresponsive)
                    {
                        _mainWindowVM.IsAdbWriteActive = false;
                        _mainWindowVM.IsAdbReadActive  = false;
                    }
                    else
                    {
                        var isCommandActive = ADBService.IsCommandActive;
                        _mainWindowVM.IsAdbWriteActive = isCommandActive || (pullRate > AdbExplorerConst.DISK_WRITE_THRESHOLD && pullRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE);
                        _mainWindowVM.IsAdbReadActive  = isCommandActive || (pushRate > AdbExplorerConst.DISK_READ_THRESHOLD  && pushRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE);
                    }
                });
            }
            catch (OperationCanceledException) { }
        }
    }

    private void OnCommandActiveChanged(bool isActive)
    {
        if (!isActive || ServerUnresponsive)
            return;

        _mainWindowVM ??= serviceProvider.GetRequiredService<MainWindowViewModel>();

        App.SafeBeginInvoke(() =>
        {
            _mainWindowVM.IsAdbWriteActive = true;
            _mainWindowVM.IsAdbReadActive  = true;
        }, DispatcherPriority.Background);

        Task.Delay(150).ContinueWith(_ =>
        {
            if (!ADBService.IsCommandActive)
            {
                App.SafeBeginInvoke(() =>
                {
                    _mainWindowVM.IsAdbReadActive = false;
                }, DispatcherPriority.Background);
            }
        });
    }
}
