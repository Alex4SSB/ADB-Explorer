using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

/// <summary>
/// One tab's full-width navigation row: the navigation toolbar, the navigation box and the search box.
/// It lives above the pane and the page, so it stays put whichever page the tab is showing.
/// </summary>
public partial class ExplorerNavBar : UserControl
{
    internal ExplorerInstance Instance { get; }

    /// <summary>The pointer entered the bar - the explorer header drops a stale mouse-down point on it.</summary>
    internal event EventHandler? PointerEntered;

    /// <summary>A click landed on the bar itself rather than on the box or a button.</summary>
    internal event EventHandler? BackgroundClicked;

    public ExplorerNavBar(ExplorerInstance instance)
    {
        Instance = instance;
        InstanceHelper.SetInstance(this, instance);

        InitializeComponent();

        ((BindingProxy)Resources["InstanceProxy"]).Data = instance;

        NavigationBox.Initialize(instance);
        SearchBox.Initialize(instance);

        // Built per tab, not bound to a shared static list - see NavigationToolBar.Build's comment.
        NavigationToolBar.ItemsSource = ADB_Explorer.Services.NavigationToolBar.Build(instance);
    }

    private void NavBar_MouseEnter(object sender, MouseEventArgs e) => PointerEntered?.Invoke(this, EventArgs.Empty);

    private void NavBar_MouseDown(object sender, MouseButtonEventArgs e) => BackgroundClicked?.Invoke(this, EventArgs.Empty);
}
