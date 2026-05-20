using Microsoft.Extensions.Hosting;
using Wpf.Ui;

namespace ADB_Explorer.Services;

/// <summary>
/// Centralized snackbar service that coordinates file-operation snackbars.
/// </summary>
public class AdbSnackbarService : IHostedService
{
    private readonly HashSet<string> _suppressedDevices = [];
    private readonly FileOpSnackbarService _fileOpSnackbarService;

    public AdbSnackbarService(ISnackbarService snackbarService)
    {
        _fileOpSnackbarService = new(snackbarService, _suppressedDevices);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _fileOpSnackbarService.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _fileOpSnackbarService.StopAsync(cancellationToken);
    }

    public void SubscribeQueue(FileOperationQueue queue) =>
        _fileOpSnackbarService.SubscribeQueue(queue);
}
