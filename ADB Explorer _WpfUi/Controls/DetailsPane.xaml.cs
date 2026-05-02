using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsPane.xaml
/// </summary>
public partial class DetailsPane : UserControl
{
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register("IsOpen", typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public string? EditorText
    {
        get => (string?)GetValue(EditorTextProperty);
        set => SetValue(EditorTextProperty, value);
    }

    public static readonly DependencyProperty EditorTextProperty =
        DependencyProperty.Register("EditorText", typeof(string),
          typeof(DetailsPane), new PropertyMetadata(null));

    public bool IsEditorFocused
    {
        get => (bool)GetValue(IsEditorFocusedProperty);
        set => SetValue(IsEditorFocusedProperty, value);
    }

    public static readonly DependencyProperty IsEditorFocusedProperty =
        DependencyProperty.Register("IsEditorFocused", typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public IEnumerable<FileClass> SelectedFiles
    {
        get => (IEnumerable<FileClass>)GetValue(SelectedFilesProperty);
        set => SetValue(SelectedFilesProperty, value);
    }

    public static readonly DependencyProperty SelectedFilesProperty =
        DependencyProperty.Register("SelectedFiles", typeof(IEnumerable<FileClass>),
          typeof(DetailsPane), new PropertyMetadata(Array.Empty<FileClass>(), OnSelectedFilesChanged));

    private static void OnSelectedFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DetailsPane)d;
        var files = (IEnumerable<FileClass>)e.NewValue;

        if (files.Count() != 1)
        {
            control.EditorText = null;

            return;
        }

        var file = files.First();
        if (Data.Settings.EnableFilePreview
            && !Data.FileActions.IsRecycleBin
            && file.Type is AbstractFile.FileType.File
            && !file.IsApk
            && !file.IsLink
            && file.Size / 1000 < Data.Settings.MaxPreviewFileSize)
        {
            var readTask = AdbHelper.ReadTextFileAsync(Data.DevicesObject.Current, Data.SelectedFiles.First().FullPath);
            readTask.ContinueWith(t =>
            {
                if (t.Result is not null)
                {
                    App.SafeInvoke(() =>
                    {
                        control.EditorText = t.Result;
                    });
                }
            });
        }
    }

    public DetailsPane()
    {
        InitializeComponent();

        ContentBox.Width = Data.Settings.DetailsPaneWidth;

        EditorTextBox.IsKeyboardFocusWithinChanged += (s, e) =>
        {
            IsEditorFocused = EditorTextBox.IsKeyboardFocusWithin || EditorTextBox.IsContextMenuOpen;
        };
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = ContentBox.ActualWidth - e.HorizontalChange;
        if (newWidth > MinWidth && newWidth < MaxWidth)
        {
            ContentBox.Width = newWidth;
            Data.Settings.DetailsPaneWidth = (int)ContentBox.Width;
        }
    }
}
