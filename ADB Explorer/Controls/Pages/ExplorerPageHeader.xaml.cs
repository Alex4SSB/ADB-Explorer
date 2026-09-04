using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.Views;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for ExplorerPageHeader.xaml
/// </summary>
public partial class ExplorerPageHeader : UserControl
{

    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    private bool bfNavigation;

    private int ClickCount = 0;
    private bool WasSelected;
    private bool WasEditing;
    private Point MouseDownPoint;
    private TextBox? _renameTextBox;

    /// <summary>Open toolbar submenu depth (Main / Navigation / sorting / etc.).</summary>
    private int _toolbarSubmenuDepth;

    /// <summary>
    /// True after a toolbar submenu closed while the left button was still down —
    /// the dismiss click should not start rubber-band selection (it may still unselect).
    /// </summary>
    private bool _suppressSelectionAfterMenu;

    private ExplorerViewModel ViewModel { get; }

    /// <summary>
    /// Returns the currently active items view (either <see cref="IconView"/> or <see cref="ExplorerGrid"/>).
    /// </summary>
    private Selector ActiveView => ViewModel.IsIconView ? IconView : ExplorerGrid;

    /// <summary>
    /// Returns the selected items from the currently active view.
    /// </summary>
    private System.Collections.IList ActiveSelectedItems => ViewModel.IsIconView
        ? IconView.SelectedItems
        : ExplorerGrid.SelectedItems;

    private void ActiveUnselectAll()
    {
        try
        {
            if (ViewModel.IsIconView)
                IconView.UnselectAll();
            else
                ExplorerGrid.UnselectAll();
        }
        catch
        { }
    }

    private void ActiveSelectAll()
    {
        if (ViewModel.IsIconView)
            IconView.SelectAll();
        else
            ExplorerGrid.SelectAll();
    }

    private void ToggleSelectAll()
    {
        if (ActiveView.Items.Count == ActiveSelectedItems.Count && ActiveSelectedItems.Count > 0)
            ActiveUnselectAll();
        else
            ActiveSelectAll();
    }

    private void ActiveScrollIntoView(object item)
    {
        if (item is null)
            return;

        if (ViewModel.IsIconView)
            IconView.ScrollIntoView(item);
        else
        {
            ExplorerGrid.ScrollIntoView(item);
            // ScrollIntoView aligns the row box and skips the 3px left margin; restore it.
            ResetExplorerHorizontalScroll();
        }
    }

    public ScrollViewer ExplorerScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid);
            return field;
        }
    } = null;

    public ScrollViewer IconScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(IconView);
            return field;
        }
    } = null;

    public ScrollViewer ActiveScrollViewer => ViewModel.IsIconView ? IconScrollViewer : ExplorerScrollViewer;

    private void ResetExplorerHorizontalScroll()
    {
        void reset() => ActiveScrollViewer?.ScrollToHorizontalOffset(0);

        reset();
        // DataGrid.ScrollIntoView often defers BringIntoView to Loaded; run after that
        // so the row left margin is not scrolled off against the tree splitter.
        App.SafeBeginInvoke(reset, DispatcherPriority.Loaded);
        App.SafeBeginInvoke(reset, DispatcherPriority.Input);
    }

    private static Point NullPoint => new(-1, -1);

    private bool IsExplorerNameOrIconHit(DependencyObject? originalSource, Point positionInSelectionRect)
    {
        if (originalSource is null || positionInSelectionRect == NullPoint)
            return false;

        return HitTestHelper.IsExplorerNameOrIconHit(
            originalSource,
            positionInSelectionRect,
            SelectionRect,
            ViewModel.IsIconView,
            IconColumn,
            NameColumn,
            PackageName);
    }

    private bool IsAlreadySelectedExplorerItem(DependencyObject? originalSource)
    {
        if (originalSource is null)
            return false;

        if (ViewModel.IsIconView)
        {
            var item = HitTestHelper.FindAncestor<ListViewItem>(originalSource);
            return item?.IsSelected == true;
        }

        var row = HitTestHelper.FindAncestor<DataGridRow>(originalSource);
        return row?.IsSelected == true;
    }

    private void TrackExplorerMouseDown(MouseButtonEventArgs e, DependencyObject? originalSource)
    {
        SelectionRect.ResetGesture();

        MouseDownPoint = SuppressExplorerMarquee
            ? NullPoint
            : e.GetPosition(SelectionRect);

        if (MouseDownPoint == NullPoint)
        {
            CopyPaste.DragStatus = CopyPasteService.DragState.None;
            return;
        }

        if (IsExplorerNameOrIconHit(originalSource, MouseDownPoint)
            || IsAlreadySelectedExplorerItem(originalSource))
            CopyPaste.DragStatus = CopyPasteService.DragState.Pending;
        else
            CopyPaste.DragStatus = CopyPasteService.DragState.None;
    }

    private void TryBeginExplorerDragOrMarquee(Point point, bool abort, ScrollViewer scroller, DependencyObject? dragSource)
    {
        if (abort || CopyPaste.WasDragging || CopyPaste.DragStatus is CopyPasteService.DragState.Active)
        {
            SelectionRect.Collapse();
            return;
        }

        if (SelectionRect.IsActive)
        {
            SelectionRect.Update(point, MouseDownPoint, scroller, ActiveView, ActiveSelectedItems, ViewModel);
            return;
        }

        if ((MouseDownPoint - point).LengthSquared < DRAG_START_DISTANCE_SQUARED)
            return;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Pending
            && ActiveSelectedItems.Count > 0
            && ActiveSelectedItems[0] is FileClass or Package)
        {
            InitiateDrag(dragSource);
            return;
        }

        CopyPaste.DragStatus = CopyPasteService.DragState.None;
        SelectionRect.Update(point, MouseDownPoint, scroller, ActiveView, ActiveSelectedItems, ViewModel);
    }

    /// <summary>
    /// Skip rubber-band / file drag from this click (open menu, or the click that just closed one).
    /// </summary>
    private bool SuppressExplorerMarquee =>
        ViewModel.IsMenuOpen || _toolbarSubmenuDepth > 0 || _suppressSelectionAfterMenu;

    /// <summary>
    /// Skip clearing selection only while the explorer context menu is open.
    /// Toolbar submenu dismiss is handled by <see cref="SuppressExplorerMarquee"/> so
    /// empty-space unselect still runs on that click.
    /// </summary>
    private bool SuppressExplorerUnselect => ViewModel.IsMenuOpen;

    private void HookToolbarMenu(AdbMenu? menu)
    {
        if (menu is null)
            return;

        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnToolbarSubmenuOpened), true);
        menu.AddHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(OnToolbarSubmenuClosed), true);
    }

    private void OnToolbarSubmenuOpened(object sender, RoutedEventArgs e) => _toolbarSubmenuDepth++;

    private void OnToolbarSubmenuClosed(object sender, RoutedEventArgs e)
    {
        _toolbarSubmenuDepth = Math.Max(0, _toolbarSubmenuDepth - 1);
        if (_toolbarSubmenuDepth != 0)
            return;

        CancelExplorerMarquee();

        // Outside click dismisses with the button still down; Escape does not.
        if (Mouse.LeftButton is MouseButtonState.Pressed)
            _suppressSelectionAfterMenu = true;
    }

    private void CancelExplorerMarquee()
    {
        MouseDownPoint = NullPoint;
        CopyPaste.DragStatus = CopyPasteService.DragState.None;
        SelectionRect.Collapse();
    }

    private double? RowHeight { get; set; }
    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight") + ScrollContentPresenterMargin;
    private double ScrollContentPresenterMargin => ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top;
    private double DataGridContentWidth
        => StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid) is ItemsPresenter presenter ? presenter.ActualWidth : 0;

    private bool IsInEditMode
    {
        get
        {
            if (FileActions.IsAppDrive)
                return false;

            if (ActiveView.SelectedItem is not FileClass file)
                return false;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            return vm.IsInEditMode;
        }
        set
        {
            if (value && !FileActions.RenameEnabled)
                return;

            if (ActiveView.SelectedItem is not FileClass file)
                return;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            vm.IsInEditMode = value;
            FileActions.IsExplorerEditing = value;
        }
    }

    private readonly DispatcherTimer SelectionTimer = new() { Interval = SELECTION_CHANGED_DELAY };
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>
    /// Guards against a stray selection right after navigating (e.g. the second click of a
    /// double-click on a drive tile landing on the newly shown grid at the same screen position).
    /// Restarted on every navigation so back-to-back navigations don't race a stale continuation.
    /// </summary>
    private readonly DispatcherTimer _explorerLoadedTimer = new() { Interval = EXPLORER_NAV_DELAY };

    /// <summary>Debounces APK icon priority updates on scroll / selection.</summary>
    private readonly DispatcherTimer _apkPriorityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>
    /// Coalesces wrap-panel reflows (side pane open/close/resize) so we scroll once after layout.
    /// </summary>
    private int _keepSelectionInViewGeneration;

    private bool _isSyncingSelection = false;

    public ExplorerPageHeader(ExplorerViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture = Settings.ActualFormatCulture;

        DataContext =
        ViewModel = viewModel;

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        FileActions.PropertyChanged += (_, e) => App.SafeInvoke(() =>
        {
            if (e.PropertyName is nameof(FileActionsEnable.ExplorerFilter)
                && !string.IsNullOrEmpty(FileActions.ExplorerFilter))
            {
                ClearSelectionForSearch();
            }

            if (e.PropertyName is nameof(FileActionsEnable.IsAppDriveThumbsLocked))
                ApplyLocationThumbSize();
        });

        Data.RunExplorerSearch += (_, _) => App.SafeInvoke(() =>
        {
            if (Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        });
        Data.ExitSearchMode += (_, _) => App.SafeInvoke(() => ExitSearchMode());

        InitializeComponent();

        Loaded += (_, _) =>
        {
            DragAutoScroll.Register(ExplorerScrollViewer);
            DragAutoScroll.Register(IconScrollViewer);
        };
        Unloaded += (_, _) =>
        {
            DragAutoScroll.Unregister(ExplorerScrollViewer);
            DragAutoScroll.Unregister(IconScrollViewer);
        };

        HookToolbarMenu(MainToolBar);
        HookToolbarMenu(NavigationToolBar);
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(SortingSelector));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(ThumbsSizeSelector));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(SearchOptionsControl));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(DetailsControl));

        PreviewTextInput += ExplorerPageHeader_PreviewTextInput;

        NavigationBox.UnfocusTarget =
        SearchBox.UnfocusTarget = ActiveView;

        SelectionTimer.Tick += SelectionTimer_Tick;
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RunExplorerSearch();
        };
        _explorerLoadedTimer.Tick += (_, _) =>
        {
            _explorerLoadedTimer.Stop();
            RuntimeSettings.IsExplorerLoaded = true;
        };
        _apkPriorityTimer.Tick += (_, _) =>
        {
            _apkPriorityTimer.Stop();
            UpdateApkIconPriorities();
        };

        DriveList.SelectionChanged += DriveList_SelectionChanged;

        // Side pane open/close/resize changes column width; icon wrap reflow can push the
        // selection off-screen. Scroll it back after the layout pass settles.
        IconView.SizeChanged += IconView_SizeChanged;
        DriveList.SizeChanged += DriveList_SizeChanged;

        FileIconView.RenameStarted += IconView_RenameStarted;
        FileIconView.RenameEnded += (_, _) => ClearRename();

        ViewModel.RequestModeRefresh = () =>
        {
            DetailsPane.RequestModeRefresh?.Invoke();
            DetailsControl.RequestModeRefresh?.Invoke();
        };

        ItemToSelect.PropertyChanged += (s, e) =>
        {
            ActiveView.SelectedItem = ItemToSelect.Value;
            if (ItemToSelect is not null)
                ActiveScrollIntoView(ItemToSelect.Value);
        };

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerViewModel.IsIconView)
                or nameof(ExplorerViewModel.ExplorerItemsSource)
                or nameof(ExplorerViewModel.ExplorerSource))
            {
                ScheduleApkIconPriorityUpdate();
            }
        };
    }

    private void ExplorerPageHeader_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsFocused || SearchBox.IsKeyboardFocusWithin
            || DetailsPane.IsEditorFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || FileActions.IsExplorerEditing)
            return;

        var selected = ActiveSelectedItems.Count;
        var selectedIndex = ActiveView.SelectedIndex;
        IBrowserItem? nextItem = null;

        for (int i = 0; i < ActiveView.Items.Count; i++)
        {
            var item = (IBrowserItem)ActiveView.Items[i];
            var name = item.ToString();

            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                if (selected != 1 || selectedIndex < i)
                {
                    ItemToSelect.Value = item;
                    break;
                }
                else
                    nextItem ??= item;
            }
        }

        if (selectedIndex == ActiveView.SelectedIndex && nextItem is not null)
            ItemToSelect.Value = nextItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || SearchBox.IsKeyboardFocusWithin
            || NavigationBox.IsKeyboardFocusWithin
            || DetailsPane.IsEditorFocused
            || FileActions.IsExplorerEditing)
            return;

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && DevicesObject?.Current is not { IsOpen: true })
        {
            e.Handled = true;
            return;
        }

        bool handle = false;

        if (e.Key is Key.A && Keyboard.Modifiers is ModifierKeys.Control)
        {
            ToggleSelectAll();
            e.Handled = true;

            return;
        }
        
        if (e.Key is Key.Delete && FileActions.DeleteEnabled)
        {
            FileActionLogic.DeleteFiles();
            e.Handled = true;
            return;
        }

        if (!NAVIGATION_KEYS.Contains(e.Key))
            return;

        if (FileActions.IsExplorerVisible)
        {
            handle |= ExplorerGridKeyNavigation(e.Key);
        }
        else if (FileActions.IsDriveViewVisible)
        {
            handle |= DriveViewKeyNavigation(e.Key);
        }

        e.Handled = handle;
    }

    private bool DriveViewKeyNavigation(Key key)
    {
        if (DriveList.Items.Count == 0)
            return false;

        if (DriveList.SelectedItems.Count == 0)
        {
            switch (key)
            {
                case Key.Left or Key.Up:
                    DriveList.SelectedIndex = DriveList.Items.Count - 1;
                    break;

                case Key.Right or Key.Down:
                    DriveList.SelectedIndex = 0;
                    break;

                default:
                    return false;
            }

            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
            return true;
        }

        switch (key)
        {
            case Key.Enter:
                ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Execute();
                return true;

            case Key.Escape:
                // Should've been clear selected drives, but causes inconsistent behavior
                return true;

            default:
                return false;
        }
    }

    private bool ExplorerGridKeyNavigation(Key key)
    {
        if (ActiveView.Items.Count < 1 || DetailsPane.IsEditorFocused)
            return false;

        switch (key)
        {
            case Key.Escape:
                if (SelectionRect.IsActive)
                    return true;

                ActiveUnselectAll();
                break;

            case Key.Left or Key.Right when !ViewModel.IsIconView:
                return false;

            case Key.Down or Key.Up or Key.Left or Key.Right or Key.Home or Key.End:
                if (bfNavigation)
                {
                    ViewModel.CurrentSelectedIndex = ActiveView.SelectedIndex;
                    bfNavigation = false;
                }

                if (ViewModel.IsIconView)
                {
                    var navKey = key;
                    if (RuntimeSettings.IsRTL && navKey is Key.Left or Key.Right)
                        navKey = navKey == Key.Left ? Key.Right : Key.Left;

                    var step = navKey is Key.Left or Key.Right ? 1 : IconView.ItemsPerRow;

                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        IconView.MultiSelect(navKey, step, ViewModel);
                    else
                        IconView.SingleSelect(navKey, step, ViewModel);
                }
                else
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        ExplorerGrid.MultiSelect(key, ViewModel);
                    else
                        ExplorerGrid.SingleSelect(key, ViewModel);
                }
                break;

            case Key.Enter:
                // Shift+Enter is bound to FollowLink on the main window; do not swallow it here.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    return false;

                if (ExplorerGrid.SelectedCells.Count < 1 || IsInEditMode)
                    return false;

                if (ActiveSelectedItems.Count == 1
                    && ActiveView.SelectedItem is FileClass selected
                    && FileActionLogic.CanEnterSelection(selected))
                    DoubleClick(ActiveView.SelectedItem);
                return true;

            case Key.Apps:
                ActiveView.ContextMenu.IsOpen = true;
                break;

            default:
                return false;
        }

        return true;
    }

    private void SelectionTimer_Tick(object sender, EventArgs e)
    {
        SelectionTimer.Stop();
        ApplySelectionEffects();
    }

    private void ApplySelectionEffects()
    {
        var files = Files;
        files.SelectedFiles = files.Actions.IsAppDrive ? [] : (files.DirList?.FileList?.Where(f => f.IsSelected) ?? []);
        files.SelectedPackages = files.Actions.IsAppDrive
            ? (Packages?.Where(p => p.IsSelected) ?? [])
            : [];
        files.Actions.SelectedItemsCount = files.Actions.IsAppDrive
            ? files.SelectedPackages.Count()
            : files.SelectedFiles.Count();

        if (DetailsPane.IsOpen)
        {
            // Snapshot so OldValue isn't a live Where() that re-evaluates after selection changes.
            DetailsPane.SelectedFiles = files.Actions.IsAppDrive
                ? files.SelectedPackages.ToList()
                : files.SelectedFiles.ToList();
        }

        if (DevicesObject.Current is { SupportsLsV2: false })
        {
            foreach (var file in files.SelectedFiles.Where(f => f.IsRegularFile && f.ShellLsSize is null))
            {
                if (DetailsPane.IsOpen && !file.IsCreationTimeResolved)
                    continue;

                file.UpdateSizeFromShell(CancellationToken.None);
            }
        }

        ViewModel.NotifySelectedFilesTotalSize();

        FileActionLogic.UpdateFileActions(files);

        if (files.Actions.IsAppDrive)
            ScheduleApkIconPriorityUpdate();
    }

    private void ScheduleApkIconPriorityUpdate()
    {
        _apkPriorityTimer.Stop();
        _apkPriorityTimer.Start();
    }

    private void UpdateApkIconPriorities()
    {
        if (!FileActions.IsAppDrive || Data.Packages is null || Data.Packages.Count == 0)
            return;

        var selected = SelectedPackages.ToList();
        var visible = CollectVisiblePackages();
        ApkIconService.UpdatePackageLoadPriorities(selected, visible);
    }

    private List<Package> CollectVisiblePackages()
    {
        List<Package> visible = [];
        if (ViewModel.IsIconView)
        {
            var range = IconView.VisibleRange;
            var count = IconView.Items.Count;
            for (int i = range.StartIndex; i <= range.EndIndex && i < count; i++)
            {
                if (i < 0)
                    continue;
                if (IconView.Items[i] is Package package)
                    visible.Add(package);
            }
        }
        else
        {
            var generator = ExplorerGrid.ItemContainerGenerator;
            for (int i = 0; i < ExplorerGrid.Items.Count; i++)
            {
                if (generator.ContainerFromIndex(i) is null)
                    continue;
                if (ExplorerGrid.Items[i] is Package package)
                    visible.Add(package);
            }
        }

        return visible;
    }

    private void ExplorerScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!FileActions.IsAppDrive)
            return;
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0 && e.ExtentHeightChange == 0)
            return;

        ScheduleApkIconPriorityUpdate();
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.SafeInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.BrowseDrive) when RuntimeSettings.BrowseDrive:
                    InitNavigation(RuntimeSettings.BrowseDrive.Path);
                    break;

                case nameof(AppRuntimeSettings.DriveViewNav):
                    if (!DiskUsagePollingService.ServerUnresponsive)
                        DriveViewNav();
                    break;

                case nameof(AppRuntimeSettings.InitLister):
                    InitLister();
                    break;

                case nameof(AppRuntimeSettings.PathBoxNavigation):
                    if (RuntimeSettings.PathBoxNavigation == "-")
                    {
                        bfNavigation = true;
                        NavigateToLocation(NavHistory.GoBack());
                    }
                    else
                    {
                        if (FileActions.IsExplorerVisible)
                            NavigateToLocation(new(RuntimeSettings.PathBoxNavigation));
                        else
                        {
                            if (!InitNavigation(RuntimeSettings.PathBoxNavigation))
                            {
                                DriveViewNav();
                            }
                        }
                    }
                    break;

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    if (RuntimeSettings.LocationToNavigate is null)
                        return;

                    DirList?.Stop();
                    DisposeFileIcons();

                    switch (RuntimeSettings.LocationToNavigate.Location)
                    {
                        case Navigation.SpecialLocation.Back:
                            bfNavigation = true;
                            NavigateToLocation(NavHistory.GoBack());
                            break;
                        case Navigation.SpecialLocation.Forward:
                            bfNavigation = true;
                            NavigateToLocation(NavHistory.GoForward());
                            break;
                        case Navigation.SpecialLocation.Up:
                            bfNavigation = false;
                            if (FileActions.IsSearchMode)
                                ExitSearchMode();
                            else
                                NavigateToPath(ParentPath);
                            break;
                        default:
                            bfNavigation = false;
                            if (FileActions.IsDriveViewVisible && RuntimeSettings.LocationToNavigate.Location is Navigation.SpecialLocation.DriveView)
                                        FileActionLogic.RefreshDrives(true, DeviceCts.Token);
                            else
                                NavigateToLocation(RuntimeSettings.LocationToNavigate);
                            break;
                    }
                    break;

                case nameof(AppRuntimeSettings.FilterActions):
                    Task.Run(() =>
                    {
                        if (FileActions.IsAppDrive || FileActions.IsRecycleBin || DevicesObject.Current is null)
                            FilterFileActions();
                    });
                    Task.Run(() => ExplorerContextMenu.UpdateSeparators());
                    break;

                case nameof(AppRuntimeSettings.NewFolder):
                    NewItem(true);
                    break;

                case nameof(AppRuntimeSettings.NewFile):
                    NewItem(false);
                    break;

                case nameof(AppRuntimeSettings.CompressToExtension):
                    NewCompressItem();
                    break;

                case nameof(AppRuntimeSettings.Rename):
                    if (FileActions.RenameEnabled)
                        IsInEditMode ^= true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    ToggleSelectAll();
                    break;

                case nameof(AppRuntimeSettings.ThumbsSize):
                    OnThumbsSizeChanged();
                    break;

                case nameof(AppRuntimeSettings.IsSearchBoxFocused) when RuntimeSettings.IsSearchBoxFocused:
                    ClearSelectionForSearch();
                    break;

                default:
                    break;
            }
        });
    }

    private void ClearSelectionForSearch()
    {
        ActiveUnselectAll();
        ClearDataItemSelectionFlags();
        FileActions.SelectedItemsCount = 0;
        SelectedFiles = [];
        SelectedPackages = [];
        if (DetailsPane is not null)
            DetailsPane.SelectedFiles = [];
    }

    private void PathBoxFocus(bool isFocused)
    {
        if (isFocused)
            _focusPathBox();
        else
            _unfocusPathBox();

        void _focusPathBox()
        {
            NavigationBox.Mode = NavigationBox.ViewMode.Path;
        }

        void _unfocusPathBox()
        {
            if (NavigationBox.Mode is NavigationBox.ViewMode.None)
                return;

            NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        }
    }

    private void FilterFileActions() => App.SafeInvoke(() => MainToolBar.Items?.Refresh());

    private void NewItem(bool isFolder)
    {
        var fileName = FileHelper.DuplicateFile(DirList.FileList, isFolder
            ? Strings.Resources.S_NEW_FOLDER
            : Strings.Resources.S_NEW_ITEM);

        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        DirList.FileList.Insert(0, newItem);

        ActiveScrollIntoView(newItem);
        ActiveView.SelectedItem = newItem;

        IsInEditMode = true;
        if (!IsInEditMode) // in case the editing element was not acquired
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void NewCompressItem()
    {
        var extension = RuntimeSettings.CompressToExtension;
        if (string.IsNullOrEmpty(extension) || !FileActionLogic.IsPendingCompress)
            return;

        var sources = FileActionLogic.GetPendingCompressSourcePaths();
        string baseName;
        if (sources.Count > 0)
        {
            var firstName = FileHelper.GetFullName(sources[0]);
            var firstExt = FileHelper.GetExtension(firstName);
            baseName = string.IsNullOrEmpty(firstExt) ? firstName : firstName[..^firstExt.Length];
        }
        else
            baseName = Strings.Resources.S_NEW_ARCHIVE;

        var fileName = FileHelper.DuplicateFile(DirList.FileList, $"{baseName}{extension}");
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingCompressTemp(newItem);

        DirList.FileList.Insert(0, newItem);

        ActiveView.SelectedItem = newItem;
        ActiveScrollViewer?.ScrollToTop();
        App.SafeBeginInvoke(() => ActiveScrollViewer?.ScrollToTop(), DispatcherPriority.Loaded);

        IsInEditMode = true;
        if (!IsInEditMode)
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void InitLister()
    {
        Files.Device = DevicesObject.Current;
        DirList = new(App.AppDispatcher, DevicesObject.Current, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private bool TrySelectBackNavigationItem()
    {
        if (!bfNavigation)
            return false;

        var path = NavHistory.TakePendingSelectionPath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (NavHistory.FindBackNavigationItem(path) is not { } prevItem)
            return false;

        ItemToSelect.Value = prevItem;
        return true;
    }

    private void DirectoryLister_PropertyChanged(object sender, PropertyChangedEventArgs e) => App.SafeInvoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DirectoryLister.CurrentLocation):
                // Empty selection shows CurrentLocation in the details pane. Refresh on location
                // changes only (preliminary + final). InProgress no longer re-triggers the same load.
                if (DetailsPane.IsOpen && ActiveSelectedItems.Count == 0)
                    DetailsPane.RefreshSelection();
                break;

            case nameof(DirectoryLister.IsProgressVisible):
                UnfinishedBlock.Visible(DirList.IsProgressVisible);
                NavigationBox.IsLoadingProgressVisible = DirList.IsProgressVisible;
                break;

            case nameof(DirectoryLister.InProgress):
                {
                    Task.Run(() =>
                    {
                        if (!DirList.InProgress)
                            Task.Delay(EMPTY_FOLDER_NOTICE_DELAY);

                        App.SafeInvoke(() =>
                        {
                            FileActions.ListingInProgress = DirList.InProgress;
                            FileActionLogic.UpdateFileActions();
                        });
                    });

                    if (DirList.InProgress)
                        return;

                    if (FileActions.IsRecycleBin)
                        TrashHelper.EnableRecycleButtons();

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when !DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    ViewModel.NotifyDirectoryLinksResolved();

                    if (DirList.FileList.Count > 0)
                    {
                        SortExplorer();

                        if (!TrySelectBackNavigationItem())
                        {
                            ActiveScrollIntoView(ActiveView.Items[0]);

                            if (Settings.ThumbsMode is AppSettings.ThumbnailMode.OnPhotoDir
                                && !ThumbnailService.IsInitialized(DevicesObject.Current.SerialNumber)
                                && FileHelper.IsPhotoDir())
                            {
                                Task.Run(() => ThumbnailService.ForceLoad(DevicesObject.Current));
                            }
                        }
                    }

                    break;
                }
        }
    });

    private bool InitNavigation(string path = "")
    {
        if (path is null)
            return true;

        var realPath = FolderHelper.FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path);
        if (realPath is null)
            return false;

        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = true;
        RuntimeSettings.BrowseDrive = null;
        RuntimeSettings.SelectedDrive = null;

        _explorerLoadedTimer.Stop();
        _explorerLoadedTimer.Start();

        return _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath, FileClass? locationSource = null)
    {
        FileActions.IsSearchMode = false;
        Data.SearchOriginPath = null;
        Data.SearchOriginCanWrite = false;
        Data.SearchTransferParent = null;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        Files.DirList?.Stop();

        ArchivePath.InvalidateCache();

        var deviceId = DevicesObject.Current?.ID;
        var isArchive = ArchivePath.IsArchivePath(realPath, deviceId);
        var devicePath = isArchive ? ArchivePath.GetArchivePath(realPath, deviceId) : realPath;

        FileActions.ListingInProgress = true;

        FileActions.WasInAppDrive = FileActions.IsAppDrive;
        FileActions.ExplorerFilter = "";
        NavHistory.Navigate(realPath);

        ViewModel.FirstSelectedIndex = -1;
        ViewModel.CurrentSelectedIndex = -1;
        ActiveUnselectAll();

        if (DetailsPane.IsOpen)
            DetailsPane.SelectedFiles = [];

        ActiveView.Focus();

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        NavigationBox.Path = realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath;
        CurrentDrive = DriveHelper.GetCurrentDrive(devicePath);
        FileActions.IsRecycleBin = realPath == RECYCLE_PATH;
        FileActions.IsAppDrive = realPath == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive);
        FileActions.IsArchive = isArchive;
        FileActions.IsTemp = realPath == TEMP_PATH;
        FileActions.ParentEnabled = realPath != FileHelper.GetParentPath(realPath)
            && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        if (Files.DirList is null && DevicesObject.Current is not null)
            InitLister();

        if (FileActions.IsAppDrive && Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
            Settings.SearchBox = SearchBox.SearchBoxMode.CurrentFolder;

        CurrentPath = realPath;

        FileActionLogic.IsPasteEnabled();

        FileActions.PushPackageEnabled = Settings.EnableApk && DevicesObject?.Current?.Type is not DeviceType.Recovery;
        FileActions.UninstallPackageEnabled = false;

        FileActions.ContextPushPackagesEnabled =
        FileActions.IsUninstallVisible.Value = FileActions.IsAppDrive;
        FileActions.IsCutPasteDeleteVisible.Value = !FileActions.IsAppDrive;
        FileActions.IsPullCopyVisible.Value = !FileActions.IsRecycleBin;
        FileActions.IsPasteVisible.Value = !FileActions.IsAppDrive && !FileActions.IsRecycleBin;

        FileActions.CopyPathDescription.Value = FileActions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;

        ApplyLocationThumbSize();

        SortExplorer();

        if (FileActions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync(DeviceCts.Token).ContinueWith(_ => Files.DirList?.Navigate(realPath));

            FileActions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            FileActions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
        }
        else
        {
            if (FileActions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true, DeviceCts.Token);
                FileActionLogic.UpdateFileActions();
                ResetExplorerHorizontalScroll();
                return true;
            }

            if (Files.DirList is null)
                return false;

            Files.DirList.Navigate(realPath, locationSource);

            FileActions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        if (Files.DirList is not null)
            ViewModel.ExplorerSource = Files.DirList.FileList;

        FileActionLogic.UpdateFileActions();

        ResetExplorerHorizontalScroll();

        return true;
    }

    private void RunExplorerSearch()
    {
        if (Settings.SearchBox is not SearchBox.SearchBoxMode.AllSubfolders
            || !FileActions.IsExplorerVisible
            || FileActions.IsAppDrive
            || FileActions.IsRecycleBin
            || DevicesObject?.Current is null
            || DirList is null)
        {
            return;
        }

        var query = FileActions.ExplorerFilter?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            if (FileActions.IsSearchMode)
                ExitSearchMode();
            return;
        }

        if (!FileActions.IsSearchMode)
        {
            Data.SearchOriginPath = CurrentPath;
            var deviceId = DevicesObject?.Current?.ID;
            Data.SearchOriginCanWrite = DirList?.CurrentLocation is { FullPath: var locationPath, CanWriteLocation: true } location
                && locationPath == CurrentPath
                || deviceId is not null && DriveHelper.IsModificationAllowedAt(CurrentPath, deviceId);
        }

        var searchRoot = Data.SearchOriginPath;
        if (string.IsNullOrEmpty(searchRoot)
            || AdbLocation.LocationFromString(searchRoot) is not Navigation.SpecialLocation.None)
        {
            return;
        }

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        DirList.Stop();
        DisposeFileIcons();

        FileActions.ListingInProgress = true;
        FileActions.IsSearchMode = true;
        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = true;
        FileActions.IsRecycleBin = false;
        FileActions.IsAppDrive = false;
        FileActions.IsArchive = false;
        FileActions.IsTemp = false;
        FileActions.ParentEnabled = false;

        var searchPath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.SearchMode);
        CurrentPath = searchPath;
        NavigationBox.Path = searchPath;
        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

        ViewModel.FirstSelectedIndex = -1;
        ViewModel.CurrentSelectedIndex = -1;
        ActiveUnselectAll();

        if (DetailsPane.IsOpen)
            DetailsPane.SelectedFiles = [];

        ApplyLocationThumbSize();

        SortExplorer();
        DirList.Search(searchRoot, query, DeviceCts.Token);
        ViewModel.ExplorerSource = DirList.FileList;
        FileActionLogic.UpdateFileActions();
        ResetExplorerHorizontalScroll();

        if (DetailsPane.IsOpen)
            DetailsPane.RefreshSelection();
    }

    private void ExitSearchMode()
    {
        if (!FileActions.IsSearchMode)
            return;

        var origin = Data.SearchOriginPath;
        FileActions.IsSearchMode = false;
        Data.SearchOriginPath = null;
        Data.SearchOriginCanWrite = false;
        Data.SearchTransferParent = null;
        FileActions.ExplorerFilter = "";

        if (!string.IsNullOrEmpty(origin))
            NavigateToPath(origin);
    }

    private void SortExplorer()
    {
        if (Settings.SortingPerLocation && Settings.LocationSorting.TryGetValue(CurrentPath, out var sort))
        {
            if (FileActions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.Date or SortingSelector.SortingProperty.Size)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else if (!FileActions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.UserId or SortingSelector.SortingProperty.Version)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else
            {
                ViewModel.SetSort(sort);
            }
        }
        else
        {
            ViewModel.SetSort(SortingSelector.SortingProperty.Name, ListSortDirection.Ascending);
        }
    }

    /// <summary>
    /// Restores the preferred thumb size for the current path (per-location or global).
    /// App drive without <c>unzip</c> stays in details view without overwriting saved sizes.
    /// </summary>
    private void ApplyLocationThumbSize()
    {
        if (FileActions.IsDriveViewVisible)
        {
            ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
            return;
        }

        if (string.IsNullOrEmpty(CurrentPath))
            return;

        if (FileActions.IsAppDriveThumbsLocked)
        {
            ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Disabled;
            return;
        }

        if (Settings.ThumbSizePerLocation)
        {
            ThumbnailService.ThumbnailSize size = ThumbnailService.ThumbnailSize.Disabled;
            Settings.LocationThumbSize.TryGetValue(CurrentPath, out size);
            ViewModel.CurrentThumbsSize = size;
        }
        else
        {
            ViewModel.CurrentThumbsSize = RuntimeSettings.ThumbsSize;
        }

        if (FileActions.IsAppDrive
            && Data.Packages is { Count: > 0 }
            && ApkIconService.IsEnabled)
            ApkIconService.BeginPreloadPackages(Data.Packages);
    }

    private static void InvalidateFileIcons()
    {
        if (DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
            file.InvalidateIconViewModelThumbnail();
    }

    private static void DisposeFileIcons()
    {
        if (DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
        {
            file.DisposeIconViewModel();
        }

        Task.Run(static () =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        });
    }

    private void NavigateToLocation(AdbLocation location)
    {
        ViewModel.IsMenuOpen = false;

        if (location.Location is Navigation.SpecialLocation.DriveView)
        {
            if (DiskUsagePollingService.ServerUnresponsive || DevicesObject?.Current is null)
                return;

            FileActions.IsRecycleBin = false;
            PathBoxFocus(false);
            RaiseUnfocusSearchBox();
            FileActionLogic.RefreshDrives(true, DeviceCts.Token);
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
        else
        {
            if (location.Location is Navigation.SpecialLocation.SearchMode)
            {
                if (!string.IsNullOrEmpty(FileActions.ExplorerFilter))
                    RunExplorerSearch();
                return;
            }

            var path = string.IsNullOrEmpty(location.Path)
                ? location.StringFromLocation()
                : location.Path;

            if (!FileActions.IsExplorerVisible)
            {
                if (!InitNavigation(path))
                    DriveViewNav();
            }
            else
                NavigateToPath(path);
        }
    }

    public bool NavigateToPath(FileClass file)
    {
        if (file is null)
            return false;

        if (!FileActions.IsAppDrive
            && DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, FileActions.IsArchive))
        {
            return _navigateToPath(ArchivePath.Join(file.FullPath, ""), file);
        }

        string realPath = !string.IsNullOrEmpty(file.LinkTarget)
            ? file.LinkTarget
            : file.FullPath;

        return realPath is not null && _navigateToPath(realPath, file);
    }

    public bool NavigateToPath(string path)
    {
        if (path is null)
            return false;

        var realPath = FolderHelper.FolderExists(path);
        if (realPath is null)
            return false;

        var locationSource = DirList?.FileList.FirstOrDefault(f => f.IsDirectory && f.FullPath == realPath);
        return _navigateToPath(realPath, locationSource);
    }

    private void DriveViewNav()
    {
        if (DiskUsagePollingService.ServerUnresponsive || DevicesObject?.Current is null)
            return;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        FileActionLogic.ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        CurrentPath =
        NavigationBox.Path = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

        CurrentDrive = null;

        if (!bfNavigation)
        {
            DriveList.SelectedIndex = -1;
            RuntimeSettings.SelectedDrive = null;
        }

        if (DriveList.SelectedIndex > -1)
        {
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();

            if (DetailsPane.IsOpen)
                DetailsPane.SelectedFiles = DriveList.SelectedItem is DriveViewModel selectedDrive ? [selectedDrive] : [];
        }

        RuntimeSettings.SelectedDrive = DriveList.SelectedItem as DriveViewModel;
        FileActionLogic.UpdateFileActions();

        ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
    }

    private void DataGridCell_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is DataGridCell && e.TargetRect == Rect.Empty)
        {
            e.Handled = true;
        }
    }

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        SelectionRect.ResetGesture();

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        CopyPaste.WasDragging = false;

        var cell = sender as DataGridCell;
        WasEditing = cell.DataContext is FileClass clickedFile && clickedFile.FolderViewModel.IsInEditMode;

        if (WasEditing)
            return;

        var row = DataGridRow.GetRowContainingElement(cell);
        var current = row.GetIndex();

        WasSelected = row.IsSelected;

        if (e.ChangedButton is MouseButton.Right && !WasSelected)
        {
            SelectOnlyItem(row.Item);
            e.Handled = true;
            return;
        }

        TrackExplorerMouseDown(e, e.OriginalSource as DependencyObject);
        e.Handled = true;
        ClickCount = e.ClickCount;

        if (ClickCount > 1)
        {
            DoubleClick(cell.DataContext);
            ClickCount = -1;
            return;
        }

        PathBoxFocus(false);
        RaiseUnfocusSearchBox();

        if (!row.IsSelected
            && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectOnlyItem(row.Item);
        }

        ViewModel.NextSelectedIndex = current;
        ViewModel.CurrentSelectedIndex = current;
        if (ExplorerGrid.SelectedItems.Count < 1)
            ViewModel.FirstSelectedIndex = current;
    }

    /// <summary>
    /// Clears selection on both views and on virtualized <see cref="FilePath.IsSelected"/> flags,
    /// then selects <paramref name="item"/> alone.
    /// </summary>
    private void SelectOnlyItem(object item)
    {
        if (Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift)
            return;

        ClearDataItemSelectionFlags();
        ActiveUnselectAll();

        if (item is FilePath filePath)
            filePath.IsSelected = true;
        else if (item is Package package)
            package.IsSelected = true;

        ActiveView.SelectedItem = item;
    }

    private void ClearDataItemSelectionFlags()
    {
        if (FileActions.IsAppDrive)
        {
            // Prefer the full package list — ActiveView.Items may omit filtered/system packages
            // while their IsSelected flags still linger from virtualization.
            var packages = Data.Packages ?? ExplorerGrid.Items.OfType<Package>();
            foreach (var pkg in packages)
            {
                if (pkg.IsSelected)
                    pkg.IsSelected = false;
            }
            return;
        }

        if (DirList?.FileList is null)
            return;

        foreach (var file in DirList.FileList)
        {
            if (file.IsSelected)
                file.IsSelected = false;
        }
    }

    private void DoubleClick(object source)
    {
        FileIconView.CancelDelayedRename();

        if (FileActions.IsRecycleBin)
            return;

        if (source is not FileClass file)
        {
            if (source is Package apk && !FileActions.ListingInProgress)
                FileActionLogic.OpenApkLocation(apk);

            return;
        }

        if (file.Type is FileType.Folder)
        {
            if (!FileActions.ListingInProgress)
            {
                bfNavigation = false;
                NavigateToPath(file);
            }

            return;
        }
        else if (file.Type is not FileType.File)
            return;

        if (!FileActions.IsAppDrive
            && DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, FileActions.IsArchive))
        {
            if (!FileActions.ListingInProgress)
            {
                bfNavigation = false;
                NavigateToPath(file);
            }

            return;
        }

        if (Settings.DoubleClickToPull
            && Settings.IsPullOnDoubleClickEnabled
            && FileActions.PullEnabled)
        {
            FileActionLogic.PullFiles(Settings.DefaultFolder);
        }
    }

    private void DataGridCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || ClickCount < 0)
            return;

        if (SelectionRect.IsActive || SelectionRect.SelectionOccurred)
        {
            SelectionRect.Collapse();
            e.Handled = true;

            return;
        }

        e.Handled = CellMouseUp(sender, e);

        CopyPaste.DragStatus = CopyPasteService.DragState.None;
    }

    private bool CellMouseUp(object sender, MouseButtonEventArgs e)
    {
        DataGridCell cell;
        DataGridRow row;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Active || CopyPaste.WasDragging)
        {
            CopyPaste.WasDragging = false;
            return true;
        }

        switch (sender)
        {
            case DataGridCell c:
                {
                    cell = c;
                    row = DataGridRow.GetRowContainingElement(cell);

                    if (cell.DataContext is FileClass clickedFile && clickedFile.FolderViewModel.IsInEditMode)
                        return false;
                    break;
                }
            case DataGridRow r:
                row = r;
                cell = null;
                break;
            default:
                return false;
        }

        var current = row.GetIndex();
        ViewModel.CurrentSelectedIndex = current;

        if (MultiRowSelect(row))
            return true;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = current;
        }

        if (!row.IsSelected || ExplorerGrid.SelectedItems?.Count != 1)
        {
            ExplorerGrid.UnselectAll();
            row.IsSelected = true;
            return true;
        }

        if (cell?.Column == NameColumn)
            MouseUpOnName(cell);

        return true;
    }

    private void MouseUpOnName(DataGridCell cell)
    {
        if (!DevicesObject.Current.HasRootShell
            && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder))
            return;

        if (!FileActions.RenameEnabled)
            return;

        var file = (FileClass)ExplorerGrid.SelectedItem;
        var path = file.FullPath;

        if (ExplorerGrid.SelectedItems.Count == 1 && WasSelected && !WasEditing)
        {
            Task.Run(() =>
            {
                var start = DateTime.Now;

                while (true)
                {
                    Task.Delay(100);

                    if (DateTime.Now - start > RENAME_CLICK_DELAY)
                        break;

                    var currentPath = App.AppDispatcher?.Invoke(() => ((FileClass)ExplorerGrid.SelectedItem)?.FullPath);
                    if (ClickCount != 1 || currentPath != path)
                        return;
                }

                App.SafeInvoke(() =>
                {
                    if (ClickCount != 1)
                        return;

                    file.FolderViewModel.IsInEditMode = true;
                    FileActions.IsExplorerEditing = true;
                });
            });
        }
    }

    private bool MultiRowSelect(DataGridRow row)
    {
        var current = row.GetIndex();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ExplorerGrid.UnselectAll();

            var firstSelected = ViewModel.FirstSelectedIndex;
            int firstUnselected = firstSelected, lastUnselected = current + 1;
            if (current < firstSelected)
            {
                firstUnselected = current;
                lastUnselected = firstSelected + 1;
            }

            for (int i = firstUnselected; i < lastUnselected; i++)
            {
                ExplorerGrid.SelectedItems.Add(ExplorerGrid.Items[i]);
            }

            return true;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            row.IsSelected = !row.IsSelected;
            return true;
        }

        return false;
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        SelectionRect.ResetGesture();

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        CopyPaste.WasDragging = false;
        var row = sender as DataGridRow;

        TrackExplorerMouseDown(e, e.OriginalSource as DependencyObject);

        ViewModel.SetIndexSingle(row.GetIndex());
    }

    private void ItemContainer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInEditMode || ActiveSelectedItems.Count != 1)
            return;

        ClickCount = -1;
        DoubleClick(ActiveView.SelectedItem);
    }

    private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
    {
        //if (RuntimeSettings.IsSettingsPaneOpen || RuntimeSettings.IsDevicesPaneOpen)
        //    return;

        var key = e.Key;
        switch (key)
        {
            case Key.Enter when IsInEditMode:
                return;
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                return;
            case Key.Enter:
                {
                    if (ExplorerGrid.SelectedItems.Count == 1
                        && ExplorerGrid.SelectedItem is FileClass selected
                        && FileActionLogic.CanEnterSelection(selected))
                        DoubleClick(ExplorerGrid.SelectedItem);
                    break;
                }
            case Key.Back:
                NavHistory.NavigateBF(Navigation.SpecialLocation.Back);
                break;

            case Key.Delete when FileActions.DeleteEnabled:
                FileActionLogic.DeleteFiles();
                break;

            case Key.Up or Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ExplorerGrid.MultiSelect(key, ViewModel);
                break;

            case Key.Up or Key.Down:
                ExplorerGrid.SingleSelect(key, ViewModel);
                break;

            case Key.F2:
                if (FileActions.RenameEnabled)
                    AppActions.List.First(action => action.Name is FileActionType.Rename).Command.Execute();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
        CopyPaste.AcceptDataObject(e, (FrameworkElement)sender);
        e.Handled = true;
    }

    private void Row_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = true;
    }

    private void Row_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = false;
    }

    private void Row_MouseLeave(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = false;
    }

    private void ExplorerGrid_DragOver(object sender, DragEventArgs e)
    {
        var allowed = CopyPaste.GetAllowedDragEffects(e.Data, (FrameworkElement)sender);

        if (allowed.HasFlag(DragDropEffects.Move) && CopyPaste.IsSelf && !e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) && !e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Link;
        }
        else if (allowed.HasFlag(DragDropEffects.Copy)) // copy is the default and does not require Ctrl to be activated
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
            e.Effects = allowed;

        if ((!allowed.HasFlag(DragDropEffects.Copy) && e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            || (!allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
            || (!allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey)))
        {
            e.Effects = DragDropEffects.None;
        }

        CopyPaste.DropEffect =
        CopyPaste.CurrentDropEffect = e.Effects;

        if (FileActions.IsAppDrive)
        {
            // Incoming APK / APKBKP install: always the default package large icon, not the
            // Windows shell APK glyph or a selected package's parsed launcher icon.
            if (!CopyPaste.IsSelf && FileHelper.AllFilesAreApks(CopyPaste.DragFiles))
            {
                CopyPaste.DragBitmap = DefaultAndroidPackageIcon.Bitmap;
            }
            else
            {
                // Outgoing package drag: CurrentFiles are APK paths whose DragImage is the shell placeholder.
                var packageIcon = ActiveSelectedItems.OfType<Package>().Select(p => p.Icon).FirstOrDefault(i => i is not null)
                    ?? Data.SelectedPackages.Select(p => p.Icon).FirstOrDefault(i => i is not null);
                if (packageIcon is not null)
                    CopyPaste.DragBitmap = packageIcon;
            }
        }
        else if (CopyPaste.CurrentFiles.Any())
        {
            CopyPaste.DragBitmap = CopyPaste.CurrentFiles.First().DragImage;
        }

        e.Handled = true;
    }

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        ViewModel.IsMenuOpen = false;
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!ViewModel.IsIconView)
        {
            var point = Mouse.GetPosition(ExplorerGrid);
            if (point.Y < ColumnHeaderHeight || CopyPaste.WasDragging)
            {
                ViewModel.IsMenuOpen = false;
                e.Handled = true;
                ClearWasDraggingAfterContext();
                return;
            }
        }
        else if (CopyPaste.WasDragging)
        {
            ViewModel.IsMenuOpen = false;
            e.Handled = true;
            ClearWasDraggingAfterContext();
            return;
        }

        ViewModel.IsMenuOpen = true;
        FileActionLogic.UpdateFileActions();
        ExplorerContextMenu.UpdateSeparators();

        if (e.Source is FrameworkElement target)
            target.ContextMenu = CreateRowContextMenu();
    }

    private void ClearWasDraggingAfterContext()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (CopyPaste.DragStatus is not CopyPasteService.DragState.Active)
                CopyPaste.WasDragging = false;
        }, DispatcherPriority.Input);
    }

    private AdbContextMenu CreateRowContextMenu() => new()
    {
        Style = TryFindResource("ExplorerContextMenuStyle") as Style,
    };

    private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        if (RowHeight is null && ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row)
            RowHeight = row.ActualHeight;

        CopyPaste.WasDragging = false;
        TrackExplorerMouseDown(e, e.OriginalSource as DependencyObject);

        if (HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject))
        {
            MouseDownPoint = NullPoint;
            return;
        }

        var gridPoint = e.GetPosition(ExplorerGrid);

        int selectionIndex = ExplorerGrid.SelectedIndex;

        var actualRowWidth = ExplorerGrid.Columns
            .Where(col => col.Visibility == Visibility.Visible)
            .Sum(item => item.ActualWidth);

        var source = e.OriginalSource as DependencyObject;
        var onHeader = HitTestHelper.FindAncestor<DataGridColumnHeader>(source) is not null;
        var onRow = HitTestHelper.FindAncestor<DataGridRow>(source) is not null;
        var rightOfRows = gridPoint.X > actualRowWidth || gridPoint.X > DataGridContentWidth;

        if (!onHeader && (!onRow || rightOfRows))
        {
            if (ExplorerGrid.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if ((e.ChangedButton is MouseButton.Right || !SuppressExplorerUnselect)
                && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ClearDataItemSelectionFlags();
                ExplorerGrid.UnselectAll();
                ExplorerGrid.SelectedIndex =
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(SelectionRect);
        bool withinEditingCell = false;
        DataGridCell cell = ExplorerGrid.SelectedCells.Count > 0
                            ? CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1])
                            : null;

        if (IsInEditMode && cell is not null)
        {
            withinEditingCell = VisualTreeHelper.GetDescendantBounds(cell).Contains(e.GetPosition(cell));
        }

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || withinEditingCell
            || SuppressExplorerMarquee
            || (!SelectionRect.IsActive && HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject));

        TryBeginExplorerDragOrMarquee(point, abortDrag, ExplorerScrollViewer, cell);
    }

    private void InitiateDrag(DependencyObject dragSource)
    {
        IEnumerable<FileClass> selectedItems;
        VirtualFileDataObject? vfdo;
        if (FileActions.IsAppDrive)
        {
            vfdo = VirtualFileDataObject.PrepareTransfer(ActiveSelectedItems.Cast<Package>());
            selectedItems = VirtualFileDataObject.SelfFiles;
        }
        else
        {
            selectedItems = ActiveSelectedItems.Cast<FileClass>();
            // Archive extract is copy-only (no cut / symlink from inside an archive).
            var effects = FileActions.IsArchive
                ? DragDropEffects.Copy
                : DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

            vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, effects);
            if (FileActions.IsArchive && vfdo is not null)
                vfdo.PreferredDropEffect = DragDropEffects.Copy;
        }

        if (vfdo is null)
            return;

        CopyPaste.DragStatus = CopyPasteService.DragState.Active;
        CopyPaste.WasDragging = true;
        CopyPaste.UpdateSelfVFDO(true);

        if (FileActions.IsAppDrive)
        {
            var package = ActiveSelectedItems.OfType<Package>().FirstOrDefault();
            // Prefer the parsed launcher icon already shown in the tile (not APK shell / placeholder).
            CopyPaste.DragBitmap = package?.Icon
                ?? VirtualFileDataObject.SelfFiles?.FirstOrDefault()?.ApkIcon
                ?? package?.IconViewModel.LargeIcon;
        }
        else
        {
            CopyPaste.DragBitmap = selectedItems.First().DragImage;
        }

        DragAutoScroll.Register(ActiveScrollViewer);
        DragAutoScroll.Begin();
        try
        {
            vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, dragSource, vfdo.PreferredDropEffect.Value);
        }
        finally
        {
            DragAutoScroll.End();
            // Escape (and other OLE cancels) leave the button down; drop the original
            // mouse-down so MouseMove cannot start a rubber-band from that point.
            MouseDownPoint = NullPoint;
            SelectionRect.Collapse();
        }
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
            return;

        CommitRenameIfDeselected();

        if (ActiveSelectedItems.Count > 0 && !RuntimeSettings.IsExplorerLoaded)
        {
            ActiveUnselectAll();
            return;
        }

        if (!ViewModel.SelectionInProgress)
        {
            if (ActiveSelectedItems.Count == 1)
            {
                ViewModel.CurrentSelectedIndex = ActiveView.SelectedIndex;
                if (ViewModel.FirstSelectedIndex < 0
                    || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
                {
                    ViewModel.FirstSelectedIndex = ActiveView.SelectedIndex;
                }
            }
            else if (ActiveSelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                ViewModel.CurrentSelectedIndex = ActiveView.Items.IndexOf(e.AddedItems[0]);
            }
        }

        SyncSelectionToOtherView(sender, e);

        bool isOngoingMultiSelection = SelectionRect.IsActive
                || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (!isOngoingMultiSelection && ActiveSelectedItems.Count <= 1)
        {
            SelectionTimer.Stop();
            ApplySelectionEffects();
        }
        else if (!SelectionTimer.IsEnabled)
        {
            SelectionTimer.Start();
        }
    }

    private void SyncSelectionToOtherView(object sender, SelectionChangedEventArgs e)
    {
        if (FileActions.IsAppDrive)
        {
            // Fix IsSelected on Package items whose containers were recycled by virtualization
            // so UnselectAll() could not propagate through the TwoWay binding.
            var selectedSet = new HashSet<object>(ActiveSelectedItems.Cast<object>());
            var packages = Data.Packages ?? ActiveView.Items.OfType<Package>();
            foreach (var pkg in packages)
            {
                var shouldSelect = selectedSet.Contains(pkg);
                if (pkg.IsSelected != shouldSelect)
                    pkg.IsSelected = shouldSelect;
            }

            // Keep the hidden view in sync when switching between details / icon view.
            _isSyncingSelection = true;
            try
            {
                var sourceItems = ActiveSelectedItems.Cast<object>().ToList();
                System.Collections.IList targetItems = ViewModel.IsIconView
                    ? ExplorerGrid.SelectedItems
                    : IconView.SelectedItems;

                var toRemove = targetItems.Cast<object>()
                    .Where(item => !sourceItems.Contains(item))
                    .ToList();
                foreach (var item in toRemove)
                    targetItems.Remove(item);

                foreach (var item in sourceItems)
                {
                    if (!targetItems.Contains(item))
                        targetItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }

            return;
        }

        _isSyncingSelection = true;
        try
        {
            var sourceItems = (sender == ExplorerGrid
                ? ExplorerGrid.SelectedItems
                : IconView.SelectedItems).Cast<object>().ToList();

            System.Collections.IList targetItems = sender == ExplorerGrid
                ? IconView.SelectedItems
                : ExplorerGrid.SelectedItems;

            var toRemove = targetItems.Cast<object>()
                .Where(item => !sourceItems.Contains(item))
                .ToList();
            foreach (var item in toRemove)
                targetItems.Remove(item);

            foreach (var item in sourceItems)
            {
                if (!targetItems.Contains(item))
                    targetItems.Add(item);
            }

            // Fix IsSelected on underlying data items for virtualized containers
            // that had no container when UnselectAll() was called, and were therefore
            // skipped by the TwoWay binding propagation.
            var sourceSet = new HashSet<object>(sourceItems);
            var files = DirList?.FileList ?? ExplorerGrid.Items.OfType<FilePath>();
            foreach (var item in files)
            {
                var shouldSelect = sourceSet.Contains(item);
                if (item.IsSelected != shouldSelect)
                    item.IsSelected = shouldSelect;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        SortingSelector.SortingProperty sortedColumn;
        if (FileActions.IsAppDrive)
        {
            sortedColumn = e.Column switch
            {
                var c when c == PackageType => SortingSelector.SortingProperty.Type,
                var c when c == PackageUid => SortingSelector.SortingProperty.UserId,
                var c when c == PackageVersion => SortingSelector.SortingProperty.Version,
                _ => SortingSelector.SortingProperty.Name,
            };
        }
        else
        {
            sortedColumn = e.Column switch
            {
                var c when c == DateColumn => SortingSelector.SortingProperty.Date,
                var c when c == TypeColumn => SortingSelector.SortingProperty.Type,
                var c when c == SizeColumn => SortingSelector.SortingProperty.Size,
                _ => SortingSelector.SortingProperty.Name,
            };
        }

        var currentDirection = sortedColumn == ViewModel.SortedColumn ? ViewModel.SortDirection : null;
        var direction = ListHelper.Invert(currentDirection);
        ViewModel.SetSort(sortedColumn, direction);

        e.Column.SortDirection = direction;
        e.Handled = true;
    }

    private void BeginRename(TextBox textBox) => _renameTextBox = textBox;

    public void ShowRenameTooltip(FrameworkElement anchor, object dataContext)
        => RenameTooltipControl.Show(anchor, dataContext);

    public void FocusActiveListing() => ActiveView.Focus();

    private void ClearRename() => _renameTextBox = null;

    private void CommitRenameIfDeselected()
    {
        if (_renameTextBox?.DataContext is not FileClass file)
        {
            ClearRename();
            return;
        }

        var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
        if (!vm.IsInEditMode)
        {
            ClearRename();
            return;
        }

        if (ActiveSelectedItems.Count == 1 && ReferenceEquals(ActiveSelectedItems[0], file))
            return;

        FileViewModelBase.RenameCommit(_renameTextBox, ViewModel.IsIconView ? ExitIconEditMode : ExitFolderEditMode);
    }

    private void ExitFolderEditMode(FileClass file)
    {
        file.FolderViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void ExitIconEditMode(FileClass file)
    {
        file.IconViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        FileViewModelBase.RenameKeyDown(textBox, e.Key, ExitFolderEditMode);
        if (e.Key is Key.Escape or Key.F2 or Key.Enter)
            e.Handled = true;
    }

    private void NameColumnEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        var textBox = sender as TextBox;

        if (textBox.DataContext is FileClass file)
        {
            FileViewModelBase.PrepareRenameTextBox(textBox);
            BeginRename(textBox);
            RenameTooltipControl.Show(textBox, file.FolderViewModel);
        }
    }

    private void IconView_RenameStarted(object sender, TextBox textBox)
    {
        BeginRename(textBox);
        if (textBox.DataContext is FileClass file)
            RenameTooltipControl.Show(textBox, file.IconViewModel, centerHorizontally: true);
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (textBox.DataContext is FileClass file && !file.FolderViewModel.IsInEditMode)
            return;

        FileViewModelBase.RenameCommit(textBox, ExitFolderEditMode);
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        FileViewModelBase.RenameTextChanged(textBox);
    }

    private void SelectionRect_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SelectionRect.IsActive || SelectionRect.SelectionOccurred)
            e.Handled = true;

        SelectionRect.Collapse();

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = ViewModel.NextSelectedIndex;
        }
    }

    private void SelectionRect_MouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel.IsIconView)
            IconView_MouseMove(sender, e);
        else
            ExplorerGrid_MouseMove(sender, e);
    }

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        // Tunneling: run before UnselectAll so a nested MouseMove cannot start a rubber-band
        // from a stale down-point (item that was selected when the menu opened).
        if (_toolbarSubmenuDepth > 0 || _suppressSelectionAfterMenu)
        {
            _suppressSelectionAfterMenu = true;
            CancelExplorerMarquee();
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        EndExplorerMouseGesture();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Pressed || SelectionRect.IsActive)
            return;

        MouseDownPoint = NullPoint;
    }

    private void EndExplorerMouseGesture()
    {
        if (SelectionRect.IsActive)
            SelectionRect.Collapse();

        MouseDownPoint = NullPoint;
        CopyPaste.WasDragging = false;
        _suppressSelectionAfterMenu = false;
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        EndExplorerMouseGesture();

        if (FileActions.ListingInProgress && e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
            return;
        }

        e.Handled = e.ChangedButton switch
        {
            MouseButton.XButton1 => NavHistory.NavigateBF(Navigation.SpecialLocation.Back),
            MouseButton.XButton2 => NavHistory.NavigateBF(Navigation.SpecialLocation.Forward),
            _ => false,
        };
    }

    private void MainWin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CopyPaste.IsDrag && CopyPaste.DragStatus is not CopyPasteService.DragState.Active && e.Key is Key.Escape)
            CopyPaste.DragBitmap = null;
        else
            OnButtonKeyDown(sender, e);
    }

    public void HandlePreviewKeyDown(KeyEventArgs e) => MainWin_PreviewKeyDown(this, e);

    public void HandlePreviewKeyUp(KeyEventArgs e) => MainWindow_OnPreviewKeyUp(this, e);

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.System && CopyPaste.IsDrag)
            e.Handled = true;
    }

    private void MainWindow_OnPreviewQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
            CopyPaste.DragBitmap = null;
    }

    private void IconView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        CopyPaste.WasDragging = false;

        // Walk up from the original source to determine if the click is on an item or empty space
        var source = e.OriginalSource as DependencyObject;
        var hitItem = source is not null
            ? ItemsControl.ContainerFromElement(IconView, source) as ListViewItem
            : null;

        TrackExplorerMouseDown(e, source);

        int selectionIndex = IconView.SelectedIndex;

        if (hitItem is not null)
        {
            if (Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                // Exclusive select on left/right click of an unselected item. ListView UnselectAll
                // cannot clear IsSelected on recycled (off-screen) containers via TwoWay binding,
                // which otherwise leaves a second item selected after a single click.
                if (!hitItem.IsSelected)
                {
                    SelectOnlyItem(hitItem.DataContext);
                    if (e.ChangedButton is MouseButton.Right)
                        e.Handled = true;
                    selectionIndex = IconView.SelectedIndex;
                }
            }
        }
        else
        {
            // Ignore clicks on scrollbars — do not keep MouseDownPoint or marquee starts
            // when the captured thumb's MouseMove bubbles over the viewport.
            if (HitTestHelper.IsInScrollBar(source))
            {
                MouseDownPoint = NullPoint;
                return;
            }

            if (IconView.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if ((e.ChangedButton is MouseButton.Right || !SuppressExplorerUnselect)
                && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ClearDataItemSelectionFlags();
                IconView.UnselectAll();
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    private void IconView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (!SelectionRect.IsActive && !SelectionRect.SelectionOccurred)
            return;

        SelectionRect.Collapse();
        e.Handled = true;
    }

    private void IconView_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(SelectionRect);

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || SuppressExplorerMarquee
            || (!SelectionRect.IsActive && HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject));

        DependencyObject dragSource = IconView;
        if (IconView.SelectedItems.Count > 0)
            dragSource = IconView.ItemContainerGenerator.ContainerFromItem(IconView.SelectedItems[0]) as DependencyObject ?? IconView;

        TryBeginExplorerDragOrMarquee(point, abortDrag, IconView.ScrollViewer, dragSource);
    }

    private void OnThumbsSizeChanged()
    {
        var size = RuntimeSettings.ThumbsSize;
        if (size != ThumbnailService.ThumbnailSize.Disabled)
            InvalidateFileIcons();

        if (ActiveSelectedItems.Count > 0)
            ScheduleKeepFirstSelectedInView();
        else
        {
            App.SafeBeginInvoke(() => ActiveScrollViewer?.ScrollToTop(), DispatcherPriority.Loaded);
        }
    }

    private void IconView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || !ViewModel.IsIconView || ActiveSelectedItems.Count == 0)
            return;

        ScheduleKeepFirstSelectedInView();
    }

    private void DriveList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged
            || !FileActions.IsDriveViewVisible
            || DriveList.SelectedItem is null)
            return;

        var generation = ++_keepSelectionInViewGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _keepSelectionInViewGeneration)
                return;
            if (DriveList.SelectedItem is not null)
                DriveList.ScrollIntoView(DriveList.SelectedItem);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// After a wrap-panel width change, ensure the first selected explorer item is still visible.
    /// </summary>
    private void ScheduleKeepFirstSelectedInView()
    {
        var generation = ++_keepSelectionInViewGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _keepSelectionInViewGeneration)
                return;
            if (ActiveSelectedItems.Count > 0)
                ActiveScrollIntoView(ActiveSelectedItems[0]);
        }, DispatcherPriority.Loaded);
    }

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);

    private void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RuntimeSettings.SelectedDrive = DriveList.SelectedItem as DriveViewModel;
        DetailsPane.SelectedFiles = RuntimeSettings.SelectedDrive is DriveViewModel selectedDrive ? [selectedDrive] : [];
        FileActionLogic.UpdateFileActions();
    }

    private void DriveList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var hitItem = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(DriveList, source)
            : null;

        if (hitItem is not null)
            return;

        foreach (var item in DriveList.Items)
        {
            (item as DriveViewModel)?.IsSelected = false;
        }

        DriveList.SelectedIndex = -1;
        RuntimeSettings.SelectedDrive = null;
        FileActionLogic.UpdateFileActions();
    }

    private void ExplorerHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DetailsPane.MaxWidth = e.NewSize.Width - 100;
    }
}
