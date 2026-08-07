using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Controls;

public partial class ConflictPerFilePanel : UserControl
{
    public static readonly DependencyProperty SourcePathProperty =
        DependencyProperty.Register(nameof(SourcePath), typeof(string), typeof(ConflictPerFilePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DestinationPathProperty =
        DependencyProperty.Register(nameof(DestinationPath), typeof(string), typeof(ConflictPerFilePanel), new PropertyMetadata(string.Empty));

    public ObservableCollection<ConflictItemDecision> Items { get; } = [];

    public string SourcePath
    {
        get => (string)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public string DestinationPath
    {
        get => (string)GetValue(DestinationPathProperty);
        set => SetValue(DestinationPathProperty, value);
    }

    public ConflictPerFilePanel()
    {
        InitializeComponent();
    }

    public void SetConflicts(
        IEnumerable<FileMergeHelper.ConflictComparisonInfo> comparisons,
        string sourcePath,
        string destinationPath)
    {
        SourcePath = sourcePath ?? string.Empty;
        DestinationPath = destinationPath ?? string.Empty;

        Items.Clear();
        var list = comparisons.ToList();
        for (var i = 0; i < list.Count; i++)
            Items.Add(new ConflictItemDecision(list[i], i, isLast: i == list.Count - 1));
    }

    public IReadOnlyList<string> GetNamesToReplace()
        => [.. Items.Where(i => i.Replace).Select(i => i.Name)];

    public IReadOnlyList<string> GetNamesToSkip()
        => [.. Items.Where(i => i.Skip).Select(i => i.Name)];

    private void SourceCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConflictItemDecision item })
            item.Replace = true;
    }

    private void DestCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConflictItemDecision item })
            item.Skip = true;
    }
}

public partial class ConflictItemDecision : ViewModelBase
{
    private const string EmptyFolderMarker = "—";

    public string Name { get; }
    public string SourceSizeText { get; }
    public string SourceModifiedText { get; }
    public string DestSizeText { get; }
    public string DestModifiedText { get; }

    public string SourceSizeComparison { get; }
    public string DestSizeComparison { get; }
    public string SourceDateComparison { get; }
    public string DestDateComparison { get; }

    public string Index { get; }

    public bool IsLast { get; }

    private bool replace = true;
    public bool Replace
    {
        get => replace;
        set
        {
            if (Set(ref replace, value) && value)
                Skip = false;
        }
    }

    private bool skip;
    public bool Skip
    {
        get => skip;
        set
        {
            if (Set(ref skip, value) && value)
                Replace = false;
        }
    }

    public ConflictItemDecision(FileMergeHelper.ConflictComparisonInfo info, int index, bool isLast = false)
    {
        Name = info.Name;
        IsLast = isLast;
        SourceSizeText = FormatSize(info.SourceSize, info.IsDirectory);
        SourceModifiedText = FormatModified(info.SourceMtimeUtc, info.IsDirectory);
        DestSizeText = FormatSize(info.DestSize, info.IsDirectory);
        DestModifiedText = FormatModified(info.DestMtimeUtc, info.IsDirectory);
        Index = $"{index + 1}.";

        if (info.IsIdentical)
        {
            // Prefer keeping the destination when size and date already match.
            replace = false;
            skip = true;
            return;
        }

        if (info.SourceSize < info.DestSize)
        {
            DestSizeComparison = Strings.Resources.S_BIGGER;
        }
        else if (info.SourceSize > info.DestSize)
        {
            SourceSizeComparison = Strings.Resources.S_BIGGER;
        }

        if (info.SourceMtimeUtc < info.DestMtimeUtc)
        {
            DestDateComparison = Strings.Resources.S_NEWER;
            // Prefer the newer (destination).
            replace = false;
            skip = true;
        }
        else if (info.SourceMtimeUtc > info.DestMtimeUtc)
        {
            SourceDateComparison = Strings.Resources.S_NEWER;
            // Prefer the newer (source).
            replace = true;
            skip = false;
        }
    }

    private static string FormatSize(long? size, bool isDirectory)
    {
        if (isDirectory)
            return EmptyFolderMarker;

        if (size is null)
            return string.Empty;

        return size.Value.BytesToSize(true);
    }

    private static string FormatModified(DateTime? mtimeUtc, bool isDirectory)
    {
        if (isDirectory)
            return EmptyFolderMarker;

        if (mtimeUtc is null)
            return string.Empty;

        // Local time only — do not append a UTC offset suffix.
        var local = DateTime.SpecifyKind(mtimeUtc.Value, DateTimeKind.Utc).ToLocalTime();
        return TabularDateFormatter.Format(local, Data.Settings.ActualFormatCulture);
    }
}
