using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

internal static class BrowserCookieLocator
{
    public static string? FindWindowsBrowser(string localAppData, string roamingAppData)
    {
        var chromeRoot = IoPath.Combine(localAppData, "Google", "Chrome", "User Data");
        if (HasChromiumCookies(chromeRoot))
        {
            return "chrome";
        }

        var edgeRoot = IoPath.Combine(localAppData, "Microsoft", "Edge", "User Data");
        if (HasChromiumCookies(edgeRoot))
        {
            return "edge";
        }

        var firefoxRoot = IoPath.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles");
        if (HasFirefoxCookies(firefoxRoot))
        {
            return "firefox";
        }

        return null;
    }

    public static string? FindMacBrowser(string userProfile)
    {
        var appSupport = IoPath.Combine(userProfile, "Library", "Application Support");
        if (HasFirefoxCookies(IoPath.Combine(appSupport, "Firefox", "Profiles")))
        {
            return "firefox";
        }

        if (HasChromiumCookies(IoPath.Combine(appSupport, "Google", "Chrome")))
        {
            return "chrome";
        }

        if (File.Exists(IoPath.Combine(userProfile, "Library", "Cookies", "Cookies.binarycookies")))
        {
            return "safari";
        }

        return null;
    }

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
