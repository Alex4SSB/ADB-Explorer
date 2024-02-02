using ADB_Explorer.Models;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace ADB_Explorer.Services;

public static class Network
{
    private static readonly HttpClient Client = new();

    public static async Task<string> GetRequestAsync(Uri url)
    {
        try
        {
            return await Client.GetStringAsync(url);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<Version> LatestAppReleaseAsync()
    {
        Client.DefaultRequestHeaders.Add("User-Agent", "Unity web player");

        var response = await GetRequestAsync(Resources.Links.REPO_RELEASES_URL);
        if (response is null)
            return null;

        JArray json = (JArray)JsonConvert.DeserializeObject(response);

        if (!json.HasValues)
            return null;

        var ver = json[0]["tag_name"].ToString().TrimStart('v');
        return new(ver);
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
