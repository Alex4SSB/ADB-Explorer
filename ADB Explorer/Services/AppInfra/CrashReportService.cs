using ADB_Explorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ADB_Explorer.Services;

/// <summary>
/// Sends unhandled exception reports via the Faro collector API.
/// Debug builds use local Grafana Alloy. Release builds use an embedded <c>FaroCollector.url</c> when present.
/// Deploy builds only report when <see cref="AppRuntimeSettings.IsAppPackaged"/> is true (Store install).
/// </summary>
public static class CrashReportService
{
    private const string LocalAlloyCollectorUrl = "http://127.0.0.1:12347/collect";

    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static readonly Lazy<Uri?> CollectorUrl = new(ResolveCollectorUrl);

    public static bool IsConfigured => CollectorUrl.Value is not null;

    /// <summary>True when crash reports are sent to local Grafana Alloy.</summary>
    public static bool UsesLocalCollector =>
        CollectorUrl.Value is { IsLoopback: true };

    public static string LocalCrashLogPath =>
        Path.Combine(Data.AppDataPath, AdbExplorerConst.LAST_CRASH_FILE);

    public readonly record struct SendResult(bool Success, int? StatusCode, string? Error);

    public static void WriteLocalCrashLog(Exception exception)
    {
        try
        {
            if (string.IsNullOrEmpty(Data.AppDataPath))
                return;

            Directory.CreateDirectory(Data.AppDataPath);
            File.WriteAllText(LocalCrashLogPath, exception.ToString());
        }
        catch
        { }
    }

    public static async Task<SendResult> SendAsync(Exception exception)
    {
        if (!IsConfigured)
            return new(false, null, "Crash reporting is not configured");

        var reportable = GetReportableException(exception);
        var collectorUrl = CollectorUrl.Value!;
        var ping = await Network.PingCollectorAsync(collectorUrl).ConfigureAwait(false);
        if (!ping.IsReachable)
            return new(false, ping.StatusCode, ping.Error);

        var json = BuildPayloadJson(exception, reportable);
        var headers = new Dictionary<string, string> { ["X-Faro-Session-Id"] = SessionId };
        Network.PostJsonFireAndForget(collectorUrl, json, headers);
        return new(true, ping.StatusCode, null);
    }

    /// <summary>
    /// Returns the exception that should be reported to telemetry. Unwraps reflection/dispatch
    /// wrappers that hide the real failure (e.g. <see cref="TargetInvocationException"/>).
    /// </summary>
    internal static Exception GetReportableException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var current = exception;
        while (ShouldUnwrap(current) && current.InnerException is not null)
            current = current.InnerException;

        return current;
    }

    private static bool ShouldUnwrap(Exception exception) =>
        exception is TargetInvocationException
        or AggregateException { InnerExceptions.Count: 1 };

    private static Uri? ResolveCollectorUrl()
    {
        foreach (var candidate in EnumerateCollectorUrlCandidates())
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                continue;

            if (uri.Scheme is not ("http" or "https"))
                continue;

            return uri;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCollectorUrlCandidates()
    {
#if DEBUG
        var devOverride = ReadAdjacentCollectorUrl();
        if (!string.IsNullOrWhiteSpace(devOverride))
            yield return devOverride;

        yield return LocalAlloyCollectorUrl;
#else
#if DEPLOY
        if (!Data.RuntimeSettings.IsAppPackaged)
            yield break;
#endif
        var embedded = ReadEmbeddedCollectorUrl();
        if (!string.IsNullOrWhiteSpace(embedded))
            yield return embedded;
#endif
    }

    private static string? ReadAdjacentCollectorUrl()
    {
#if DEBUG
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "FaroCollector.url");
            if (!File.Exists(path))
                return null;

            return NormalizeCollectorUrl(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }

    private const string EmbeddedCollectorResourceName = "FaroCollector.url";

    private static string? ReadEmbeddedCollectorUrl()
    {
        var assembly = typeof(CrashReportService).Assembly;
        var stream = assembly.GetManifestResourceStream(EmbeddedCollectorResourceName);
        if (stream is null)
        {
            var fallbackName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(EmbeddedCollectorResourceName, StringComparison.Ordinal));
            if (fallbackName is null)
                return null;

            stream = assembly.GetManifestResourceStream(fallbackName);
            if (stream is null)
                return null;
        }

        using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return NormalizeCollectorUrl(reader.ReadToEnd());
        }
    }

    private static string? NormalizeCollectorUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var url = raw.Trim();
        if (url.Length > 0 && url[0] == '\uFEFF')
            url = url[1..].Trim();

        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    private static string BuildPayloadJson(Exception original, Exception reportable)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var viewName = GetCurrentViewName(reportable);
        var pageId = GetPageId(viewName);

        var exceptionObject = new JObject
        {
            ["type"] = reportable.GetType().FullName ?? reportable.GetType().Name,
            ["value"] = reportable.Message,
            ["timestamp"] = timestamp,
        };

        var stacktrace = BuildStacktraceJObject(reportable);
        if (stacktrace is not null)
            exceptionObject["stacktrace"] = stacktrace;

        var context = BuildContextJObject(original, reportable);
        if (context.HasValues)
            exceptionObject["context"] = context;

        var payload = new JObject
        {
            ["meta"] = new JObject
            {
                ["sdk"] = new JObject
                {
                    ["name"] = "adb-explorer-desktop",
                    ["version"] = Data.AppVersion.ToString(),
                },
                ["app"] = new JObject
                {
                    ["name"] = Properties.AppGlobal.AppDisplayName,
                    ["version"] = Properties.AppGlobal.AppVersion,
                    ["environment"] = Data.RuntimeSettings.IsAppPackaged ? "store" : "portable",
                },
                ["session"] = new JObject { ["id"] = SessionId },
                ["page"] = new JObject
                {
                    ["id"] = pageId,
                    ["url"] = $"https://adb-explorer.local{pageId}",
                },
                ["browser"] = new JObject
                {
                    ["name"] = "Windows",
                    ["version"] = Environment.OSVersion.Version.ToString(),
                    ["os"] = RuntimeInformation.OSDescription,
                    ["userAgent"] = $"ADB-Explorer/{Properties.AppGlobal.AppVersion} ({RuntimeInformation.OSArchitecture})",
                },
            },
            ["events"] = new JArray(new JObject
            {
                ["name"] = "session_start",
                ["domain"] = "session",
                ["timestamp"] = timestamp,
            }),
            ["exceptions"] = new JArray(exceptionObject),
        };

        return payload.ToString(Formatting.None);
    }

    private static string GetCurrentViewName(Exception exception)
    {
        var page = Data.CurrentPage.Value;
        if (page is not null)
            return PageTypeNameToViewName(page.Name);

        foreach (var frame in new StackTrace(exception, true).GetFrames() ?? [])
        {
            var declaringType = frame.GetMethod()?.DeclaringType?.FullName;
            if (declaringType is null)
                continue;

            var viewName = ViewNameFromDeclaringType(declaringType);
            if (viewName is not null)
                return viewName;
        }

        return "Unknown";
    }

    private static string GetPageId(string viewName) =>
        $"/{viewName.ToLowerInvariant()}";

    private static string PageTypeNameToViewName(string pageTypeName) => pageTypeName.Replace("Page", "");

    private static string? ViewNameFromDeclaringType(string declaringTypeFullName)
    {
        if (declaringTypeFullName.Contains("ADB_Explorer.Views.Pages."))
            return declaringTypeFullName.Replace("ADB_Explorer.Views.Pages.", "").Replace("Page", "");

        return null;
    }

    private static JObject? BuildStacktraceJObject(Exception exception)
    {
        var frames = new StackTrace(exception, true).GetFrames();
        if (frames is null || frames.Length == 0)
            return null;

        var frameObjects = new JArray();
        foreach (var frame in frames)
        {
            var frameObject = new JObject
            {
                ["function"] = frame.GetMethod()?.Name ?? "",
                ["module"] = frame.GetMethod()?.DeclaringType?.FullName ?? "",
            };

            var filename = frame.GetFileName();
            if (!string.IsNullOrWhiteSpace(filename))
                frameObject["filename"] = filename;

            var line = frame.GetFileLineNumber();
            if (line > 0)
                frameObject["lineno"] = line;

            var column = frame.GetFileColumnNumber();
            if (column > 0)
                frameObject["colno"] = column;

            frameObjects.Add(frameObject);
        }

        return new JObject { ["frames"] = frameObjects };
    }

    private static JObject BuildContextJObject(Exception original, Exception reportable)
    {
        var context = new JObject
        {
            ["dotnetVersion"] = Environment.Version.ToString(),
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
        };

        if (!ReferenceEquals(original, reportable))
        {
            context["wrapperException"] = new JObject
            {
                ["type"] = original.GetType().FullName ?? original.GetType().Name,
                ["value"] = original.Message,
            };
        }

        if (reportable.InnerException is not null)
            context["innerException"] = FormatExceptionChain(reportable.InnerException);

        context["fullException"] = original.ToString();

        return context;
    }

    private static string FormatExceptionChain(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;

        while (current is not null)
        {
            builder.Append(current.GetType().FullName);
            builder.Append(": ");
            builder.AppendLine(current.Message);
            current = current.InnerException;
        }

        return builder.ToString().TrimEnd();
    }
}
