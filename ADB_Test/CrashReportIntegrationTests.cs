using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ADB_Test;

[TestClass]
[TestCategory("Integration")]
public class CrashReportIntegrationTests
{
    private static string? ResolveGrafanaCollectorUrl()
    {
        foreach (var path in EnumerateCollectorUrlPaths())
        {
            if (!File.Exists(path))
                continue;

            var url = File.ReadAllText(path).Trim();
            if (url.Length > 0 && url[0] == '\uFEFF')
                url = url[1..].Trim();

            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCollectorUrlPaths()
    {
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "FaroCollector.url"));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ADB Explorer", "FaroCollector.url"));
    }

    private static Uri RequireGrafanaCollectorUri()
    {
        var collectorUrl = ResolveGrafanaCollectorUrl();
        Assert.IsNotNull(collectorUrl, "FaroCollector.url not found. Copy FaroCollector.url.example to ADB Explorer/FaroCollector.url with your Grafana Cloud collector URL.");
        Assert.IsTrue(Uri.TryCreate(collectorUrl, UriKind.Absolute, out var uri), $"Invalid collector URL: {collectorUrl}");
        Assert.AreEqual(Uri.UriSchemeHttps, uri.Scheme, "Integration test must target Grafana Cloud over HTTPS, not local Alloy.");
        Assert.IsFalse(uri.IsLoopback, "Integration test must target Grafana Cloud, not local Alloy.");
        return uri;
    }

    private static string BuildTestPayload(string sessionId)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return $$"""
            {
              "meta": {
                "sdk": { "name": "adb-explorer-desktop", "version": "0.0.0-test" },
                "app": { "name": "ADB Explorer Test", "version": "0.0.0-test", "environment": "integration-test" },
                "session": { "id": "{{sessionId}}" },
                "page": { "id": "/test", "url": "https://adb-explorer.local/test" },
                "browser": { "name": "Windows", "version": "10.0", "os": "Windows", "userAgent": "ADB-Explorer-Test" }
              },
              "events": [
                {
                  "name": "session_start",
                  "domain": "session",
                  "timestamp": "{{timestamp}}"
                }
              ],
              "exceptions": [
                {
                  "type": "System.InvalidOperationException",
                  "value": "Integration test crash report",
                  "timestamp": "{{timestamp}}"
                }
              ]
            }
            """;
    }

    [TestMethod]
    public async Task GrafanaCollector_IsReachable()
    {
        var uri = RequireGrafanaCollectorUri();

        var result = await Network.PingCollectorAsync(uri);

        Assert.IsTrue(result.IsReachable, result.Error ?? "Expected Grafana Faro collector to respond.");
        Assert.AreEqual(400, result.StatusCode, "Expected Grafana Faro collector to reject requests without X-Faro-Session-Id.");
    }

    [TestMethod]
    public async Task SendCrashReport_SubmitsWithoutWaitingForResponse()
    {
        var uri = RequireGrafanaCollectorUri();

        var sendResult = await CrashReportService.SendAsync(new InvalidOperationException("Integration test crash report"));

        Assert.IsTrue(
            sendResult.Success,
            sendResult.Error
            ?? $"HTTP {sendResult.StatusCode}. If this fails, enable Session tracking for the ADB Explorer Frontend Observability app in Grafana Cloud.");
        Assert.AreEqual(400, sendResult.StatusCode, "Send should succeed after the collector ping.");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task SendCrashReport_ToGrafanaCloud_EventuallyAccepted()
    {
        var uri = RequireGrafanaCollectorUri();

        var sessionId = Guid.NewGuid().ToString("N");
        var json = BuildTestPayload(sessionId);
        var headers = new Dictionary<string, string> { ["X-Faro-Session-Id"] = sessionId };

        var result = await Network.PostJsonAsync(uri, json, headers);

        Assert.IsTrue(
            result.Success,
            result.Error
            ?? $"HTTP {result.StatusCode}. If this times out, enable Session tracking for the ADB Explorer Frontend Observability app in Grafana Cloud.");
        Assert.AreEqual(202, result.StatusCode, "Grafana Faro collector should return 202 Accepted.");
    }
}
