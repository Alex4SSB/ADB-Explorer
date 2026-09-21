using ADB_Explorer.Helpers;
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

        Loaded += (_, _) =>
        {
            if (InstanceHelper.GetInstance(this) is { } instance)
                SetInstance(instance);
        };
    }

    /// <summary>Tooltip content can't inherit the tab, so its bindings go through a proxy pointed at the pane's actions.</summary>
    public void SetInstance(ExplorerInstance? instance)
        => ((BindingProxy)Resources["ActionsProxy"]).Data = instance?.FileList.Actions;
}
