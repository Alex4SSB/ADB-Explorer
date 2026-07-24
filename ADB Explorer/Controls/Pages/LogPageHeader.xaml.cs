using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls.Pages;

public partial class LogPageHeader : UserControl
{
    public LogPageHeader()
    {
        InitializeComponent();

        DataContextChanged += LogPageHeader_DataContextChanged;
    }

    private void LogPageHeader_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LogViewModel oldVm)
        {
            oldVm.LogEntryAdded -= OnLogEntryAdded;
            oldVm.LogCleared -= OnLogCleared;
            oldVm.RefreshControls -= RefreshControls;
        }

        if (e.NewValue is LogViewModel newVm)
        {
            newVm.LogEntryAdded += OnLogEntryAdded;
            newVm.LogCleared += OnLogCleared;
            newVm.RefreshControls += RefreshControls;
        }
    }

    private void OnLogEntryAdded(Log entry)
    {
        if (entry is null)
            return;

        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded || LogTextBox is null)
                return;

            LogTextBox.AppendText(entry.ToString() + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }

    private void OnLogCleared() =>
        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded || LogTextBox is null)
                return;

            LogTextBox.Document.Blocks.Clear();
        });

    private void RefreshControls() =>
        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded || LogControlsPanel is null)
                return;

            LogControlsPanel.Items.Refresh();
        });
}
