using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

internal static class BrowserCookieLocator
{
    /// <summary>
    /// Every browser that has a usable cookie database, in preference order.
    /// A single browser is not enough: Chrome locks its database while it runs, so
    /// yt-dlp fails to copy it ("Could not copy Chrome cookie database") even though
    /// Firefox on the same machine reads fine. Callers walk this list on failure.
    /// </summary>
    public static IReadOnlyList<string> FindWindowsBrowsers(string localAppData, string roamingAppData)
    {
        var browsers = new List<string>();

        if (HasChromiumCookies(IoPath.Combine(localAppData, "Google", "Chrome", "User Data")))
        {
            browsers.Add("chrome");
        }

        if (HasChromiumCookies(IoPath.Combine(localAppData, "Microsoft", "Edge", "User Data")))
        {
            browsers.Add("edge");
        }

        if (HasFirefoxCookies(IoPath.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles")))
        {
            browsers.Add("firefox");
        }

        return browsers;
    }

    public static string? FindWindowsBrowser(string localAppData, string roamingAppData) =>
        FindWindowsBrowsers(localAppData, roamingAppData).FirstOrDefault();

    public static IReadOnlyList<string> FindMacBrowsers(string userProfile)
    {
        var browsers = new List<string>();
        var appSupport = IoPath.Combine(userProfile, "Library", "Application Support");

        if (HasFirefoxCookies(IoPath.Combine(appSupport, "Firefox", "Profiles")))
        {
            browsers.Add("firefox");
        }

        if (HasChromiumCookies(IoPath.Combine(appSupport, "Google", "Chrome")))
        {
            browsers.Add("chrome");
        }

        if (File.Exists(IoPath.Combine(userProfile, "Library", "Cookies", "Cookies.binarycookies")))
        {
            browsers.Add("safari");
        }

        return browsers;
    }

    public static string? FindMacBrowser(string userProfile) =>
        FindMacBrowsers(userProfile).FirstOrDefault();

    private static bool HasChromiumCookies(string userDataRoot)
    {
        try
        {
            if (!Directory.Exists(userDataRoot))
            {
                return false;
            }

            return Directory.EnumerateDirectories(userDataRoot)
                .Where(path =>
                {
                    var name = IoPath.GetFileName(path);
                    return name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                })
                .Any(path =>
                    File.Exists(IoPath.Combine(path, "Network", "Cookies"))
                    || File.Exists(IoPath.Combine(path, "Cookies")));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasFirefoxCookies(string profilesRoot)
    {
        try
        {
            return Directory.Exists(profilesRoot)
                && Directory.EnumerateFiles(profilesRoot, "cookies.sqlite", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }
}
