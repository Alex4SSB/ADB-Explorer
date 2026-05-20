using ADB_Explorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace ADB_Explorer.Services;

public static class Network
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });

    static Network()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("ADB-Explorer");
        Client.Timeout = TimeSpan.FromSeconds(20);
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

    public static async Task<Version> LatestAppReleaseAsync()
    {
        var response = await GetRequestAsync(Resources.Links.REPO_RELEASES_URL).ConfigureAwait(false);
        if (response is null)
            return null;

        JArray json = (JArray)JsonConvert.DeserializeObject(response);

        if (!json.HasValues)
            return null;

        foreach (var release in json)
        {
            if (release["prerelease"].ToObject<bool>() || release["draft"].ToObject<bool>())
                continue;

            var match = AdbRegEx.RE_GITHUB_VERSION().Match(release["tag_name"].ToString());
            if (match.Success)
            {
                try
                {
                    return new(match.Value);
                }
                catch
                { }
            }
        }

        return null;
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

    public static string GetDefaultBrowser()
    {
        try
        {
            var browserName = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var browserCommand = Registry.ClassesRoot.OpenSubKey($@"{browserName.GetValue("Progid")}\shell\open\command").GetValue(null);
            return AdbRegEx.RE_EXE_FROM_REG().Match($"{browserCommand}").Value;
        }
        catch
        {
            return null;
        }
    }
}
