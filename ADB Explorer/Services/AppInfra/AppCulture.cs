using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

/// <summary>
/// Separates app UI language from BCL/WPF exception message language.
/// </summary>
internal static class AppCulture
{
    /// <summary>
    /// BCL and WPF build <see cref="Exception.Message"/> from the thread UI culture at throw time.
    /// Keep it English so crash reports and log grouping stay consistent.
    /// App strings use <see cref="Strings.Resources.Culture"/> instead.
    /// </summary>
    internal static readonly CultureInfo ExceptionMessages = CultureInfo.GetCultureInfo("en-US");

    internal static void ApplyThreadCultures()
    {
        var formatCulture = Data.Settings.ActualFormatCulture;

        CultureInfo.DefaultThreadCurrentUICulture = ExceptionMessages;
        CultureInfo.DefaultThreadCurrentCulture = formatCulture;

        Thread.CurrentThread.CurrentUICulture = ExceptionMessages;
        Thread.CurrentThread.CurrentCulture = formatCulture;
    }
}
