using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Linq.Expressions;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SearchOptionsControl.xaml
/// </summary>
[ObservableObject]
public partial class SearchOptionsControl : UserControl
{
    /// <summary>The <see cref="Pages.ExplorerPageHeader"/> that hosts this control, set once via <see cref="Initialize"/>.</summary>
    private Pages.ExplorerPageHeader? Owner { get; set; }

    internal void Initialize(Pages.ExplorerPageHeader owner)
    {
        Owner = owner;

        owner.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerInstance.IsSearchExpanded))
                NotifySearchMenuVisibilityChanged();
        };
    }

    public SearchOptionsControl()
    {
        Items = [
            new SearchBoxModeItem(Strings.Resources.S_SEARCH_ALL_SUBFOLDERS, SearchBox.SearchBoxMode.AllSubfolders),
            new SearchBoxModeItem(Strings.Resources.S_SEARCH_CURRENT_FOLDER, SearchBox.SearchBoxMode.CurrentFolder),
            new Separator(),
            new SearchToggleItem(
                Strings.Resources.S_SEARCH_CASE_SENSITIVE,
                new TextChangeCaseIcon(),
                () => Data.Settings.SearchCaseSensitive,
                Strings.Resources.S_SEARCH_CASE_SENSITIVE_INFO),
            new SearchToggleItem(
                Strings.Resources.S_SEARCH_CONTENTS,
                new DocumentSearchIcon(),
                () => Data.Settings.SearchContents,
                Strings.Resources.S_SEARCH_CONTENTS_INFO,
                () => !Data.FileActions.IsAppDrive),
            new SearchToggleItem(
                Strings.Resources.S_SEARCH_ARCHIVES,
                new ZipIcon(),
                () => Data.Settings.SearchArchives,
                Strings.Resources.S_SEARCH_ARCHIVES_INFO,
                () => !Data.FileActions.IsAppDrive),
            new Separator(),
            new SearchToggleItem(
                Strings.Resources.S_SEARCH_DISPLAY_PATH_RELATIVE,
                new ItemPathIcon(),
                () => Data.Settings.SearchDisplayPathRelative,
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
        Owner?.Instance.IsSearchExpanded == true || !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    private static bool CanCloseSearch() => !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter);

    private void CloseSearch()
    {
        Data.FileActions.ExplorerFilter = "";
        if (Owner is not null)
            Owner.Instance.IsSearchExpanded = false;
    }

    private void NotifySearchMenuVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsCloseSearchVisible));
        OnPropertyChanged(nameof(IsSearchOptionsVisible));
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
        public virtual bool IsRadioButton { get; set; } = true;
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

    /// <summary>A checkable (non-exclusive) search option backed by a single boolean <see cref="AppSettings"/> property.</summary>
    public partial class SearchToggleItem : SearchOptionsBaseItem
    {
        [ObservableProperty]
        public override partial bool IsChecked { get; set; } = false;

        private readonly PropertyInfo? valueProp;

        public bool Value
        {
            get => (bool)(valueProp!.GetValue(Data.Settings) ?? false);
            set
            {
                valueProp!.SetValue(Data.Settings, value);
                IsChecked = Action.IsEnabled && value;
            }
        }

        public SearchToggleItem(string name, UIElement icon, Expression<Func<bool>> propertyExpr, string? info = null, Func<bool>? isAllowed = null)
        {
            valueProp = AbstractSetting.ExtractPropertyInfo(propertyExpr);

            Name = name;
            Icon = icon;
            Info = info;
            IsRadioButton = false;

            if (isAllowed != null)
            {
                Data.FileActions.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive))
                    {
                        IsChecked = Action.IsEnabled && Value;
                    }
                };
            }
            else
                isAllowed = () => true;

            Action = new(isAllowed, () => Value ^= true);

            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == valueProp?.Name)
                    OnPropertyChanged(nameof(Value));
            };

            IsChecked = Action.IsEnabled && Value;
        }
    }
}
