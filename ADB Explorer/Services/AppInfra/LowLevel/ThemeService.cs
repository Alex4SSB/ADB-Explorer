using ADB_Explorer.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Services;

internal class AdbThemeService
{
    public static SystemTheme CurrentTheme { get; private set; } = SystemTheme.Unknown;

    private static Window? _window;
    private static bool _watchingSystemTheme;
    private static bool _updatingTheme;

    static AdbThemeService()
    {
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
    }

    /// <summary>
    /// Dark Fluent chrome, or a high-contrast theme whose window color is dark
    /// (Aquatic, Dusk, Night sky). Desert is treated as light.
    /// </summary>
    public static bool IsDarkChrome()
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        if (theme == ApplicationTheme.Dark)
            return true;

        if (theme != ApplicationTheme.HighContrast)
            return false;

        if (CurrentTheme is SystemTheme.HCWhite)
            return false;

        if (CurrentTheme is SystemTheme.HC1 or SystemTheme.HC2 or SystemTheme.HCBlack)
            return true;

        if (Application.Current?.TryFindResource("ApplicationBackgroundColor") is Color background)
            return IsDarkColor(background);

        return false;
    }

    private static bool IsDarkColor(Color color)
    {
        var luma = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
        return luma < 128;
    }

    public static void SetTheme(AppSettings.AppTheme theme, Window? window = null)
    {
        if (window is not null)
            _window = window;

        if (_updatingTheme)
        {
            return;
        }

        _updatingTheme = true;
        try
        {
            SyncSystemThemeWatcher(theme);
            ApplyWpfUiTheme(theme);

            ApplyAppThemeDictionary();

            ApplyHighContrastResourcePatch();

            RefreshWindowChrome();

        }
        finally
        {
            _updatingTheme = false;
        }
    }

    public static void SetAccent(Color? color)
    {
        if (ApplicationThemeManager.GetAppTheme() == ApplicationTheme.HighContrast)
            return;

        var accentColor = color ?? ApplicationAccentColorManager.GetColorizationColor();
        ApplicationAccentColorManager.Apply(accentColor, ApplicationThemeManager.GetAppTheme());

        // Force-reload the WPF UI theme dictionary so that its SolidColorBrush
        // objects (e.g. AccentButtonBackground) are recreated and resolve their
        // {DynamicResource AccentFillColorDefault} bindings from the just-updated
        // Application.Resources.  Without this, already-rendered controls keep a
        // stale reference to the old brush instance.
        ApplicationThemeManager.Apply(ApplicationThemeManager.GetAppTheme(), updateAccent: false);
    }

    /// <summary>
    /// <see cref="ApplicationThemeManager.Changed"/> fires synchronously from
    /// <see cref="SystemThemeWatcher"/>'s window-message hook, which itself runs inside the
    /// WndProc call for WM_SYSCOLORCHANGE / WM_THEMECHANGED (sent, not posted, by the shell).
    /// Doing our resource-dictionary swap and full window-chrome refresh here blocks that
    /// message pump long enough for Windows to consider the window unresponsive (it goes
    /// black and can't be dragged). Defer the actual work to a dispatcher tick so the pump
    /// returns immediately.
    /// </summary>
    private static void OnApplicationThemeChanged(ApplicationTheme theme, Color _)
    {
        if (_updatingTheme)
            return;

        App.SafeBeginInvoke(() => HandleSystemThemeChanged(theme));
    }

    private static void HandleSystemThemeChanged(ApplicationTheme theme)
    {
        var sw = Stopwatch.StartNew();

        if (_updatingTheme)
        {
            return;
        }

        try
        {
            var setting = Data.Settings.Theme;
            if (setting == AppSettings.AppTheme.Light)
            {
                if (theme != ApplicationTheme.Light)
                    SetTheme(setting);
                return;
            }

            if (setting == AppSettings.AppTheme.Dark)
            {
                if (theme != ApplicationTheme.Dark)
                    SetTheme(setting);
                return;
            }

            CurrentTheme = ApplicationThemeManager.GetSystemTheme();

            ApplyAppThemeDictionary();

            ApplyHighContrastResourcePatch();

            RefreshWindowChrome();
        }
        catch { }
    }

    private static void ApplyWpfUiTheme(AppSettings.AppTheme theme)
    {
        SystemTheme actualTheme = SystemTheme.Unknown;

        switch (theme)
        {
            case AppSettings.AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                actualTheme = SystemTheme.Light;
                break;

            case AppSettings.AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                actualTheme = SystemTheme.Dark;
                break;

            case AppSettings.AppTheme.WindowsDefault:
                ApplicationThemeManager.ApplySystemTheme();
                actualTheme = ApplicationThemeManager.GetSystemTheme();
                break;
        }

        CurrentTheme = actualTheme;
    }

    private static void ApplyAppThemeDictionary()
    {
        if (Application.Current is null)
            return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var source = $"/Themes/{AppThemeDictionaryName()}.xaml";

        var currentTheme = dictionaries.FirstOrDefault(d =>
            d.Source != null &&
            d.Source.OriginalString.StartsWith("/Themes/", StringComparison.OrdinalIgnoreCase));

        if (currentTheme?.Source?.OriginalString.Equals(source, StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        if (currentTheme != null)
            dictionaries.Remove(currentTheme);

        // Append so app overrides (e.g. MenuBarBackground) win over WPF UI's HC placeholders.
        dictionaries.Add(new ResourceDictionary
        {
            Source = new(source, UriKind.Relative)
        });
    }

    private static string AppThemeDictionaryName()
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        if (theme == ApplicationTheme.Dark)
            return "Dark";
        if (theme == ApplicationTheme.HighContrast)
            return "HighContrast";
        return "Light";
    }

    private static void SyncSystemThemeWatcher(AppSettings.AppTheme theme)
    {
        if (theme == AppSettings.AppTheme.WindowsDefault)
            StartWatching();
        else
            StopWatching();
    }

    private static void StartWatching()
    {
        if (_watchingSystemTheme || _window is null)
            return;

        SystemThemeWatcher.Watch(_window);
        _watchingSystemTheme = true;
    }

    private static void StopWatching()
    {
        if (!_watchingSystemTheme)
            return;

        _watchingSystemTheme = false;

        if (_window is { IsLoaded: true })
        {
            SystemThemeWatcher.UnWatch(_window);
        }
    }

    private static void ApplyHighContrastResourcePatch()
    {
        var isHighContrast = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.HighContrast;

        // Live-bound so XAML triggers (e.g. the accent-icon hover flip, which should only kick in
        // under HC) can react to a theme switch without an app restart.
        Data.RuntimeSettings.IsHighContrast = isHighContrast;

        if (isHighContrast)
            PatchHighContrastPlaceholderColors();
        else
            ClearHighContrastPlaceholderColors();
    }

    private static bool _highContrastPlaceholdersPatched;
    private static bool _refreshingChrome;

    /// <summary>
    /// Title bar and LeftFluent navigation are transparent over the window. WPF UI writes a
    /// local <see cref="Window.Background"/> (and OS <c>SystemColors.WindowColor</c>) when
    /// removing Mica, so those chrome surfaces stay on the previous theme until rebound.
    /// </summary>
    private static void RefreshWindowChrome()
    {
        if (_refreshingChrome)
        {
            return;
        }

        _refreshingChrome = true;
        try
        {
            if (_window is not null)
                RefreshWindowChrome(_window);

            if (Application.Current is null)
                return;

            foreach (Window window in Application.Current.Windows)
            {
                if (window == _window)
                    continue;

                RefreshWindowChrome(window);
            }
        }
        finally
        {
            _refreshingChrome = false;
        }
    }

    private static void RefreshWindowChrome(Window window)
    {
        if (!window.IsLoaded)
        {
            window.Loaded -= OnWindowLoadedRefreshChrome;
            window.Loaded += OnWindowLoadedRefreshChrome;
            return;
        }

        var appTheme = ApplicationThemeManager.GetAppTheme();
        var isHighContrast = appTheme == ApplicationTheme.HighContrast;

        if (isHighContrast)
        {
            ApplyHighContrastWindowBackground(window);
            ApplyHighContrastWindowBorder(window);
            EnsureHighContrastBorderHooks(window);
        }
        else
        {
            RemoveHighContrastBorderHooks(window);

            window.SetCurrentValue(Control.BackgroundProperty, Brushes.Transparent);

            if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } clearTarget })
                clearTarget.BackgroundColor = Colors.Transparent;

            // Leave HC ActiveCaption border; FluentWindow re-applies SystemAccent on next Activated.
            if (window.IsActive)
                WindowCompositionRefresh.ResetBorderColor(window);
        }

        RefreshTitleBar(window, appTheme, isHighContrast);
    }

    private static void OnWindowLoadedRefreshChrome(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        window.Loaded -= OnWindowLoadedRefreshChrome;
        _ = window.Dispatcher.BeginInvoke(() => RefreshWindowChrome(window));
    }

    private static void ApplyHighContrastWindowBackground(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");

        if (PresentationSource.FromVisual(window) is not HwndSource { CompositionTarget: { } target })
            return;

        if (window.TryFindResource("ApplicationBackgroundColor") is Color color)
            target.BackgroundColor = color;
    }

    /// <summary>
    /// In high contrast, DWM replaces the soft window shadow with a solid border
    /// (<c>DWMWA_BORDER_COLOR</c>), and the title bar paints its own background/foreground from
    /// TitleBarBackgroundBrush/TitleBarForegroundBrush. Use <see cref="SystemColors.ActiveCaptionColor"/>
    /// — the same window-chrome color as the title bar (e.g. yellow in Aquatic, blue-grey in
    /// Desert) — not Hotlight (hyperlinks) or Highlight (selection); switch to the Inactive*
    /// caption colors while the window isn't active, same as Windows does for its own chrome.
    /// </summary>
    private static void ApplyHighContrastWindowBorder(Window window)
    {
        var isActive = window.IsActive;

        var captionColor = isActive ? SystemColors.ActiveCaptionColor : SystemColors.InactiveCaptionColor;
        var captionTextColor = isActive ? SystemColors.ActiveCaptionTextColor : SystemColors.InactiveCaptionTextColor;

        WindowCompositionRefresh.ApplyBorderColor(window, captionColor);

        if (Application.Current is not null)
        {
            // Also routed through SetBrush (not a direct resources[...] = ... assignment) so these two
            // keys - written here, not in PatchHighContrastPlaceholderColors - still get stripped by
            // ClearHighContrastPlaceholderColors when HC turns off; see _patchedResourceKeys.
            SetBrush(Application.Current.Resources, "TitleBarBackgroundBrush", new SolidColorBrush(captionColor));
            SetBrush(Application.Current.Resources, "TitleBarForegroundBrush", new SolidColorBrush(captionTextColor));
        }
    }

    private static void EnsureHighContrastBorderHooks(Window window)
    {
        // FluentWindow reapplies SystemAccent on Activated and COLOR_DEFAULT on Deactivated;
        // reassert ActiveCaption chrome after those handlers run.
        window.Activated -= OnWindowActivationChangedForHcBorder;
        window.Activated += OnWindowActivationChangedForHcBorder;
        window.Deactivated -= OnWindowActivationChangedForHcBorder;
        window.Deactivated += OnWindowActivationChangedForHcBorder;
    }

    private static void RemoveHighContrastBorderHooks(Window window)
    {
        window.Activated -= OnWindowActivationChangedForHcBorder;
        window.Deactivated -= OnWindowActivationChangedForHcBorder;
    }

    private static void OnWindowActivationChangedForHcBorder(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        if (ApplicationThemeManager.GetAppTheme() != ApplicationTheme.HighContrast)
            return;

        _ = window.Dispatcher.BeginInvoke(
            () => ApplyHighContrastWindowBorder(window),
            DispatcherPriority.Loaded);
    }

    private static void RefreshTitleBar(Window window, ApplicationTheme appTheme, bool isHighContrast)
    {
        var titleBar = FindTitleBar(window);
        if (titleBar is null)
            return;

        // WPF UI's TitleBar only restyles caption buttons for ApplicationTheme.Dark.
        // Dark contrast themes (Aquatic / Dusk / Night sky) need the same treatment.
        ApplicationTheme titleBarTheme;
        if (isHighContrast && IsDarkChrome())
            titleBarTheme = ApplicationTheme.Dark;
        else
            titleBarTheme = appTheme;

        titleBar.SetCurrentValue(TitleBar.ApplicationThemeProperty, titleBarTheme);
    }

    private static TitleBar? FindTitleBar(Window window)
    {
        if (window.FindName("TitleBar") is TitleBar named)
            return named;

        if (window.Content is TitleBar contentBar)
            return contentBar;

        if (window.Content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is TitleBar bar)
                    return bar;
            }
        }

        return null;
    }

    /// <summary>
    /// Every app-level resource key any HC patch step has set - via <see cref="SetColor"/>/
    /// <see cref="SetBrush"/>/<see cref="SetColors"/>, called from both
    /// <see cref="PatchHighContrastPlaceholderColors"/> and <see cref="ApplyHighContrastWindowBorder"/>
    /// - recorded as it's patched rather than hand-listed separately, so
    /// <see cref="ClearHighContrastPlaceholderColors"/> can never drift out of sync with what was
    /// actually patched (a key added to one list and not the other used to be a silent bug: either
    /// never cleaned up, or "cleaned up" without ever having been set).
    /// </summary>
    private static readonly HashSet<string> _patchedResourceKeys = [];

    /// <summary>
    /// WPF UI ships exactly four static high-contrast theme dictionaries (HC1 / HC2 / HCBlack /
    /// HCWhite), each with its SystemColorXxxColor keys hardcoded to literal hex values matching
    /// only the stock, unedited palette for that preset — it never reads
    /// <see cref="System.Windows.SystemColors"/>, so a theme the user has edited in Windows'
    /// high-contrast editor (or a fully custom one) is otherwise silently ignored. Overwrite those
    /// keys with the genuinely live OS colors first, so every DynamicResource bound to them — ours
    /// in <c>HighContrast.xaml</c> and WPF UI's own leftover placeholder keys below (defined as
    /// #FF0000 "unused" red so misuse is obvious) — tracks whatever the user actually has
    /// configured, built-in or custom.
    /// </summary>
    private static void PatchHighContrastPlaceholderColors()
    {
        if (Application.Current is null)
            return;

        var resources = Application.Current.Resources;

        var windowText = SystemColors.WindowTextColor;
        var window = SystemColors.WindowColor;
        // WPF's SystemColors renames the Win32 button-face/button-text roles (the names WPF UI's
        // own XAML keys use) to Control/ControlText.
        var buttonFace = SystemColors.ControlColor;
        var buttonText = SystemColors.ControlTextColor;
        var grayText = SystemColors.GrayTextColor;
        var highlight = SystemColors.HighlightColor;
        var highlightText = SystemColors.HighlightTextColor;
        var hotlight = SystemColors.HotTrackColor;

        SetColor(resources, "SystemColorWindowTextColor", windowText);
        SetColor(resources, "SystemColorWindowColor", window);
        SetColor(resources, "SystemColorButtonFaceColor", buttonFace);
        SetColor(resources, "SystemColorButtonTextColor", buttonText);
        SetColor(resources, "SystemColorGrayTextColor", grayText);
        SetColor(resources, "SystemColorHighlightColor", highlight);
        SetColor(resources, "SystemColorHighlightTextColor", highlightText);
        SetColor(resources, "SystemColorHotlightColor", hotlight);

        SetColors(resources, windowText,
            "TextFillColorPrimary", "TextFillColorSecondary", "TextFillColorTertiary", "TextFillColorInverse",
            "AccentTextFillColorDisabled", "TextOnAccentFillColorSelectedText", "TextOnAccentFillColorPrimary",
            "TextOnAccentFillColorSecondary", "SystemFillColorAttention", "SystemFillColorInformational",
            "SystemFillColorSuccess", "SystemFillColorCaution", "SystemFillColorCritical", "SystemFillColorNeutral",
            "SystemFillColorSolidNeutral", "ControlStrokeColorDefault", "ControlStrokeColorSecondary",
            "ControlStrokeColorTertiary", "ControlStrokeColorOnAccentDefault", "ControlStrokeColorOnAccentSecondary",
            "ControlStrokeColorOnAccentTertiary", "ControlStrokeColorOnAccentDisabled",
            "ControlStrokeColorForStrongFillWhenOnImage", "CardStrokeColorDefault", "CardStrokeColorDefaultSolid",
            "ControlStrongStrokeColorDefault", "ControlStrongStrokeColorDisabled", "SurfaceStrokeColorDefault",
            "SurfaceStrokeColorFlyout", "SurfaceStrokeColorInverse", "DividerStrokeColorDefault",
            "FocusStrokeColorOuter");

        SetColors(resources, grayText,
            "TextFillColorDisabled", "TextPlaceholderColor", "TextOnAccentFillColorDisabled");

        SetColors(resources, buttonFace,
            "ControlFillColorDefault", "ControlFillColorSecondary", "ControlFillColorTertiary",
            "ControlFillColorDisabled", "ControlFillColorInputActive", "ControlStrongFillColorDefault",
            "ControlStrongFillColorDisabled", "ControlSolidFillColorDefault", "SubtleFillColorSecondary",
            "SubtleFillColorTertiary", "SubtleFillColorDisabled", "ControlAltFillColorSecondary",
            "ControlAltFillColorTertiary", "ControlAltFillColorQuarternary", "ControlAltFillColorDisabled",
            "ControlOnImageFillColorDefault", "ControlOnImageFillColorSecondary",
            "ControlOnImageFillColorTertiary", "ControlOnImageFillColorDisabled");

        SetColors(resources, window,
            "AccentFillColorDisabled", "FocusStrokeColorInner", "CardBackgroundFillColorDefault",
            "CardBackgroundFillColorSecondary", "SmokeFillColorDefault", "LayerFillColorDefault",
            "LayerFillColorAlt", "LayerOnAcrylicFillColorDefault", "LayerOnAccentAcrylicFillColorDefault",
            "AcrylicBackgroundFillColorDefault", "LayerOnMicaBaseAltFillColorDefault",
            "LayerOnMicaBaseAltFillColorSecondary", "LayerOnMicaBaseAltFillColorTertiary",
            "LayerOnMicaBaseAltFillColorTransparent", "SolidBackgroundFillColorBase",
            "SolidBackgroundFillColorSecondary", "SolidBackgroundFillColorTertiary",
            "SolidBackgroundFillColorQuarternary", "SolidBackgroundFillColorTransparent",
            "SolidBackgroundFillColorBaseAlt", "SystemFillColorAttentionBackground",
            "SystemFillColorSuccessBackground", "SystemFillColorCautionBackground",
            "SystemFillColorCriticalBackground", "SystemFillColorNeutralBackground",
            "SystemFillColorSolidAttentionBackground", "SystemFillColorSolidNeutralBackground");

        SetColor(resources, "SubtleFillColorTransparent", Colors.Transparent);
        SetColor(resources, "ControlFillColorTransparent", Colors.Transparent);
        SetColor(resources, "ControlAltFillColorTransparent", Colors.Transparent);

        // AccentButtonForeground and its PointerOver/Pressed pairs are defined with
        // Color="{StaticResource SystemColorXxxColor}" inside WPF UI's own HC dictionaries, so
        // patching the Color keys above never reaches them — the brush already captured the stale
        // preset color when that dictionary was parsed. Replace the brush objects directly,
        // mirroring WPF UI's own Highlight/ButtonFace/Highlight pairing for these three states.
        SetBrush(resources, "AccentButtonForeground", new SolidColorBrush(highlightText));
        SetBrush(resources, "AccentButtonForegroundPointerOver", new SolidColorBrush(buttonFace));
        SetBrush(resources, "AccentButtonForegroundPressed", new SolidColorBrush(highlight));

        _highContrastPlaceholdersPatched = true;
    }

    private static void ClearHighContrastPlaceholderColors()
    {
        if (!_highContrastPlaceholdersPatched || Application.Current is null)
            return;

        var resources = Application.Current.Resources;
        foreach (var key in _patchedResourceKeys)
            resources.Remove(key);

        _patchedResourceKeys.Clear();
        _highContrastPlaceholdersPatched = false;
    }

    private static void SetColor(ResourceDictionary resources, string key, Color color)
    {
        resources[key] = color;
        _patchedResourceKeys.Add(key);
    }

    private static void SetColors(ResourceDictionary resources, Color color, params string[] keys)
    {
        foreach (var key in keys)
            SetColor(resources, key, color);
    }

    private static void SetBrush(ResourceDictionary resources, string key, Brush brush)
    {
        resources[key] = brush;
        _patchedResourceKeys.Add(key);
    }
}
