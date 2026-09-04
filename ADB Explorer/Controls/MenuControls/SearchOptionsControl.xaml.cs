using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SearchOptionsControl.xaml
/// </summary>
[ObservableObject]
public partial class SearchOptionsControl : UserControl
{
    public SearchOptionsControl()
    {
        Items = [
            new SearchBoxModeItem(Strings.Resources.S_SEARCH_ALL_SUBFOLDERS, SearchBox.SearchBoxMode.AllSubfolders),
            new SearchBoxModeItem(Strings.Resources.S_SEARCH_CURRENT_FOLDER, SearchBox.SearchBoxMode.CurrentFolder),
        ];

        CloseSearchAction = new(CanCloseSearch, CloseSearch);

        Data.FileActions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileActionsEnable.ExplorerFilter))
                NotifySearchMenuVisibilityChanged();
        };

        Data.RuntimeSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppRuntimeSettings.IsSearchBoxFocused))
                NotifySearchMenuVisibilityChanged();
        };

        InitializeComponent();
    }

    public ICollection<object> Items { get; }

    public BaseAction CloseSearchAction { get; }

    public bool IsCloseSearchVisible => !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    public bool IsSearchOptionsVisible =>
        Data.RuntimeSettings.IsSearchBoxFocused || !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    public new bool IsVisible => IsCloseSearchVisible || IsSearchOptionsVisible;

    private static bool CanCloseSearch() => !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    private static void CloseSearch()
    {
        Data.FileActions.ExplorerFilter = "";
        Data.RuntimeSettings.IsSearchBoxFocused = false;
    }

    private void NotifySearchMenuVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsCloseSearchVisible));
        OnPropertyChanged(nameof(IsSearchOptionsVisible));
        OnPropertyChanged(nameof(IsVisible));
        CommandManager.InvalidateRequerySuggested();
    }

    static Dictionary<SearchBox.SearchBoxMode, UIElement> SearchBoxModeIcons => new()
    {
        { SearchBox.SearchBoxMode.CurrentFolder, new FolderSearchIcon() },
        { SearchBox.SearchBoxMode.AllSubfolders, new FolderMultipleIcon() },
    };

    public abstract partial class SearchOptionsBaseItem : ObservableObject
    {
        public virtual BaseAction Action { get; set; } = null!;
        public virtual UIElement Icon { get; set; } = null!;
        public virtual string? Info { get; set; } = null;
        public virtual bool IsChecked { get; set; }
        public virtual string Name { get; set; } = "";
    }

    public partial class SearchBoxModeItem : SearchOptionsBaseItem
    {
        [ObservableProperty]
        public override partial bool IsChecked { get; set; } = false;

        public SearchBoxModeItem(string name, SearchBox.SearchBoxMode mode, string? info = null)
        {
            Info = info;
            Name = name;
            Mode = mode;
            Icon = SearchBoxModeIcons[mode];
            Action = new(IsModeAllowed, () => Data.Settings.SearchBox = mode);

            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SearchBox))
                {
                    IsChecked = IsModeAllowed()
                        ? Data.Settings.SearchBox == mode
                        : mode == SearchBox.SearchBoxMode.CurrentFolder;
                }
            };

            Data.FileActions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive) or nameof(FileActionsEnable.IsRecycleBin))
                {
                    IsChecked = IsModeAllowed()
                        ? Data.Settings.SearchBox == mode
                        : mode == SearchBox.SearchBoxMode.CurrentFolder;
                    CommandManager.InvalidateRequerySuggested();
                }
            };

            IsChecked = IsModeAllowed()
                ? Data.Settings.SearchBox == mode
                : mode == SearchBox.SearchBoxMode.CurrentFolder;
        }

        private bool IsModeAllowed()
        {
            if (Data.FileActions.IsRecycleBin)
                return false;

            // App list is flat — recursive subfolder search does not apply.
            if (Data.FileActions.IsAppDrive)
                return Mode == SearchBox.SearchBoxMode.CurrentFolder;

            return true;
        }

        private SearchBox.SearchBoxMode Mode { get; }
    }
}
