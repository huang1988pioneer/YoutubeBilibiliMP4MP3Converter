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
    AssertTrue(YoutubeDownloadPolicy.GetMp4FormatSelector("1440P").Contains("height<=1440", StringComparison.Ordinal),
        "1440P selector must cap height at 1440");
    AssertEqual("720P", YoutubeDownloadPolicy.NormalizeQuality("720"),
        "Bare heights normalize to the P suffix");
    AssertEqual("4K", YoutubeDownloadPolicy.NormalizeQuality("2160p"),
        "2160p is stored as 4K");

    var choices = FormatChoiceBuilder.FromJson("""
        {
          "formats": [
            {"ext":"mp4","vcodec":"avc1","acodec":"mp4a","height":360,"tbr":500,"filesize":1000},
            {"ext":"mp4","vcodec":"avc1","acodec":"none","height":1080,"tbr":4000,"filesize":5000000},
            {"ext":"m4a","vcodec":"none","acodec":"mp4a","abr":128,"filesize":800000},
            {"ext":"mhtml","vcodec":"images","acodec":"none","height":100,"tbr":1}
          ]
        }
        """);
    AssertEqual("1080P", choices[0].Quality, "Highest video height is listed first");
    AssertTrue(choices[0].Label.Contains("1080p · mp4", StringComparison.Ordinal)
        && choices[0].Label.Contains("MB", StringComparison.Ordinal),
        "Video rows include height, container, and estimated size");
    AssertEqual("audio", choices[^1].Kind, "Audio-only mp3 is the last row");
    AssertTrue(choices[^1].Label.StartsWith("僅音訊", StringComparison.Ordinal),
        "Audio row is labeled in Chinese");
    AssertFalse(choices.Any(choice => choice.Label.StartsWith("100p", StringComparison.Ordinal)),
        "Storyboard images are not download choices");

    AssertTrue(YoutubeDownloadPolicy.LooksLikeHttpForbidden(
            "ERROR: unable to download video data: HTTP Error 403: Forbidden"),
        "yt-dlp 403 lines must trigger a compatibility retry");
    AssertFalse(YoutubeDownloadPolicy.LooksLikeHttpForbidden("download 100%"),
        "Successful progress is not a 403");
    AssertEqual("16", YoutubeDownloadPolicy.ConcurrentFragments("https://www.bilibili.com/video/BV1kQHs6cEpP").ToString(),
        "Bilibili downloads use parallel fragments because one connection is throttled");
    AssertEqual("16", YoutubeDownloadPolicy.ConcurrentFragments("https://b23.tv/abc").ToString(),
        "Bilibili short links use the same parallel download");
    AssertEqual("4", YoutubeDownloadPolicy.ConcurrentFragments("https://www.youtube.com/watch?v=abc").ToString(),
        "YouTube uses fewer parallel fragments to avoid HTTP 403");
    AssertEqual("8", YoutubeDownloadPolicy.ConcurrentFragments("https://example.com/watch").ToString(),
        "Other sites use a middle fragment count");
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
        ? "1.5.3"
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

    AssertEqual("brew install yt-dlp ffmpeg", ToolSetupGuide.GetInstallCommand("osx", hasHomebrew: true),
        "macOS with Homebrew installs both tools via brew");
    AssertTrue(ToolSetupGuide.GetInstallCommand("osx", hasHomebrew: false).StartsWith(ToolSetupGuide.HomebrewInstallCommand, StringComparison.Ordinal),
        "macOS without Homebrew must install Homebrew first");
    AssertEqual("winget install yt-dlp.yt-dlp Gyan.FFmpeg", ToolSetupGuide.GetInstallCommand("windows", hasHomebrew: false),
        "Windows guide matches the README winget command");
    AssertTrue(ToolSetupGuide.SupportsOneClickInstall("osx") && ToolSetupGuide.SupportsOneClickInstall("windows"),
        "macOS and Windows offer one-click install");
    AssertFalse(ToolSetupGuide.SupportsOneClickInstall("linux"),
        "Linux only shows a copyable command");
    foreach (var os in new[] { "osx", "windows", "linux" })
    {
        AssertTrue(ToolSetupGuide.GetSteps(os, hasHomebrew: false).Length > 0, $"{os} install guide needs steps");
    }
    var macScript = ToolSetupGuide.BuildMacInstallScript();
    AssertTrue(macScript.StartsWith("#!/bin/zsh", StringComparison.Ordinal),
        "macOS installer script must be runnable by Terminal");
    AssertTrue(macScript.Contains("brew install yt-dlp ffmpeg", StringComparison.Ordinal),
        "macOS installer script installs both tools");

    AssertEqual("會員影片", VideoAccess.Badge(VideoAccess.Classify("普通標題", null, "subscriber_only")),
        "YouTube subscriber_only is labeled as a member video");
    AssertEqual("付費影片", VideoAccess.Badge(VideoAccess.Classify("【充电专属】完整版", null, null)),
        "Bilibili charge-exclusive titles are labeled as paid");
    AssertEqual("會員影片", VideoAccess.Badge(VideoAccess.Classify("【会员专属】直播回放", "公开说明", "public")),
        "Member-only titles stay labeled even when availability says public");
    AssertEqual("付費影片", VideoAccess.Badge(VideoAccess.Classify("電影", null, "premium_only")),
        "premium_only is a paid video");
    AssertEqual(null, VideoAccess.Badge(VideoAccess.Classify("一般影片", "public description", "public")),
        "Public videos have no access badge");
    AssertTrue(VideoAccess.RequiresAccount(VideoAccess.Kind.Member)
        && VideoAccess.RequiresAccount(VideoAccess.Kind.Paid),
        "Member and paid videos must stay downloadable with an account");
    AssertTrue(VideoAccess.LooksLikeAccountRequired(
            "ERROR: [youtube] abc: Join this channel to get access to members-only content"),
        "yt-dlp members-only errors must request cookies");
    AssertFalse(VideoAccess.LooksLikeAccountRequired("[download] 100%"),
        "Progress lines are not access errors");

    var memberFailure = DownloadFailureText.Summarize(
        "正在轉換 1/1...",
        "ERROR: [youtube] abc: Join this channel to get access to members-only content\n轉換失敗，結束碼 1",
        isMac: true);
    AssertTrue(memberFailure.Contains("會員或付費", StringComparison.Ordinal),
        "Member download failures explain that cookies are required");
    AssertFalse(memberFailure.Contains("正在轉換", StringComparison.Ordinal),
        "The progress status is not the failure reason");

    var forbidden = DownloadFailureText.Summarize(
        "正在轉換 1/1...",
        "ERROR: unable to download video data: HTTP Error 403: Forbidden\n轉換失敗，結束碼 1",
        isMac: true);
    AssertTrue(forbidden.Contains("403", StringComparison.Ordinal),
        "403 failures keep the YouTube hint");
    AssertFalse(forbidden.Contains("正在轉換", StringComparison.Ordinal),
        "403 failures do not repeat the progress status");

    var bare = DownloadFailureText.Summarize("正在轉換 1/1...", "轉換失敗，結束碼 1", isMac: true);
    AssertTrue(bare.Contains("結束碼 1", StringComparison.Ordinal),
        "A bare exit code is still reported");
    AssertFalse(bare.StartsWith("正在轉換", StringComparison.Ordinal),
        "A bare failure does not lead with the progress status");

    Console.WriteLine("PASS: cookie handling regression tests");
    Console.WriteLine("PASS: YouTube 403 / Mac copy regression tests");
    Console.WriteLine("PASS: version and tool locator tests");
    Console.WriteLine("PASS: tool setup guide tests");
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
