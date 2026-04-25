using Microsoft.Extensions.Hosting;
using Wpf.Ui;

namespace ADB_Explorer.Services;

/// <summary>
/// Centralized snackbar service that coordinates both file-operation and thumbnail snackbars.
/// </summary>
public class AdbSnackbarService : IHostedService
{
    private readonly HashSet<string> _suppressedDevices = [];
    private readonly FileOpSnackbarService _fileOpSnackbarService;
    private readonly ThumbnailSnackbarService _thumbnailSnackbarService;

    public AdbSnackbarService(ISnackbarService snackbarService)
    {
        _fileOpSnackbarService = new(snackbarService, _suppressedDevices);
        _thumbnailSnackbarService = new(snackbarService, _suppressedDevices);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _fileOpSnackbarService.StartAsync(cancellationToken);
        await _thumbnailSnackbarService.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _fileOpSnackbarService.StopAsync(cancellationToken);
        await _thumbnailSnackbarService.StopAsync(cancellationToken);
    }

    public void SubscribeQueue(FileOperationQueue queue) =>
        _fileOpSnackbarService.SubscribeQueue(queue);
}
