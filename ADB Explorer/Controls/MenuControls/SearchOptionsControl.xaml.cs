namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SearchOptionsControl.xaml
/// </summary>
[ObservableObject]
public partial class SearchOptionsControl : UserControl
{
    /// <summary>The pane whose search state this control shows - the focused one in a split view.</summary>
    private ExplorerInstance? _instance;

    internal void Initialize(ExplorerPageContent owner) => SetInstance(owner.Instance);

    internal void SetInstance(ExplorerInstance instance)
    {
        if (_instance is not null)
            _instance.PropertyChanged -= Instance_PropertyChanged;

        _instance = instance;
        _instance.PropertyChanged += Instance_PropertyChanged;

        NotifySearchMenuVisibilityChanged();
    }

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerInstance.IsSearchExpanded))
            NotifySearchMenuVisibilityChanged();
    }

    public SearchOptionsControl()
    {
        Items = [
            new SearchBoxModeItem(Strings.Resources.S_SEARCH_ALL_SUBFOLDERS, SearchBox.SearchBoxMode.AllSubfolders),
            new SearchBoxModeItem(Strings.Resources.S_SEARCH_CURRENT_FOLDER, SearchBox.SearchBoxMode.CurrentFolder),
            new Separator(),
            new SettingToggleItem(
                Strings.Resources.S_SEARCH_CASE_SENSITIVE,
                () => Data.Settings.SearchCaseSensitive,
                LtrIcon(new TextChangeCaseIcon()),
                Strings.Resources.S_SEARCH_CASE_SENSITIVE_INFO),
            new SettingToggleItem(
                Strings.Resources.S_SEARCH_CONTENTS,
                () => Data.Settings.SearchContents,
                LtrIcon(new DocumentSearchIcon()),
                Strings.Resources.S_SEARCH_CONTENTS_INFO,
                () => !Data.FileActions.IsAppDrive),
            new SettingToggleItem(
                Strings.Resources.S_SEARCH_ARCHIVES,
                () => Data.Settings.SearchArchives,
                LtrIcon(new ZipIcon()),
                Strings.Resources.S_SEARCH_ARCHIVES_INFO,
                () => !Data.FileActions.IsAppDrive),
            new Separator(),
            new SettingToggleItem(
                Strings.Resources.S_SEARCH_DISPLAY_PATH_RELATIVE,
                () => Data.Settings.SearchDisplayPathRelative,
                LtrIcon(new ItemPathIcon()),
                Strings.Resources.S_SEARCH_DISPLAY_PATH_RELATIVE_INFO,
                () => !Data.FileActions.IsAppDrive)
        ];

        CloseSearchAction = new(CanCloseSearch, CloseSearch);

        Data.FileActions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileActionsEnable.ExplorerFilter))
                NotifySearchMenuVisibilityChanged();
        };

        InitializeComponent();
    }

    public ICollection<object> Items { get; }

    public BaseAction CloseSearchAction { get; }

    public bool IsCloseSearchVisible => !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    public bool IsSearchOptionsVisible =>
        _instance?.IsSearchExpanded == true || !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    private static bool CanCloseSearch() => !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    private void CloseSearch()
    {
        Data.FileActions.ExplorerFilter = "";
        if (_instance is not null)
            _instance.IsSearchExpanded = false;
    }

    private void NotifySearchMenuVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsCloseSearchVisible));
        OnPropertyChanged(nameof(IsSearchOptionsVisible));
    }

    private static UIElement LtrIcon(UserControl icon) => (UIElement)new BaseIcon(icon, 16, RtlBehavior.ForceLtr).IconContent;

    static Dictionary<SearchBox.SearchBoxMode, UIElement> SearchBoxModeIcons => new()
    {
        { SearchBox.SearchBoxMode.CurrentFolder, LtrIcon(new FolderSearchIcon()) },
        { SearchBox.SearchBoxMode.AllSubfolders, LtrIcon(new FolderMultipleIcon()) },
    };

    public class SearchBoxModeItem : SelectorItem
    {
        public SearchBoxModeItem(string name, SearchBox.SearchBoxMode mode)
        {
            Name = name;
            Mode = mode;
            Icon = SearchBoxModeIcons[mode];
            Action = new(IsModeAllowed, () => Data.Settings.SearchBox = mode);

            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SearchBox))
                {
                    UpdateIsChecked();
                }
            };

            Data.FileActions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive))
                {
                    UpdateIsChecked();
                }
            };

            UpdateIsChecked();
        }

        private bool IsModeAllowed()
        {
            // App list is flat — recursive subfolder search does not apply.
            if (Data.FileActions.IsAppDrive)
                return Mode == SearchBox.SearchBoxMode.CurrentFolder;

            return true;
        }

        private void UpdateIsChecked()
        {
            IsChecked = Mode switch
            {
                SearchBox.SearchBoxMode.CurrentFolder when Data.FileActions.IsAppDrive => true,
                SearchBox.SearchBoxMode.AllSubfolders when Data.FileActions.IsAppDrive => false,
                _ => Data.Settings.SearchBox == Mode,
            };
        }

        private SearchBox.SearchBoxMode Mode { get; }
    }
}
