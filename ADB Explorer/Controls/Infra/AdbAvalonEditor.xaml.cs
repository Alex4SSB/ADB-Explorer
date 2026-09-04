using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for AdbAvalonEditor.xaml
/// </summary>
public partial class AdbAvalonEditor : UserControl
{
    private bool _updatingText;
    private bool _hexSnapping;
    private HashSet<FrameworkElement> _visualChildren = [];

    private string? OriginalText;

    public bool HasUnsavedChanges
    {
        get => (bool)GetValue(HasUnsavedChangesProperty);
        private set => SetValue(HasUnsavedChangesProperty, value);
    }

    public static readonly DependencyProperty HasUnsavedChangesProperty =
        DependencyProperty.Register("HasUnsavedChanges", typeof(bool),
          typeof(AdbAvalonEditor), new PropertyMetadata(false));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool),
          typeof(AdbAvalonEditor), new PropertyMetadata(false, OnIsReadOnlyChanged));

    public bool IsHexMode
    {
        get => (bool)GetValue(IsHexModeProperty);
        set => SetValue(IsHexModeProperty, value);
    }

    public static readonly DependencyProperty IsHexModeProperty =
        DependencyProperty.Register(nameof(IsHexMode), typeof(bool),
          typeof(AdbAvalonEditor), new PropertyMetadata(false, OnIsHexModeChanged));

    private static void OnIsHexModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AdbAvalonEditor control || control.EditorTextBox is null)
            return;

        control.EditorTextBox.Options.EnableRectangularSelection = !control.IsHexMode;
        control.SnapHexCaret();
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AdbAvalonEditor)d;
        control.EditorTextBox.IsReadOnly = (bool)e.NewValue;
        if ((bool)e.NewValue)
            control.HasUnsavedChanges = false;
    }

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
        control.HasUnsavedChanges = control.EditorText != control.OriginalText;
    }

    public AdbAvalonEditor()
    {
        InitializeComponent();

        EditorTextBox.Options.EnableHyperlinks = false;
        EditorTextBox.Options.EnableEmailHyperlinks = false;
        EditorTextBox.Options.AllowToggleOverstrikeMode = true;

        EditorTextBox.TextChanged += EditorTextBox_TextChanged;
        EditorTextBox.TextArea.ContextMenu = (ContextMenu)FindResource("TextBoxContextMenu");
        EditorTextBox.TextArea.ContextMenuOpening += EditorTextBox_ContextMenuOpening;
        EditorTextBox.TextArea.ContextMenuClosing += (_, _) => IsContextMenuOpen = false;
        EditorTextBox.TextArea.PreviewKeyDown += EditorTextBox_PreviewKeyDown;
        EditorTextBox.TextArea.TextEntering += EditorTextBox_TextEntering;
        EditorTextBox.TextArea.Caret.PositionChanged += (_, _) => SnapHexCaret();
        EditorTextBox.TextArea.SelectionChanged += (_, _) => SnapHexCaret();
        CommandManager.AddPreviewExecutedHandler(EditorTextBox.TextArea, EditorTextBox_PreviewExecuted);
        EditorTextBox.TextArea.ClipToBounds = true;

        Loaded += AvalonEditor_Loaded;
        Unloaded += AvalonEditor_Unloaded;

        ApplyEditorTheme(ApplicationThemeManager.GetAppTheme());
        // Changed can fire synchronously from a system theme-change message hook; defer so
        // this (and every other open editor's) handler never blocks that message pump.
        ApplicationThemeManager.Changed += (_, _) => App.SafeBeginInvoke(() =>
        {
            ApplyEditorTheme(ApplicationThemeManager.GetAppTheme());
        });
    }

    public void SetSyntaxHighlighting(IHighlightingDefinition? definition)
        => EditorTextBox.SyntaxHighlighting = definition;

    private void ApplyEditorTheme(ApplicationTheme theme)
    {
        if (theme == ApplicationTheme.HighContrast)
        {
            EditorTextBox.TextArea.SelectionBrush = new SolidColorBrush(SystemColors.HighlightColor);
            EditorTextBox.TextArea.SelectionForeground = new SolidColorBrush(SystemColors.HighlightTextColor);
        }
        else
        {
            bool isDark = theme == ApplicationTheme.Dark;
            EditorTextBox.TextArea.SelectionBrush = new SolidColorBrush(
                isDark ? Color.FromArgb(0x7F, 0x77, 0x77, 0x77) : Color.FromArgb(0xFF, 0xCC, 0xE8, 0xFF));
            EditorTextBox.TextArea.SelectionForeground = null;
        }

        // ThemeAwareHighlightingColorizer reads the theme per paint; force a redraw.
        EditorTextBox.TextArea.TextView.Redraw();
    }

    private void EditorTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (EditorTextBox.TextArea.ContextMenu is not ContextMenu menu)
            return;

        IsContextMenuOpen = true;
        menu.FlowDirection = AppFlowDirection;

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

    private void EditorTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (_updatingText || IsReadOnly)
            return;

        if (IsHexMode)
            NormalizeHexDocument();

        _updatingText = true;
        EditorText = EditorTextBox.Document.Text;
        _updatingText = false;

        HasUnsavedChanges = EditorText != OriginalText;
    }

    public void MarkAsUnsaved()
    {
        OriginalText = null;
        HasUnsavedChanges = true;
    }

    private void AvalonEditor_Loaded(object sender, RoutedEventArgs e)
    {
        _visualChildren = StyleHelper.EnumerateVisualChildren(this);
        ApplyDefaultTextFlowDirection();

        if (Window.GetWindow(this) is Window window)
            window.PreviewMouseDown += Window_PreviewMouseDown;
    }

    private void EditorTextBox_TextEntering(object sender, TextCompositionEventArgs e)
    {
        if (!IsHexMode || string.IsNullOrEmpty(e.Text))
            return;

        e.Handled = true;
        if (IsReadOnly)
            return;

        GetHexSelection(out var selStart, out var selLength);
        if (HexText.ExtractDigits(e.Text).Length == 0)
            return;

        ApplyHexState(HexText.Insert(EditorTextBox.Document.Text, selStart, selLength, e.Text, EditorTextBox.TextArea.OverstrikeMode));
    }

    private void EditorTextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!IsHexMode)
            return;

        if (e.Command == ApplicationCommands.Paste)
        {
            e.Handled = true;
            if (IsReadOnly)
                return;

            string? text;
            try
            {
                if (!Clipboard.ContainsText())
                    return;
                text = Clipboard.GetText();
            }
            catch (ExternalException)
            {
                return;
            }

            GetHexSelection(out var selStart, out var selLength);
            if (HexText.ExtractDigits(text).Length == 0)
                return;

            ApplyHexState(HexText.Insert(EditorTextBox.Document.Text, selStart, selLength, text, EditorTextBox.TextArea.OverstrikeMode));
            return;
        }

        if (e.Command != ApplicationCommands.Cut)
            return;

        e.Handled = true;
        if (IsReadOnly || EditorTextBox.SelectionLength == 0)
            return;

        try
        {
            Clipboard.SetText(EditorTextBox.SelectedText);
        }
        catch (ExternalException)
        {
            return;
        }

        GetHexSelection(out var cutStart, out var cutLength);
        ApplyHexState(HexText.Insert(EditorTextBox.Document.Text, cutStart, cutLength, ""));
    }

    private void EditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleHexKey(e))
            return;

        if (!TryGetTextFlowDirectionFromKey(e, out var flowDirection))
            return;

        EditorTextBox.TextArea.FlowDirection = flowDirection;
        e.Handled = true;
    }

    private bool TryHandleHexKey(KeyEventArgs e)
    {
        if (!IsHexMode)
            return false;

        if (e.Key is Key.Left or Key.Right)
        {
            var delta = e.Key == Key.Left ? -1 : 1;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                delta *= 2;

            MoveHexCaret(delta, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return true;
        }

        if (IsReadOnly)
            return false;

        if (e.Key == Key.Back)
        {
            GetHexSelection(out var selStart, out var selLength);
            ApplyHexState(HexText.Backspace(EditorTextBox.Document.Text, selStart, selLength));
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Delete)
        {
            GetHexSelection(out var selStart, out var selLength);
            ApplyHexState(HexText.Delete(EditorTextBox.Document.Text, selStart, selLength));
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.Space or Key.Return or Key.Enter)
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void MoveHexCaret(int nibbleDelta, bool extend)
    {
        var text = EditorTextBox.Document.Text;
        var caretOffset = EditorTextBox.CaretOffset;
        var selLength = EditorTextBox.SelectionLength;
        var selStart = EditorTextBox.SelectionStart;

        if (selLength > 0 && !extend)
        {
            var edge = nibbleDelta < 0 ? selStart : selStart + selLength;
            SetHexCaret(text, HexText.NibbleIndex(text, edge));
            return;
        }

        var currentNibble = HexText.NibbleIndex(text, caretOffset);
        var total = HexText.NibbleIndex(text, text.Length);
        var next = currentNibble + nibbleDelta;
        if (next < 0)
            next = 0;
        else if (next > total)
            next = total;

        if (!extend)
        {
            SetHexCaret(text, next);
            return;
        }

        int anchorNibble;
        if (selLength == 0)
            anchorNibble = currentNibble;
        else
        {
            var other = caretOffset <= selStart ? selStart + selLength : selStart;
            anchorNibble = HexText.NibbleIndex(text, other);
        }

        var range = HexText.SelectionForNibbles(text, anchorNibble, next);
        if (range.Anchor == range.Caret)
        {
            SetHexCaret(text, next);
            return;
        }

        EditorTextBox.TextArea.Selection = Selection.Create(EditorTextBox.TextArea, range.Anchor, range.Caret);
        EditorTextBox.TextArea.Caret.Offset = range.Caret;
    }

    private void SetHexCaret(string text, int nibble)
    {
        EditorTextBox.TextArea.ClearSelection();
        EditorTextBox.CaretOffset = HexText.CaretOffsetFromNibble(text, nibble);
    }

    private void GetHexSelection(out int selStart, out int selLength)
    {
        if (EditorTextBox.SelectionLength > 0)
        {
            selStart = EditorTextBox.SelectionStart;
            selLength = EditorTextBox.SelectionLength;
            return;
        }

        selStart = EditorTextBox.CaretOffset;
        selLength = 0;
    }

    private void ApplyHexState(HexText.CaretState state)
    {
        _updatingText = true;
        EditorTextBox.Document.Text = state.Text;
        _updatingText = false;

        EditorTextBox.CaretOffset = state.Caret;
        EditorTextBox.TextArea.ClearSelection();

        _updatingText = true;
        EditorText = state.Text;
        _updatingText = false;
        HasUnsavedChanges = EditorText != OriginalText;
    }

    private void NormalizeHexDocument()
    {
        var text = EditorTextBox.Document.Text;
        var normalized = HexText.Normalize(text);
        if (normalized == text)
            return;

        var nibble = HexText.NibbleIndex(text, EditorTextBox.CaretOffset);
        _updatingText = true;
        EditorTextBox.Document.Text = normalized;
        EditorTextBox.CaretOffset = HexText.CaretOffsetFromNibble(normalized, nibble);
        _updatingText = false;
    }

    private void SnapHexCaret()
    {
        if (EditorTextBox is null || !IsHexMode || _updatingText || _hexSnapping)
            return;

        var text = EditorTextBox.Document.Text;
        var caretOffset = EditorTextBox.CaretOffset;
        var snappedCaret = HexText.CaretOffsetFromNibble(text, HexText.NibbleIndex(text, caretOffset));

        if (EditorTextBox.SelectionLength == 0)
        {
            if (snappedCaret != caretOffset)
            {
                _hexSnapping = true;
                EditorTextBox.CaretOffset = snappedCaret;
                _hexSnapping = false;
            }
            return;
        }

        var selStart = EditorTextBox.SelectionStart;
        var selEnd = selStart + EditorTextBox.SelectionLength;
        var anchorOff = caretOffset <= selStart ? selEnd : selStart;
        var range = HexText.SelectionForNibbles(
            text,
            HexText.NibbleIndex(text, anchorOff),
            HexText.NibbleIndex(text, caretOffset));

        if (range.Anchor == range.Caret)
        {
            _hexSnapping = true;
            SetHexCaret(text, HexText.NibbleIndex(text, caretOffset));
            _hexSnapping = false;
            return;
        }

        if (range.Anchor == anchorOff && range.Caret == caretOffset)
            return;

        _hexSnapping = true;
        EditorTextBox.TextArea.Selection = Selection.Create(EditorTextBox.TextArea, range.Anchor, range.Caret);
        EditorTextBox.TextArea.Caret.Offset = range.Caret;
        _hexSnapping = false;
    }

    private static FlowDirection AppFlowDirection =>
        Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private void ApplyDefaultTextFlowDirection()
        => EditorTextBox.TextArea.FlowDirection = FlowDirection.LeftToRight;

    private static bool TryGetTextFlowDirectionFromKey(KeyEventArgs e, out FlowDirection flowDirection)
    {
        flowDirection = default;
        if (e.IsRepeat)
            return false;

        if ((e.Key == Key.LeftShift && Keyboard.IsKeyDown(Key.LeftCtrl))
            || (e.Key == Key.LeftCtrl && Keyboard.IsKeyDown(Key.LeftShift)))
        {
            flowDirection = FlowDirection.LeftToRight;
            return true;
        }

        if ((e.Key == Key.RightShift && Keyboard.IsKeyDown(Key.RightCtrl))
            || (e.Key == Key.RightCtrl && Keyboard.IsKeyDown(Key.RightShift)))
        {
            flowDirection = FlowDirection.RightToLeft;
            return true;
        }

        return false;
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
