using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ADB_Explorer.Helpers
{
    public static class SelectionHelper
    {
        public enum MenuType
        {
            Context,
            Submenu,
            Menubar,
        }

        public static MenuType GetMenuType(UIElement control) =>
            (MenuType)control.GetValue(MenuTypeProperty);

        public static void SetMenuType(UIElement control, MenuType value) =>
            control.SetValue(MenuTypeProperty, value);

        public static readonly DependencyProperty MenuTypeProperty =
            DependencyProperty.RegisterAttached(
                "MenuType",
                typeof(MenuType),
                typeof(SelectionHelper),
                null);

        public static bool GetIsMenuOpen(UIElement control) =>
            (bool)control.GetValue(IsMenuOpenProperty);

        public static void SetIsMenuOpen(UIElement control, bool value) =>
            control.SetValue(IsMenuOpenProperty, value);

        public static readonly DependencyProperty IsMenuOpenProperty =
            DependencyProperty.RegisterAttached(
                "IsMenuOpen",
                typeof(bool),
                typeof(SelectionHelper),
                null);

        public static int GetFirstSelectedIndex(UIElement control) =>
            (int)control.GetValue(FirstSelectedIndexProperty);

        public static void SetFirstSelectedIndex(UIElement control, int value) =>
            control.SetValue(FirstSelectedIndexProperty, value);

        public static readonly DependencyProperty FirstSelectedIndexProperty =
            DependencyProperty.RegisterAttached(
                "FirstSelectedIndex",
                typeof(int),
                typeof(SelectionHelper),
                null);

        public static int GetCurrentSelectedIndex(UIElement control) =>
            (int)control.GetValue(CurrentSelectedIndexProperty);

        public static void SetCurrentSelectedIndex(UIElement control, int value) =>
            control.SetValue(CurrentSelectedIndexProperty, value);

        public static readonly DependencyProperty CurrentSelectedIndexProperty =
            DependencyProperty.RegisterAttached(
                "CurrentSelectedIndex",
                typeof(int),
                typeof(SelectionHelper),
                null);

        public static bool GetSelectionInProgress(UIElement control) =>
            (bool)control.GetValue(SelectionInProgressProperty);

        public static void SetSelectionInProgress(UIElement control, bool value) =>
            control.SetValue(SelectionInProgressProperty, value);

        public static readonly DependencyProperty SelectionInProgressProperty =
            DependencyProperty.RegisterAttached(
                "SelectionInProgress",
                typeof(bool),
                typeof(SelectionHelper),
                null);

        public static void MultiSelect(this DataGrid dataGrid, Key key)
        {
            SetSelectionInProgress(dataGrid, true);

            var firstIndex = GetFirstSelectedIndex(dataGrid);
            var currentIndex = GetCurrentSelectedIndex(dataGrid);

            if (key == Key.Up)
                currentIndex--;
            else if (key == Key.Down)
                currentIndex++;

            dataGrid.UnselectAll();

            var index1 = firstIndex < currentIndex ? firstIndex : currentIndex;
            var index2 = firstIndex < currentIndex ? currentIndex : firstIndex;

            for (int i = index1; i <= index2; i++)
            {
                if (i < 0 || i >= dataGrid.Items.Count)
                    continue;

                dataGrid.SelectedItems.Add(dataGrid.Items[i]);
            }

            if (currentIndex >= 0 && currentIndex < dataGrid.Items.Count)
                dataGrid.ScrollIntoView(dataGrid.Items[currentIndex]);

            if (currentIndex >= 0 && currentIndex < dataGrid.Items.Count)
                SetCurrentSelectedIndex(dataGrid, currentIndex);

            SetSelectionInProgress(dataGrid, false);
        }

        public static void SingleSelect(this DataGrid dataGrid, Key key)
        {
            if (dataGrid.Items.Count == 1 && dataGrid.SelectedIndex == -1)
            {
                dataGrid.SelectedIndex = 0;
                return;
            }

            dataGrid.SelectedIndex = GetCurrentSelectedIndex(dataGrid);

            if (key == Key.Up)
            {
                if (dataGrid.SelectedIndex > -1)
                    dataGrid.SelectedIndex--;
                else
                    dataGrid.SelectedIndex = dataGrid.Items.Count - 1;
            }
            else if (key == Key.Down)
            {
                if (dataGrid.SelectedIndex < 0 || dataGrid.Items.IndexOf(dataGrid.SelectedItems[^1]) < dataGrid.Items.Count - 1)
                    dataGrid.SelectedIndex++;
                else
                    dataGrid.SelectedIndex = -1;
            }

            SetCurrentSelectedIndex(dataGrid, dataGrid.SelectedIndex);
            if (dataGrid.SelectedIndex > -1)
                dataGrid.ScrollIntoView(dataGrid.SelectedItem);
        }
    }
}
