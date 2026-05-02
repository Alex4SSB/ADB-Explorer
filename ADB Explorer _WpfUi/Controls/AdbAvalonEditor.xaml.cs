using ADB_Explorer.Helpers;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for AdbAvalonEditor.xaml
/// </summary>
public partial class AdbAvalonEditor : UserControl
{
    private bool _updatingText;
    private HashSet<FrameworkElement> _visualChildren;

    private string OriginalText;

    public bool HasUnsavedChanges
    {
        get => (bool)GetValue(HasUnsavedChangesProperty);
        private set => SetValue(HasUnsavedChangesProperty, value);
    }

    public static readonly DependencyProperty HasUnsavedChangesProperty =
        DependencyProperty.Register("HasUnsavedChanges", typeof(bool),
          typeof(AdbAvalonEditor), new PropertyMetadata(false));

    public string EditorText
    {
        get => (string)GetValue(EditorTextProperty);
        set => SetValue(EditorTextProperty, value);
    }

    public static readonly DependencyProperty EditorTextProperty =
        DependencyProperty.Register("EditorText", typeof(string),
          typeof(AdbAvalonEditor), new PropertyMetadata(null, OnEditorTextChanged));

    public bool IsContextMenuOpen
    {
        get => (bool)GetValue(IsContextMenuOpenProperty);
        set => SetValue(IsContextMenuOpenProperty, value);
    }

    public static readonly DependencyProperty IsContextMenuOpenProperty =
        DependencyProperty.Register("IsContextMenuOpen", typeof(bool),
          typeof(AdbAvalonEditor), new PropertyMetadata(false));

    private static void OnEditorTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AdbAvalonEditor)d;
        if (control._updatingText)
            return;

        control._updatingText = true;
        string val = (string)e.NewValue ?? string.Empty;

        control.EditorTextBox.Document.Text = val;
        control._updatingText = false;

        control.OriginalText = val;
    }

    public AdbAvalonEditor()
    {
        InitializeComponent();

        EditorTextBox.TextChanged += EditorTextBox_TextChanged;
        EditorTextBox.TextArea.ContextMenu = (ContextMenu)FindResource("TextBoxContextMenu");
        EditorTextBox.TextArea.ContextMenuOpening += EditorTextBox_ContextMenuOpening;
        EditorTextBox.TextArea.ContextMenuClosing += (_, _) => IsContextMenuOpen = false;
        EditorTextBox.TextArea.ClipToBounds = true;

        Loaded += AvalonEditor_Loaded;
        Unloaded += AvalonEditor_Unloaded;

        ApplyEditorTheme(ApplicationThemeManager.GetAppTheme());
        ApplicationThemeManager.Changed += (_, _) => ApplyEditorTheme(ApplicationThemeManager.GetAppTheme());
    }

    private void ApplyEditorTheme(ApplicationTheme theme)
    {
        bool isDark = theme == ApplicationTheme.Dark;
        EditorTextBox.TextArea.SelectionBrush = new SolidColorBrush(
            isDark ? Color.FromArgb(0x7F, 0x77, 0x77, 0x77) : Color.FromArgb(0xFF, 0xCC, 0xE8, 0xFF));
        EditorTextBox.TextArea.SelectionForeground = null;
    }

    private void EditorTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (EditorTextBox.TextArea.ContextMenu is not ContextMenu menu)
            return;

        IsContextMenuOpen = true;

        bool hasSelection = !EditorTextBox.TextArea.Selection.IsEmpty;
        bool isReadOnly = EditorTextBox.IsReadOnly;
        bool hasText = EditorTextBox.Document.TextLength > 0;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Command switch
            {
                RoutedUICommand c when c == ApplicationCommands.Cut => hasSelection && !isReadOnly,
                RoutedUICommand c when c == ApplicationCommands.Copy => hasSelection,
                RoutedUICommand c when c == ApplicationCommands.Paste => !isReadOnly && Clipboard.ContainsText(),
                RoutedUICommand c when c == ApplicationCommands.Undo => !isReadOnly && EditorTextBox.Document.UndoStack.CanUndo,
                RoutedUICommand c when c == ApplicationCommands.Redo => !isReadOnly && EditorTextBox.Document.UndoStack.CanRedo,
                RoutedUICommand c when c == ApplicationCommands.SelectAll => hasText,
                _ => item.IsEnabled
            };
        }
    }

    private void EditorTextBox_TextChanged(object sender, EventArgs e)
    {
        if (_updatingText)
            return;

        _updatingText = true;
        EditorText = EditorTextBox.Document.Text;
        _updatingText = false;

        HasUnsavedChanges = EditorText != OriginalText;
    }

    private void AvalonEditor_Loaded(object sender, RoutedEventArgs e)
    {
        _visualChildren = StyleHelper.EnumerateVisualChildren(this);

        if (Window.GetWindow(this) is Window window)
            window.PreviewMouseDown += Window_PreviewMouseDown;
    }

    private void AvalonEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
            window.PreviewMouseDown -= Window_PreviewMouseDown;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (EditorTextBox.IsKeyboardFocusWithin &&
            e.OriginalSource is FrameworkElement source &&
            !_visualChildren.Contains(source))
        {
            Keyboard.ClearFocus();
        }
    }
}
