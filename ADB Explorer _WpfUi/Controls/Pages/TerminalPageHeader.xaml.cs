using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for TerminalPageHeader.xaml
/// </summary>
public partial class TerminalPageHeader : UserControl
{
    public TerminalPageHeader()
    {
        InitializeComponent();
    }

    private TerminalViewModel ViewModel => (TerminalViewModel)DataContext;

    private void Input_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter)
            return;

        string stdout, stderr;

        if (ViewModel.SelectedDevice is LogicalDeviceViewModel selectedDevice)
        {
            ADBService.ExecuteDeviceAdbShellCommand(selectedDevice.ID,
                "",
                out stdout,
                out stderr,
                CancellationToken.None,
                Input.Text);
        }
        else if (ViewModel.SelectedDevice is AdbTerminalDevice)
        {
            ADBService.ExecuteAdbCommand("",
                out stdout,
                out stderr,
                CancellationToken.None,
                Input.Text);
        }
        else
        {
            return;
        }

        StdOut.Document.Blocks.Clear();
        StdOut.AppendText(stdout);
        StdErr.Text = stderr;
    }
}
