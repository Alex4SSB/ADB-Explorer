using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for PasteAndPullTooltip.xaml
/// </summary>
public partial class PasteAndPullTooltip : UserControl
{
    public bool TempHide { get; set; } = false;

    public PasteAndPullTooltip()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Data.Settings.HidePasteNamingInfo = PermanentHideCheckBox.IsChecked is true;
        Visibility = Visibility.Hidden;
    }
}
