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

    public bool IsVisible => IsCloseSearchVisible || IsSearchOptionsVisible;

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

    static Dictionary<SearchBox.SearchBoxMode, FluentPathIcon> SearchBoxModeIcons => new()
    {
        { SearchBox.SearchBoxMode.CurrentFolder, new FluentPathIcon() { Data = FluentPathGeometries.FolderSearch, Width = 16, Height = 16 } },
        { SearchBox.SearchBoxMode.AllSubfolders, new FluentPathIcon() { Data = FluentPathGeometries.FolderMultiple, Width = 16, Height = 16 } },
    };

    public abstract partial class SearchOptionsBaseItem : ObservableObject
    {
        public virtual BaseAction Action { get; set; }
        public virtual UIElement Icon { get; set; }
        public virtual string? Info { get; set; } = null;
        public virtual bool IsChecked { get; set; }
        public virtual string Name { get; set; }
    }

    public partial class SearchBoxModeItem : SearchOptionsBaseItem
    {
        [ObservableProperty]
        public override partial bool IsChecked { get; set; } = false;

        public SearchBoxModeItem(string name, SearchBox.SearchBoxMode mode, string? info = null)
        {
            Info = info;
            Name = name;
            Icon = SearchBoxModeIcons[mode];
            Action = new(IsSearchAllowed, () => Data.Settings.SearchBox = mode);

            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SearchBox))
                {
                    IsChecked = IsSearchAllowed()
                        ? Data.Settings.SearchBox == mode
                        : mode == SearchBox.SearchBoxMode.CurrentFolder;
                }
            };

            Data.FileActions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive) or nameof(FileActionsEnable.IsRecycleBin))
                {
                    IsChecked = IsSearchAllowed()
                        ? Data.Settings.SearchBox == mode
                        : mode == SearchBox.SearchBoxMode.CurrentFolder;
                }
            };

            IsChecked = IsSearchAllowed()
                ? Data.Settings.SearchBox == mode
                : mode == SearchBox.SearchBoxMode.CurrentFolder;
        }

        private static bool IsSearchAllowed() => !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive;
    }
}
