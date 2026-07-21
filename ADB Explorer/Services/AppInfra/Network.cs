using ADB_Explorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace ADB_Explorer.Services;

public static class Network
{
    public readonly record struct PostJsonResult(bool Success, int? StatusCode, string? Error);

    public readonly record struct HttpReachabilityResult(bool IsReachable, int? StatusCode, string? Error);

    public readonly record struct AppReleaseInfo(Version Version, string? PortableArchiveUrl);

    private static readonly SocketsHttpHandler Handler = new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    private static readonly HttpClient Client = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private static readonly HttpClient BackgroundClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        // Grafana Cloud Faro can take 30+ seconds to return 202 Accepted.
        Timeout = TimeSpan.FromSeconds(60),
    };

    private static readonly HttpClient DownloadClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    static Network()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("ADB-Explorer");
        BackgroundClient.DefaultRequestHeaders.UserAgent.ParseAdd("ADB-Explorer");
        DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("ADB-Explorer");
    }

    /// <summary>
    /// Checks that the Faro collector endpoint responds. Any HTTP status means the server is reachable.
    /// </summary>
    public static async Task<HttpReachabilityResult> PingCollectorAsync(Uri url, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);

        try
        {
            using var cts = new CancellationTokenSource(timeout.Value);
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            request.Headers.ExpectContinue = false;

            using var response = await Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            return new(true, (int)response.StatusCode, null);
        }
        catch (TaskCanceledException)
        {
            return new(false, null, "Request timed out");
        }
        catch (Exception ex)
        {
            return new(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Sends JSON in the background without waiting for the caller.
    /// </summary>
    public static void PostJsonFireAndForget(Uri url, string json, IReadOnlyDictionary<string, string>? headers = null)
    {
        _ = PostJsonInBackgroundAsync(url, json, headers);
    }

    private static async Task PostJsonInBackgroundAsync(Uri url, string json, IReadOnlyDictionary<string, string>? headers)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            request.Headers.ExpectContinue = false;

            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                    request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await BackgroundClient.SendAsync(request).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort after the UI has already confirmed reachability.
        }
    }

    public static async Task<PostJsonResult> PostJsonAsync(Uri url, string json, IReadOnlyDictionary<string, string>? headers = null)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            request.Headers.ExpectContinue = false;

            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                    request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return new(true, (int)response.StatusCode, null);

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var error = string.IsNullOrWhiteSpace(body)
                ? response.ReasonPhrase
                : $"{response.ReasonPhrase}: {body}";
            return new(false, (int)response.StatusCode, error);
        }
        catch (TaskCanceledException)
        {
            return new(false, null, "Request timed out");
        }
        catch (Exception ex)
        {
            return new(false, null, ex.Message);
        }
    }

    public static async Task<string> GetRequestAsync(Uri url)
    {
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<IEnumerable<string>> GetAdbVersionListAsync()
    {
        var response = await GetRequestAsync(Resources.Links.REPO_ADB_VER_LIST).ConfigureAwait(false);
        if (response is null)
            return null;

        return AdbVersions.ParseFromVersionList(response).ToArray();
    }

    public static async Task<AppReleaseInfo?> LatestAppReleaseAsync()
    {
        var response = await GetRequestAsync(Resources.Links.REPO_RELEASES_URL).ConfigureAwait(false);
        if (response is null)
            return null;

        JArray json = (JArray)JsonConvert.DeserializeObject(response);

        if (!json.HasValues)
            return null;

        var archSuffix = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 ? "ARM64" : "x64";

        foreach (var release in json)
        {
            if (release["prerelease"].ToObject<bool>() || release["draft"].ToObject<bool>())
                continue;

            var match = AdbRegEx.RE_GITHUB_VERSION().Match(release["tag_name"].ToString());
            if (!match.Success)
                continue;

            try
            {
                Version version = new(match.Value);
                string? archiveUrl = null;

                if (release["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        if (name is null || !name.EndsWith($"_{archSuffix}.7z", StringComparison.OrdinalIgnoreCase))
                            continue;

                        archiveUrl = asset["browser_download_url"]?.ToString();
                        break;
                    }
                }

                return new(version, archiveUrl);
            }
            catch
            { }
        }

        return null;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="targetPath"/> via a temp <c>.partial</c> file, then replaces the target.
    /// </summary>
    public static async Task<bool> DownloadFileAsync(string url, string targetPath)
    {
        var partialPath = targetPath + ".partial";

        try
        {
            using var response = await DownloadClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            await using (var fs = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            File.Move(partialPath, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(partialPath))
                    File.Delete(partialPath);
            }
            catch
            { }

            return false;
        }
    }

    public static async Task<DateTime?> GetPrivacyPolicyLastUpdatedAsync()
    {
        var response = await GetRequestAsync(Resources.Links.REPO_PRIVACY_COMMITS_URL).ConfigureAwait(false);
        if (response is null)
            return null;

        try
        {
            JArray json = (JArray)JsonConvert.DeserializeObject(response)!;
            if (!json.HasValues)
                return null;

            return json[0]["commit"]?["committer"]?["date"]?.ToObject<DateTime>();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<DateTime?> GetReleasePublishedDateAsync(string version)
    {
        if (string.IsNullOrEmpty(version) || version == "0.0.0")
            return null;

        var response = await GetRequestAsync(Resources.Links.RepoReleaseByTagUrl(version)).ConfigureAwait(false);
        if (response is null)
            return null;

        try
        {
            JObject json = (JObject)JsonConvert.DeserializeObject(response)!;
            return json["published_at"]?.ToObject<DateTime>();
        }
        catch
        {
            return null;
        }
    }

    public static string GetWsaIp()
    {
        var wsaInterface = NetworkInterface.GetAllNetworkInterfaces().Where(net => net.Name.Contains(AdbExplorerConst.WSA_INTERFACE_NAME));
        if (!wsaInterface.Any())
            return null;

        var addresses = wsaInterface.First().GetIPProperties().UnicastAddresses;
        if (!addresses.Any())
            return null;

        var ipv4 = addresses.Where(add => add.Address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork);
        if (!ipv4.Any())
            return null;

        return ipv4.First().Address.ToString();
    }

    public static string? GetDefaultBrowser()
    {
        try
        {
            var browserName = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var browserCommand = Registry.ClassesRoot.OpenSubKey($@"{browserName.GetValue("Progid")}\shell\open\command").GetValue(null);
            var path = AdbRegEx.RE_EXE_FROM_REG().Match($"{browserCommand}").Value;
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    public static void OpenUrl(string url, string? browserPath)
    {
        if (!string.IsNullOrEmpty(browserPath))
            Process.Start(browserPath, $"\"{url}\"");
        else
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void OpenBrowserSearch(string query, string? browserPath)
    {
        if (!string.IsNullOrEmpty(browserPath))
            Process.Start(browserPath, $"\"? {query}\"");
        else
            OpenUrl($"https://www.google.com/search?q={Uri.EscapeDataString(query)}", browserPath);
    }
}
