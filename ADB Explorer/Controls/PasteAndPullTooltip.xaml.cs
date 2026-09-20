using ADB_Explorer.Helpers;

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

        // Tooltip content can't inherit the tab, so its bindings go through this proxy instead.
        Loaded += (_, _) =>
        {
            if (InstanceHelper.GetInstance(this) is { } instance)
                ((BindingProxy)Resources["ActionsProxy"]).Data = instance.FileList.Actions;
        };
    }
}
