using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for PaneStatus.xaml. The item count, selection and paste / pull warnings of
/// the explorer pane in <see cref="Instance"/>, whichever pane that is.
/// </summary>
public partial class PaneStatus : UserControl
{
    public static readonly DependencyProperty InstanceProperty =
        DependencyProperty.Register(
            nameof(Instance),
            typeof(ExplorerInstance),
            typeof(PaneStatus),
            new PropertyMetadata(null, (d, e) => ((PaneStatus)d).OnInstanceChanged((ExplorerInstance?)e.NewValue)));

    public ExplorerInstance? Instance
    {
        get => (ExplorerInstance?)GetValue(InstanceProperty);
        set => SetValue(InstanceProperty, value);
    }

    private readonly PaneStatusViewModel _status = new();

    public PaneStatus()
    {
        InitializeComponent();

        DataContext = _status;

        Loaded += (_, _) => _status.Attach();
        Unloaded += (_, _) => _status.Detach();
    }

    private void OnInstanceChanged(ExplorerInstance? instance)
    {
        _status.Instance = instance;

        InstanceHelper.SetInstance(this, instance);
        PasteTooltip.SetInstance(instance);
    }
}
