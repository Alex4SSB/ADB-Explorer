using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;
using Microsoft.Extensions.Hosting;

namespace ADB_Explorer.Services;

public class DiskUsagePollingService(IServiceProvider serviceProvider) : BackgroundService
{
    private ExplorerViewModel _explorerViewModel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var interval = Data.FileOpQ?.IsActive is true
                    ? AdbExplorerConst.DISK_USAGE_INTERVAL_ACTIVE
                    : AdbExplorerConst.DISK_USAGE_INTERVAL_IDLE;

                await Task.Delay(interval, stoppingToken);

                _explorerViewModel ??= serviceProvider.GetRequiredService<ExplorerViewModel>();

                // Pull = device→PC = PC disk write; Push = PC→device = PC disk read
                var (pullRate, pushRate) = SyncTransferTracker.Snapshot();

                App.SafeInvoke(() =>
                {
                    _explorerViewModel.AdbWriteRate = (pullRate is > 0 and < AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? pullRate : 0).BytesToSize(true) + "/s";
                    _explorerViewModel.AdbReadRate  = (pushRate is > 0 and < AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? pushRate : 0).BytesToSize(true) + "/s";

                    _explorerViewModel.IsAdbWriteActive = pullRate > AdbExplorerConst.DISK_WRITE_THRESHOLD && pullRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE;
                    _explorerViewModel.IsAdbReadActive  = pushRate > AdbExplorerConst.DISK_READ_THRESHOLD  && pushRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE;
                });
            }
            catch (OperationCanceledException) { }
        }
    }
}
