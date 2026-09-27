using System.Windows.Media.Animation;

namespace ADB_Explorer.Views.Pages;

public partial class DevicesPageContent : UserControl
{
    public DevicesPageContent()
    {
        Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

        InitializeComponent();
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() => DevicePollingService.RefreshDevices(CancellationToken.None));

        var clockwise = !Data.RuntimeSettings.IsRTL;
        var transform = new RotateTransform();
        RefreshDevicesIcon.RenderTransform = transform;
        RefreshDevicesIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        transform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(clockwise ? 0 : 360, clockwise ? 360 : 0, TimeSpan.FromMilliseconds(200)));
    }
}
