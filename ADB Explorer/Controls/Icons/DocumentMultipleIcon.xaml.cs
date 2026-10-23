namespace ADB_Explorer.Controls;

/// <summary>
/// ic_fluent_document_multiple_20_regular — main document (const blue) and outline (const green),
/// or both in the foreground color when <see cref="IsMonochrome"/>.
/// </summary>
public partial class DocumentMultipleIcon : ScaledPathIcon
{
    public DocumentMultipleIcon()
    {
        InitializeComponent();
    }

    public bool IsMonochrome
    {
        get => (bool)GetValue(IsMonochromeProperty);
        set => SetValue(IsMonochromeProperty, value);
    }

    public static readonly DependencyProperty IsMonochromeProperty = DependencyProperty.Register(
        nameof(IsMonochrome),
        typeof(bool),
        typeof(DocumentMultipleIcon),
        new PropertyMetadata(false));
}
