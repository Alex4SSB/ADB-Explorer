using ADB_Explorer.Models;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Carries the owning tab's <see cref="ExplorerInstance"/> down an Explorer header's tree, so
/// shared styles read per-tab state instead of the active-tab-only <c>Data.*</c> statics.
/// </summary>
public static class InstanceHelper
{
    public static readonly DependencyProperty InstanceProperty =
        DependencyProperty.RegisterAttached(
            "Instance",
            typeof(ExplorerInstance),
            typeof(InstanceHelper),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static ExplorerInstance? GetInstance(DependencyObject element) =>
        (ExplorerInstance?)element.GetValue(InstanceProperty);

    public static void SetInstance(DependencyObject element, ExplorerInstance? value) =>
        element.SetValue(InstanceProperty, value);
}
