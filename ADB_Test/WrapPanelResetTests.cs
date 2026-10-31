#nullable enable

using ADB_Explorer.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace ADB_Test;

/// <summary>
/// The icon view's <see cref="AdbVirtualizingWrapPanel"/> after a sort, which resets every container:
/// the top rows come back in the first layout pass, and the rest of the viewport follows without blocking it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WrapPanelResetTests
{
    public class Item(int i)
    {
        public string Name { get; } = $"file_{(i * 7919) % 100000:D6}";
    }

    [TestMethod]
    public void Sort_RealizesTopRowsFirst_ThenFillsViewport()
    {
        RunSta(() =>
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());

            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(
                new ObservableCollection<Item>(Enumerable.Range(0, 2000).Select(i => new Item(i))));
            view.SortDescriptions.Add(new(nameof(Item.Name), ListSortDirection.Ascending));

            var list = CreateIconList(app, view);
            var window = new Window { Width = 900, Height = 700, Content = list };
            window.Show();
            Pump(DispatcherPriority.ContextIdle);

            var panel = list.WrapPanel;
            var fullViewport = panel.Children.Count;
            var rowsInViewport = fullViewport / panel.ItemsPerRowCount;
            Assert.IsTrue(rowsInViewport > 2, $"Viewport too small for the test: {rowsInViewport} rows.");

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new(nameof(Item.Name), ListSortDirection.Descending));
            }

            // Layout runs at Render priority, above Loaded; the remaining rows are added at Background
            Pump(DispatcherPriority.Loaded);
            Assert.AreEqual(2 * panel.ItemsPerRowCount, panel.Children.Count, "Only the top rows should be realized right after the sort.");

            Pump(DispatcherPriority.ContextIdle);
            Assert.AreEqual(fullViewport, panel.Children.Count, "The whole viewport should be realized once the UI is idle.");
            Assert.AreSame(view.GetItemAt(0), list.ItemContainerGenerator.ItemFromContainer(panel.Children[0]));

            window.Close();
            app.Shutdown();
        });
    }

    private static AdbListView CreateIconList(Application app, ICollectionView view)
    {
        var panel = new FrameworkElementFactory(typeof(AdbVirtualizingWrapPanel));
        panel.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        panel.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);

        var tile = new FrameworkElementFactory(typeof(Border));
        tile.SetValue(FrameworkElement.WidthProperty, 100.0);
        tile.SetValue(FrameworkElement.HeightProperty, 120.0);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(Item.Name)));
        tile.AppendChild(label);

        return new AdbListView
        {
            Style = (Style)app.FindResource(typeof(Wpf.Ui.Controls.ListView)),
            ItemsPanel = new ItemsPanelTemplate(panel),
            ItemTemplate = new DataTemplate { VisualTree = tile },
            ItemsSource = view,
        };
    }

    /// <summary>Runs the dispatcher until everything queued above <paramref name="priority"/> is done.</summary>
    private static void Pump(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(priority, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            throw new AssertFailedException(error.ToString());
    }
}
