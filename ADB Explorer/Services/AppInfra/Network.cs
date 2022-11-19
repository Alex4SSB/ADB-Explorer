namespace ADB_Explorer.Services;

public static class Network
{
    private static readonly WebClient Client = new WebClient() { Headers = new() { "User-Agent: Unity web player" } };

    public static string GetRequest(Uri url)
    {
        try
        {
            return Client.DownloadString(url);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Version LatestAppRelease()
    {
        var response = GetRequest(Resources.Links.REPO_RELEASES_URL);
        if (response is null)
            return null;

        JArray json = (JArray)JsonConvert.DeserializeObject(response);

        if (!json.HasValues)
            return null;

        var ver = json[0]["tag_name"].ToString().TrimStart('v');
        return new(ver);
    }
}
