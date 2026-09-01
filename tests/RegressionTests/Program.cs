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

    AssertTrue(CookieRetryPolicy.ShouldRetryWithoutAutomaticCookies(exitCode: 1, usedAutomaticCookies: true),
        "Failed automatic browser cookies must retry anonymously");
    AssertFalse(CookieRetryPolicy.ShouldRetryWithoutAutomaticCookies(exitCode: 1, usedAutomaticCookies: false),
        "Failed user-supplied cookies must not silently drop authentication");
    AssertFalse(CookieRetryPolicy.ShouldRetryWithoutAutomaticCookies(exitCode: 0, usedAutomaticCookies: true),
        "Successful downloads must not run twice");
    AssertFalse(CookieRetryPolicy.ShouldUseAutomaticCookies(
            automaticCookiesRequested: true,
            automaticCookiesUnavailable: true,
            isBilibiliUrl: true),
        "A browser cookie source that failed during parsing must stay disabled for downloading");
    AssertTrue(CookieRetryPolicy.ShouldUseAutomaticCookies(
            automaticCookiesRequested: true,
            automaticCookiesUnavailable: false,
            isBilibiliUrl: false,
            allowForAnySite: true),
        "YouTube 403 fallback may use browser cookies");

    AssertTrue(YoutubeDownloadPolicy.IsYouTubeUrl("https://www.youtube.com/watch?v=KDKU-ifLufQ"),
        "Standard watch URLs are YouTube");
    AssertTrue(YoutubeDownloadPolicy.IsYouTubeUrl("https://youtu.be/KDKU-ifLufQ"),
        "youtu.be short links are YouTube");
    AssertFalse(YoutubeDownloadPolicy.IsYouTubeUrl("https://www.bilibili.com/video/BV1xx411c7mD"),
        "Bilibili URLs are not YouTube");

    var selector = YoutubeDownloadPolicy.GetMp4FormatSelector("1080P");
    AssertTrue(selector.Contains("vcodec^=avc1", StringComparison.Ordinal),
        "1080P selector must prefer H.264 to avoid AV1 403s");
    AssertTrue(selector.Contains("height<=1080", StringComparison.Ordinal),
        "1080P selector must cap height");
    AssertTrue(YoutubeDownloadPolicy.GetMp4FormatSelector("4K").Contains("height<=2160", StringComparison.Ordinal),
        "4K selector must allow 2160");

    AssertTrue(YoutubeDownloadPolicy.LooksLikeHttpForbidden(
            "ERROR: unable to download video data: HTTP Error 403: Forbidden"),
        "yt-dlp 403 lines must trigger a compatibility retry");
    AssertFalse(YoutubeDownloadPolicy.LooksLikeHttpForbidden("download 100%"),
        "Successful progress is not a 403");
    AssertTrue(YoutubeDownloadPolicy.LooksLikeOutdatedYtDlp(
            "WARNING: Your yt-dlp version (2026.03.17) is older than 90 days!"),
        "Outdated yt-dlp warning must be recognized");
    AssertTrue(YoutubeDownloadPolicy.OutdatedYtDlpHint(isMac: true).Contains("brew upgrade yt-dlp", StringComparison.Ordinal),
        "macOS hint must mention brew upgrade");

    AssertTrue(PlatformCopy.SupportedPlatformsLabel("osx").Contains("macOS", StringComparison.Ordinal),
        "macOS footer must not say Windows");
    AssertTrue(PlatformCopy.SupportedPlatformsLabel("windows").Contains("Windows 10/11", StringComparison.Ordinal),
        "Windows footer keeps the Windows label");
    var assemblyVersion = typeof(PlatformCopy).Assembly.GetName().Version;
    var expectedVersion = assemblyVersion is null
        ? "1.3.0"
        : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    AssertEqual(expectedVersion, PlatformCopy.DisplayVersion,
        "Footer/title version must come from the assembly");
    AssertTrue(!string.Equals(PlatformCopy.DisplayVersion, "1.0.0", StringComparison.Ordinal),
        "Displayed version must not stay frozen at 1.0.0");
    AssertTrue(PlatformCopy.SupportedPlatformsLabel("osx").Contains(PlatformCopy.DisplayVersion, StringComparison.Ordinal),
        "macOS footer must show the current assembly version");

    AssertEqual(null, ToolLocator.FindExecutable(""),
        "Empty tool names must not be treated as executables");
    AssertEqual(null, ToolLocator.FindExecutable("definitely-not-an-installed-tool-xyz"),
        "Missing tools must not be cached as a hit");
    var ytDlp = ToolLocator.FindExecutable("yt-dlp");
    if (ytDlp is not null)
    {
        AssertEqual(ytDlp, ToolLocator.FindExecutable("yt-dlp"),
            "Successful tool lookups must reuse the cached path");
    }

    Console.WriteLine("PASS: cookie handling regression tests");
    Console.WriteLine("PASS: YouTube 403 / Mac copy regression tests");
    Console.WriteLine("PASS: version and tool locator tests");
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

static void AssertTrue(bool actual, string message)
{
    if (!actual)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool actual, string message) => AssertTrue(!actual, message);
