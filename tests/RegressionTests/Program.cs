using YoutubeOrBilibiliMP3Converter;

var root = Path.Combine(Path.GetTempPath(), $"converter-cookie-regression-{Guid.NewGuid():N}");
var local = Path.Combine(root, "Local");
var roaming = Path.Combine(root, "Roaming");

try
{
    // Regression: an installed/empty Chrome directory is not a usable cookie source.
    Directory.CreateDirectory(Path.Combine(local, "Google", "Chrome", "User Data"));
    AssertEqual(null, BrowserCookieLocator.FindWindowsBrowser(local, roaming),
        "Empty Chrome profile must not force --cookies-from-browser chrome");

    var chromeCookies = Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Network", "Cookies");
    Directory.CreateDirectory(Path.GetDirectoryName(chromeCookies)!);
    File.WriteAllText(chromeCookies, "fixture");
    AssertEqual("chrome", BrowserCookieLocator.FindWindowsBrowser(local, roaming),
        "Chrome is selected only when its cookie database exists");

    File.Delete(chromeCookies);
    var firefoxCookies = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles", "profile.default", "cookies.sqlite");
    Directory.CreateDirectory(Path.GetDirectoryName(firefoxCookies)!);
    File.WriteAllText(firefoxCookies, "fixture");
    AssertEqual("firefox", BrowserCookieLocator.FindWindowsBrowser(local, roaming),
        "Firefox profile is detected by cookies.sqlite");

    Console.WriteLine("PASS: browser cookie detection regression tests");
    return 0;
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static void AssertEqual(string? expected, string? actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message}. Expected: {expected ?? "<null>"}; Actual: {actual ?? "<null>"}");
    }
}
