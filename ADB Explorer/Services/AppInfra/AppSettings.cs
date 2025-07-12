using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class AppSettings : ViewModelBase
{
    public enum SystemVals
    {
        windowMaximized,
        detailedVisible,
        detailedHeight,
    }

    public enum AppTheme
    {
        windowsDefault,
        light,
        dark,
    }

    public enum DoubleClickAction
    {
        pull,
        edit,
        none,
    }

    #region paths

    private string defaultFolder;
    /// <summary>
    /// Default Folder For Pull On Double-Click And Folder / File Browsers
    /// </summary>
    public string DefaultFolder
    {
        get => Get(ref defaultFolder, "");
        set
        {
            if (Set(ref defaultFolder, value))
                OnPropertyChanged(nameof(IsPullOnDoubleClickEnabled));
        }
    }

    public bool IsPullOnDoubleClickEnabled => !string.IsNullOrEmpty(DefaultFolder);

    private string manualAdbPath;
    public string ManualAdbPath
    {
        get => Get(ref manualAdbPath, "");
        set => Set(ref manualAdbPath, value);
    }

    #endregion

    #region File Behavior

    private bool showExtensions;
    public bool ShowExtensions
    {
        get => Get(ref showExtensions, true);
        set => Set(ref showExtensions, value);
    }

    private bool showSystemPackages;
    public bool ShowSystemPackages
    {
        get => Get(ref showSystemPackages, true);
        set => Set(ref showSystemPackages, value);
    }

    private bool showHiddenItems;
    public bool ShowHiddenItems
    {
        get => Get(ref showHiddenItems, true);
        set => Set(ref showHiddenItems, value);
    }

    private bool advancedDrag;
    public bool AdvancedDrag
    {
        get => Get(ref advancedDrag, true);
        set
        {
            if (Set(ref advancedDrag, value))
            {
                if (!value)
                    Data.RuntimeSettings.IsAdvancedDragEnabled = false;
                else
                    ExplorerHelper.CheckConflictingApps();
            }
        }
    }

    #endregion

    #region Double Click

    private DoubleClickAction doubleClick;
    public DoubleClickAction DoubleClick
    {
        get => Get(ref doubleClick, DoubleClickAction.pull);
        set => Set(ref doubleClick, value);
    }

    #endregion

    #region device

    private bool saveDevices;
    public bool SaveDevices
    {
        get => Get(ref saveDevices, true);
        set => Set(ref saveDevices, value);
    }

    private bool autoOpen;
    /// <summary>
    /// Automatically Open Device For Browsing
    /// </summary>
    public bool AutoOpen
    {
        get => Get(ref autoOpen, false);
        set => Set(ref autoOpen, value);
    }

    private bool autoRoot;
    /// <summary>
    /// Automatically Try To Open Devices Using Root Privileges
    /// </summary>
    public bool AutoRoot
    {
        get => Get(ref autoRoot, false);
        set => Set(ref autoRoot, value);
    }

    #endregion

    #region Drives & Features

    private bool pollDrives;
    public bool PollDrives
    {
        get => Get(ref pollDrives, true);
        set => Set(ref pollDrives, value);
    }

    private bool enableRecycle;
    /// <summary>
    /// Enable moving deleted items to a special folder, instead of permanently deleting them
    /// </summary>
    public bool EnableRecycle
    {
        get => Get(ref enableRecycle, false);
        set => Set(ref enableRecycle, value);
    }

    private bool enableApk;
    public bool EnableApk
    {
        get => Get(ref enableApk, false);
        set => Set(ref enableApk, value);
    }

    #endregion

    #region adb

    private bool enableMdns;
    /// <summary>
    /// Runs ADB server with mDNS enabled, polling for services is enabled in the connection expander
    /// </summary>
    public bool EnableMdns
    {
        get => Get(ref enableMdns, false);
        set => Set(ref enableMdns, value);
    }

    private bool pollDevices;
    /// <summary>
    /// <see langword="false"/> - disables polling for both ADB devices and mDNS services. Enables manual refresh button
    /// </summary>
    public bool PollDevices
    {
        get => Get(ref pollDevices, true);
        set => Set(ref pollDevices, value);
    }

    private bool pollBattery;
    /// <summary>
    /// Enables battery information for all devices (polling and displaying)
    /// </summary>
    public bool PollBattery
    {
        get => Get(ref pollBattery, true);
        set => Set(ref pollBattery, value);
    }

    private bool enableLog;
    /// <summary>
    /// <see langword="false"/> - disables logging of commands and command log button
    /// </summary>
    public bool EnableLog
    {
        get => Get(ref enableLog, false);
        set => Set(ref enableLog, value);
    }

    #endregion

    #region File Ops

    private bool enableCompactView;
    public bool EnableCompactView
    {
        get => Get(ref enableCompactView, false);
        set => Set(ref enableCompactView, value);
    }

    private bool stopPollingWhileSync;
    public bool StopPollingOnSync
    {
        get => Get(ref stopPollingWhileSync, false);
        set
        {
            if (Set(ref stopPollingWhileSync, value))
            {
                Data.RuntimeSettings.IsPollingStopped = value
                    && Data.FileOpQ.Operations.Any(op => op is FileSyncOperation && op.Status is FileOperation.OperationStatus.InProgress);
            }
        }
    }

    private bool allowMultiOp;
    public bool AllowMultiOp
    {
        get => Get(ref allowMultiOp, true);
        set => Set(ref allowMultiOp, value);
    }

    private bool rescanOnPush;
    public bool RescanOnPush
    {
        get => Get(ref rescanOnPush, true);
        set => Set(ref rescanOnPush, value);
    }

    #endregion

    #region about

    private bool checkForUpdates;
    /// <summary>
    /// GET releases on GitHub repo on each launch
    /// </summary>
    public bool CheckForUpdates
    {
        get => Get(ref checkForUpdates, true);
        set => Set(ref checkForUpdates, value);
    }

    #endregion

    #region graphics

    private bool forceFluentStyles;
    /// <summary>
    /// Use Fluent [Windows 11] Styles In Windows 10
    /// </summary>
    public bool ForceFluentStyles
    {
        get => Get(ref forceFluentStyles, false);
        set => Set(ref forceFluentStyles, value);
    }

    private bool swRender;
    /// <summary>
    /// Disables HW acceleration
    /// </summary>
    public bool SwRender
    {
        get => Get(ref swRender, false);
        set => Set(ref swRender, value);
    }

    private bool isAnimated = true;
    /// <summary>
    /// This setting is not updated at runtime as long as we are unable to combine it with different enter and exit storyboards. <br />
    /// The issue is the inability to separate the change of this setting from the change of the original triggers.
    /// </summary>
    public bool IsAnimated
    {
        get => isAnimated;
        set
        {
            isAnimated = value;
            OnPropertyChanged(nameof(isAnimated));
        }
    }

    private bool disableAnimation;
    /// <summary>
    /// Disables all visual animations
    /// </summary>
    public bool DisableAnimation
    {
        get
        {
            var value = Get(ref disableAnimation, false);

            if (!Data.RuntimeSettings.IsWindowLoaded)
                IsAnimated = !disableAnimation;

            return value;
        }
        set => Set(ref disableAnimation, value);
    }

    private bool enableSplash;
    public bool EnableSplash
    {
        get => Get(ref enableSplash, true);
        set => Set(ref enableSplash, value);
    }

    #endregion

    private AppTheme appTheme;
    public AppTheme Theme
    {
        get => Get(ref appTheme, AppTheme.windowsDefault);
        set => Set(ref appTheme, value);
    }

    private bool progressMethod;
    public bool UseProgressRedirection
    {
        get => Get(ref progressMethod, false);
        set => Set(ref progressMethod, value);
    }

    private bool hidePasteNamingInfo;
    public bool HidePasteNamingInfo
    {
        get => Get(ref hidePasteNamingInfo, false);
        set => Set(ref hidePasteNamingInfo, value);
    }

    private bool showWelcomeScreen;
    public bool ShowWelcomeScreen
    {
        get => Get(ref showWelcomeScreen, true);
        set => Set(ref showWelcomeScreen, value);
    }

    private bool advancedDragSet;
    public bool AdvancedDragSet
    {
        get => Get(ref advancedDragSet, false);
        set => Set(ref advancedDragSet, value);
    }

    private string lastVersion;
    public string LastVersion
    {
        get => Get(ref lastVersion, "0.0.0");
        set => Set(ref lastVersion, value);
    }

    private bool showLaunchWsaMessage;
    public bool ShowLaunchWsaMessage
    {
        get => Get(ref showLaunchWsaMessage, true);
        set => Set(ref showLaunchWsaMessage, value);
    }

    private ulong editorMaxFileSize;
    public ulong EditorMaxFileSize
    {
        get
        {
            if (editorMaxFileSize == 0)
                Set<ulong>(ref editorMaxFileSize, 300000);

            return Get<ulong>(ref editorMaxFileSize, 300000);
        }
    }

    private bool? unrootOnDisconnect;
    public bool? UnrootOnDisconnect
    {
        get => Get(ref unrootOnDisconnect, null);
        set => Set(ref unrootOnDisconnect, value);
    }

    private string lastDevice = null;
    public string LastDevice
    {
        get => Get(ref lastDevice, null);
        set => Set(ref lastDevice, value);
    }

    private CultureInfo uiCulture;
    public CultureInfo UICulture
    {
        get
        {
            if (uiCulture is null)
            {
                var uiLanguage = UILanguage;
                uiCulture = string.IsNullOrEmpty(uiLanguage)
                    ? CultureInfo.InvariantCulture
                    : new(uiLanguage);

                UpdateTranslation();
            }

            return uiCulture;
        }
        set
        {
            if (base.Set(ref uiCulture, value))
            {
                UILanguage = value.Name;
                UpdateTranslation();
            }
        }
    }

    private void UpdateTranslation()
    {
        var actual = UICulture.Equals(CultureInfo.InvariantCulture)
            ? OriginalCulture
            : UICulture;

        string precent = actual.Name == "en" || actual.Parent.Name == "en"
                         ? null
                         : $"{SettingsHelper.GetCurrentPercentageTranslated(actual) * 100:f0}%";

        CultureTranslationProgress.Value = precent;
    }

    public ObservableProperty<string> CultureTranslationProgress { get; private set; } = new() { Value = null };

    private string uiLanguage;
    public string UILanguage
    {
        get => Get(ref uiLanguage, "");
        set => Set(ref uiLanguage, value);
    }

    private CultureInfo originalCulture = null;
    public CultureInfo OriginalCulture
    {
        get
        {
            if (originalCulture is null)
            {
                try
                {
                    originalCulture = Thread.CurrentThread.CurrentUICulture;
                }
                catch
                { }
            }
            return originalCulture;
        }
    }

    private bool showLanguageNotification;
    public bool ShowLanguageNotification
    {
        get => Get(ref showLanguageNotification, true);
        set => Set(ref showLanguageNotification, value);
    }

    private bool homeLocationsExpanded;
    public bool HomeLocationsExpanded
    {
        get => Get(ref homeLocationsExpanded, false);
        set => Set(ref homeLocationsExpanded, value);
    }

    protected override bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        Storage.StoreValue(propertyName, value);

        OnPropertyChanged(propertyName);

        return true;
    }

    private static T Get<T>(ref T storage, T defaultValue, [CallerMemberName] string propertyName = null)
    {
        if (storage is null || storage.Equals(default(T)))
        {
            var value = storage switch
            {
                Version => new Version((string)Storage.RetrieveValue(propertyName)),
                Enum => Storage.RetrieveEnum<T>(propertyName),
                bool => Storage.RetrieveBool(propertyName),
                null when typeof(T) == typeof(string) => Storage.RetrieveValue(propertyName),
                null when typeof(T) == typeof(bool?) => Storage.RetrieveBool(propertyName),
                null when typeof(T) == typeof(Version) => new Version((string)Storage.RetrieveValue(propertyName)),
                _ => Storage.RetrieveValue(propertyName),
            };

            storage = (T)(value ?? defaultValue);
        }

        return storage;
    }
}
