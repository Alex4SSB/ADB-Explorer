using Wpf.Ui.Controls;

namespace ADB_Explorer.Controls;

/// <summary>
/// Path-based icon for use with Wpf.Ui controls that require <see cref="IconElement"/> (e.g. NavigationViewItem).
/// </summary>
public class FluentPathIcon : IconElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(Geometry),
        typeof(FluentPathIcon),
        new PropertyMetadata(Geometry.Empty, OnDataChanged));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(FluentPathIcon),
        new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

    public Geometry Data
    {
        get => (Geometry)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    protected System.Windows.Shapes.Path? PathElement { get; private set; }

    protected override UIElement InitializeChildren()
    {
        PathElement = new System.Windows.Shapes.Path
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch,
            Fill = Foreground,
            Data = Data,
        };

        return PathElement;
    }

    protected override void OnForegroundChanged(DependencyPropertyChangedEventArgs args)
    {
        PathElement?.SetCurrentValue(System.Windows.Shapes.Path.FillProperty, args.NewValue as Brush);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (FluentPathIcon)d;
        if (self.PathElement is null)
            return;

        self.PathElement.Data = (Geometry)e.NewValue;
    }

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (FluentPathIcon)d;
        if (self.PathElement is null)
            return;

        self.PathElement.Stretch = (Stretch)e.NewValue;
    }
}

public class BaseIcon
{
    private const string DefaultForegroundBrush = "TextFillColorPrimaryBrush";

    public object IconContent { get; private set; }

    public double Size { get; }

    public BaseIcon(string glyph, double fontSize = 22, string? brush = null)
    {
        Size = fontSize;
        FontIcon fontIcon = new()
        {
            Glyph = glyph,
            FontSize = fontSize,
            Style = CreateForegroundStyle(typeof(FontIcon), "BaseIconFontStyle", brush ?? DefaultForegroundBrush),
        };

        IconContent = fontIcon;
    }

    public BaseIcon(Geometry data, double height = 22, string? brush = null, Stretch stretch = Stretch.Uniform)
    {
        Size = height;
        FluentPathIcon icon = new()
        {
            Data = data,
            Stretch = stretch,
            Width = height,
            Height = height,
            Style = CreateForegroundStyle(typeof(FluentPathIcon), "BaseIconPathStyle", brush ?? DefaultForegroundBrush),
        };

        IconContent = icon;
    }

    public BaseIcon(ImageSource imageSource, double height = 22, Stretch stretch = Stretch.Uniform)
    {
        Size = height;
        IconContent = new System.Windows.Controls.Image()
        {
            Source = imageSource,
            Height = height,
            Stretch = stretch,
        };
    }

    public BaseIcon(UserControl content, double size = 18)
    {
        Size = size;
        IconContent = content;
    }

    public static BaseIcon NewItem() => new(new NewItemIcon());

    private static Style CreateForegroundStyle(Type targetType, string baseStyleKey, string enabledBrushKey)
    {
        var baseStyle = (Style)App.Current.Resources[baseStyleKey];
        if (enabledBrushKey == DefaultForegroundBrush)
            return baseStyle;

        var style = new Style(targetType, baseStyle);
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(enabledBrushKey)));
        return style;
    }
}

public static class FluentPathGeometries
{
    // ic_fluent_text_bullet_list_square_20_regular
    public static readonly Geometry TextBulletListSquare = Geometry.Parse(
        "M6.75 8C7.16421 8 7.5 7.66421 7.5 7.25C7.5 6.83579 7.16421 6.5 6.75 6.5C6.33579 6.5 6 6.83579 6 7.25C6 7.66421 6.33579 8 6.75 8ZM7.5 10.25C7.5 10.6642 7.16421 11 6.75 11C6.33579 11 6 10.6642 6 10.25C6 9.83579 6.33579 9.5 6.75 9.5C7.16421 9.5 7.5 9.83579 7.5 10.25ZM6.75 14C7.16421 14 7.5 13.6642 7.5 13.25C7.5 12.8358 7.16421 12.5 6.75 12.5C6.33579 12.5 6 12.8358 6 13.25C6 13.6642 6.33579 14 6.75 14ZM9 7.5C9 7.22386 9.22386 7 9.5 7H13.5C13.7761 7 14 7.22386 14 7.5C14 7.77614 13.7761 8 13.5 8H9.5C9.22386 8 9 7.77614 9 7.5ZM9.5 10C9.22386 10 9 10.2239 9 10.5C9 10.7761 9.22386 11 9.5 11H13.5C13.7761 11 14 10.7761 14 10.5C14 10.2239 13.7761 10 13.5 10H9.5ZM9 13.5C9 13.2239 9.22386 13 9.5 13H13.5C13.7761 13 14 13.2239 14 13.5C14 13.7761 13.7761 14 13.5 14H9.5C9.22386 14 9 13.7761 9 13.5ZM5.75 3H14.25C15.7688 3 17 4.23122 17 5.75V14.25C17 15.7688 15.7688 17 14.25 17H5.75C4.23122 17 3 15.7688 3 14.25V5.75C3 4.23122 4.23122 3 5.75 3ZM4 5.75V14.25C4 15.2165 4.7835 16 5.75 16H14.25C15.2165 16 16 15.2165 16 14.25V5.75C16 4.7835 15.2165 4 14.25 4H5.75C4.7835 4 4 4.7835 4 5.75Z");

    // ic_fluent_folder_briefcase_20_regular
    public static readonly Geometry FolderBriefcase = Geometry.Parse(
        "M4.5 3C3.11929 3 2 4.11929 2 5.5V14.5C2 15.8807 3.11929 17 4.5 17H9V16H4.5C3.67157 16 3 15.3284 3 14.5V8H7.08579C7.48361 8 7.86514 7.84196 8.14645 7.56066L9.70711 6H15.5C16.3284 6 17 6.67157 17 7.5V9.49982C17.4911 9.86866 17.8419 10.4141 17.9581 11.0419C17.9721 11.0445 17.9861 11.0472 18 11.05V7.5C18 6.11929 16.8807 5 15.5 5H9.70711L8.21967 3.51256C7.89148 3.18437 7.44636 3 6.98223 3H4.5ZM3 5.5C3 4.67157 3.67157 4 4.5 4H6.98223C7.18115 4 7.37191 4.07902 7.51256 4.21967L8.79289 5.5L7.43934 6.85355C7.34557 6.94732 7.21839 7 7.08579 7H3V5.5ZM12 11.5V12H11.5C10.6716 12 10 12.6716 10 13.5V17.5C10 18.3284 10.6716 19 11.5 19H17.5C18.3284 19 19 18.3284 19 17.5V13.5C19 12.6716 18.3284 12 17.5 12H17V11.5C17 10.6716 16.3284 10 15.5 10H13.5C12.6716 10 12 10.6716 12 11.5ZM13.5 11H15.5C15.7761 11 16 11.2239 16 11.5V12H13V11.5C13 11.2239 13.2239 11 13.5 11Z");

    // \uE62F (CopyItemPath), normalized to 20x20
    public static readonly Geometry AppDataPath = Geometry.Parse(
        "M15.9375,13.125C16.19791603,13.125 16.41926956,13.21614647 16.6015625,13.3984375C16.78385162,13.58073044 16.87499809,13.80208397 16.875,14.0625C16.87499809,14.32291794 16.78385162,14.54427147 16.6015625,14.7265625C16.41926956,14.90885544 16.19791603,15 15.9375,15C15.67708206,15 15.45572853,14.90885544 15.2734375,14.7265625C15.09114456,14.54427147 15,14.32291794 15,14.0625C15,13.80208397 15.09114456,13.58073044 15.2734375,13.3984375C15.45572853,13.21614647 15.67708206,13.125 15.9375,13.125ZM12.8125,13.125C13.07291603,13.125 13.29426956,13.21614647 13.4765625,13.3984375C13.65885353,13.58073044 13.75,13.80208397 13.75,14.0625C13.75,14.32291794 13.65885353,14.54427147 13.4765625,14.7265625C13.29426956,14.90885544 13.07291603,15 12.8125,15C12.55208206,15 12.33072853,14.90885544 12.1484375,14.7265625C11.96614456,14.54427147 11.875,14.32291794 11.875,14.0625C11.875,13.80208397 11.96614456,13.58073044 12.1484375,13.3984375C12.33072853,13.21614647 12.55208206,13.125 12.8125,13.125ZM6.86523438,5C7.00195313,5 7.12890625,5.04231834 7.24609375,5.12695313C7.36328125,5.21158934 7.44140625,5.31901121 7.48046875,5.44921875L9.98046875,14.19921875C9.99348927,14.26432419 10,14.31966209 10,14.36523438C10,14.53450584 9.94140625,14.68261719 9.82421875,14.80957031C9.70703125,14.93652344 9.56054688,15 9.38476563,15C9.24804688,15 9.12109375,14.95768356 9.00390625,14.87304688C8.88671875,14.78841209 8.80859375,14.68099022 8.76953125,14.55078125L6.26953125,5.80078125C6.25650978,5.77474022 6.25,5.74869871 6.25,5.72265625C6.25,5.69661522 6.25,5.66731834 6.25,5.63476563C6.25,5.46549559 6.30859375,5.31738281 6.42578125,5.19042969C6.54296875,5.06347656 6.68945313,5 6.86523438,5ZM3.11523438,5C3.25195313,5 3.37890625,5.04231834 3.49609375,5.12695313C3.61328125,5.21158934 3.69140625,5.31901121 3.73046875,5.44921875L6.23046875,14.19921875C6.24348927,14.26432419 6.25,14.31966209 6.25,14.36523438C6.25,14.53450584 6.19140625,14.68261719 6.07421875,14.80957031C5.95703125,14.93652344 5.81054688,15 5.63476563,15C5.49804688,15 5.37109375,14.95768356 5.25390625,14.87304688C5.13671875,14.78841209 5.05859375,14.68099022 5.01953125,14.55078125L2.51953125,5.80078125C2.50651026,5.77474022 2.5,5.74869871 2.5,5.72265625C2.5,5.69661522 2.5,5.66731834 2.5,5.63476563C2.5,5.46549559 2.55859375,5.31738281 2.67578125,5.19042969C2.79296875,5.06347656 2.93945313,5 3.11523438,5ZM3.70117188,3.75C3.37565088,3.75 3.06477833,3.81673217 2.76855469,3.95019531C2.47233057,4.08365965 2.21191406,4.26269627 1.98730469,4.48730469C1.76269531,4.71191502 1.5836587,4.97233152 1.45019531,5.26855469C1.31673169,5.56477928 1.25,5.87565184 1.25,6.20117188L1.25,13.79882813C1.25,14.12434959 1.31673169,14.43522263 1.45019531,14.73144531C1.5836587,15.02766991 1.76269531,15.28808594 1.98730469,15.51269531C2.21191406,15.73730469 2.47233057,15.91634178 2.76855469,16.04980469C3.06477833,16.1832695 3.37565088,16.25 3.70117188,16.25L16.29882813,16.25C16.62434769,16.25 16.93521881,16.1832695 17.23144531,16.04980469C17.527668,15.91634178 17.78808594,15.73730469 18.01269531,15.51269531C18.23730469,15.28808594 18.41633987,15.02766991 18.54980469,14.73144531C18.68326569,14.43522263 18.74999809,14.12434959 18.75,13.79882813L18.75,6.20117188C18.74999809,5.87565184 18.68326569,5.56477928 18.54980469,5.26855469C18.41633987,4.97233152 18.23730469,4.71191502 18.01269531,4.48730469C17.78808594,4.26269627 17.527668,4.08365965 17.23144531,3.95019531C16.93521881,3.81673217 16.62434769,3.75 16.29882813,3.75L3.70117188,3.75ZM3.671875,2.5L16.328125,2.5C16.81640625,2.5 17.28352737,2.59928465 17.72949219,2.79785156C18.17545319,2.99642086 18.56607819,3.26334715 18.90136719,3.59863281C19.23665237,3.93391967 19.50358009,4.32454491 19.70214844,4.77050781C19.90071487,5.21647215 20,5.68359375 20,6.171875L20,13.828125C20,14.31640625 19.90071487,14.78352928 19.70214844,15.22949219C19.50358009,15.675457 19.23665237,16.066082 18.90136719,16.40136719C18.56607819,16.73665428 18.17545319,17.003582 17.72949219,17.20214844C17.28352737,17.40071678 16.81640625,17.5 16.328125,17.5L3.671875,17.5C3.18359375,17.5 2.7164712,17.40071678 2.27050781,17.20214844C1.82454419,17.003582 1.43391919,16.73665428 1.09863281,16.40136719C0.76334631,16.066082 0.49641925,15.675457 0.29785156,15.22949219C0.09928386,14.78352928 0,14.31640625 0,13.828125L0,6.171875C0,5.68359375 0.09928386,5.21647215 0.29785156,4.77050781C0.49641925,4.32454491 0.76334631,3.93391967 1.09863281,3.59863281C1.43391919,3.26334715 1.82454419,2.99642086 2.27050781,2.79785156C2.7164712,2.59928465 3.18359375,2.5 3.671875,2.5Z");
}
