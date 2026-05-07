using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

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

    public FileClass? File
    {
        get => (FileClass?)GetValue(FileProperty);
        private set => SetValue(FileProperty, value);
    }

    public static readonly DependencyProperty FileProperty =
        DependencyProperty.Register("File", typeof(FileClass),
          typeof(DetailsPane), new PropertyMetadata(null));

    public ObservableCollection<FileDetailsViewModel> ThumbnailInfoItems { get; } = [];

    private static readonly FileClass MultipleFiles = new("MultipleFiles", "/MultipleFiles", AbstractFile.FileType.MultipleFiles);
    private static readonly FileClass Drive = new("Drive", "/Drive", AbstractFile.FileType.Drive);

    private static void OnSelectedFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (DetailsPane)d;
        var files = (IEnumerable<FileClass>)e.NewValue;

        control.EditorText = null;
        if (files.FirstOrDefault() is FileClass fc && string.IsNullOrEmpty(fc.FullName))
            return;

        if (Data.Settings.SidePane is Services.AppSettings.SidePaneMode.Preview)
        {
            control.NoPreviewTextBlock.Text = files.Any()
                ? Strings.Resources.S_PREVIEW_INVALID
                : Strings.Resources.S_PREVIEW_EMPTY_SELECTION;

            if (files.Count() == 1
                && !Data.FileActions.IsRecycleBin
                && files.First() is FileClass file
                && file.Type is AbstractFile.FileType.File
                && !file.IsApk
                && !file.IsLink
                && file.Size / 1000 < Data.Settings.MaxPreviewFileSize)
            {
                control.NoPreviewTextBlock.Visibility = Visibility.Collapsed;

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
            else
            {
                control.NoPreviewTextBlock.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (files.Count() == 1)
            {
                var f = files.First();
                control.File = f;
                control.FileNameTextBlock.Text = f.DisplayName;
                control.LargeFileIcon.Source = f.DragImage;
                control.LargeFileIcon.MaxHeight = f.CacheThumbnail?.Image is null ? 128 : 192;
                control.SmallFileIcon.Source = f.CacheThumbnail?.Image is null ? null : f.FileIcon32;
                control.InvalidSelectionBorder.Visibility = Visibility.Collapsed;
                control.PopulateThumbnailInfoItems(f);
            }
            else if (files.Count() > 1)
            {
                control.File = null;
                control.FileNameTextBlock.Text = $"{files.Count()} {Strings.Resources.S_ITEMS_SELECTED_PLURAL}";
                control.LargeFileIcon.Source = MultipleFiles.DragImage;
                control.LargeFileIcon.MaxHeight = 128;
                control.SmallFileIcon.Source = null;
                control.InvalidSelectionBorder.Visibility = Visibility.Visible;
            }
            else
            {
                control.File = null;
                if (Data.CurrentPath is null)
                {
                    control.FileNameTextBlock.Text = "";
                    control.LargeFileIcon.Source = null;
                }
                else if (Data.CurrentDrive?.Path == Data.CurrentPath)
                {
                    control.FileNameTextBlock.Text = $"{Data.CurrentDrive.DisplayName}{(Data.CurrentDrive.Path == "/" ? " " : "\n")}({Data.CurrentDrive.Path})";
                    control.LargeFileIcon.Source = Drive.DragImage;
                }
                else
                {
                    control.FileNameTextBlock.Text = FileHelper.GetFullName(Data.CurrentPath);
                    control.LargeFileIcon.Source = new FileClass("", Data.CurrentPath, AbstractFile.FileType.Folder).DragImage;
                }

                control.LargeFileIcon.MaxHeight = 128;
                control.SmallFileIcon.Source = null;
                control.InvalidSelectionBorder.Visibility = Visibility.Visible;
            }
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

        Data.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.SidePane))
                OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
        };

        OnSelectedFilesChanged(this, new DependencyPropertyChangedEventArgs(SelectedFilesProperty, null, SelectedFiles));
    }

    private void PopulateThumbnailInfoItems(FileClass file)
    {
        ThumbnailInfoItems.Clear();

        if (file.Type is not AbstractFile.FileType.Unknown)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_COLUMN_TYPE, f => f.FolderViewModel.TypeName));

        ThumbnailInfoItems.Add(new(file, Strings.Resources.S_ITEM_LOCATION, f => f.ParentPath, valueIsLtr: true));

        if (file.Type is AbstractFile.FileType.File && file.Size.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_COLUMN_SIZE, f => f.FolderViewModel.SizeString));

        if (file.ModifiedTime.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_COLUMN_DATE_MODIFIED, f => f.FolderViewModel.ModifiedTimeString));

        if (file.IsLink)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_FILE_TYPE_LINK, f => f.LinkTarget, valueIsLtr: true));

        if (file.CacheThumbnail is not { } thumb)
            return;

        var info = thumb.Info;

        if (info.Resolution.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_PICTURE_DIMENSIONS, f => f.CacheThumbnail!.Value.Info.ResolutionString, valueIsLtr: true));

        if (info.Duration.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_VIDEO_DURATION, f => f.CacheThumbnail!.Value.Info.DurationString));

        if (info.FNumber.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_IMAGE_F_STOP, f => f.CacheThumbnail!.Value.Info.FNumberString, valueIsLtr: true));

        if (info.ExposureTime.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_IMAGE_EXPOSURE, f => f.CacheThumbnail!.Value.Info.ExposureTimeString));

        if (info.ISO.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_IMAGE_ISO, f => f.CacheThumbnail!.Value.Info.ISOString, valueIsLtr: true));

        if (info.Bitrate.HasValue)
            ThumbnailInfoItems.Add(new(file, Strings.Resources.S_VIDEO_BITRATE, f => f.CacheThumbnail!.Value.Info.BitrateString, valueIsLtr: true));
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
