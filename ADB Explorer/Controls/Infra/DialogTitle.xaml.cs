using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

public partial class DialogTitle : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(DialogTitle), new PropertyMetadata(""));

    public static readonly DependencyProperty ErrorCodeProperty =
        DependencyProperty.Register(nameof(ErrorCode), typeof(int), typeof(DialogTitle), new PropertyMetadata(0));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public int ErrorCode
    {
        get => (int)GetValue(ErrorCodeProperty);
        set => SetValue(ErrorCodeProperty, value);
    }

    public DialogTitle()
    {
        InitializeComponent();
    }

    public DialogTitle(string title, DialogError error)
        : this()
    {
        Title = title;
        ErrorCode = (int)error;
    }
}
