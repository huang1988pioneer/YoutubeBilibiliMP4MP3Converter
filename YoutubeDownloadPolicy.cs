namespace YoutubeOrBilibiliMP3Converter;

internal static class YoutubeDownloadPolicy
{
    public const string StandardExtractorArgs = "youtube:player_client=default,ios,tv,web";
    public const string FallbackExtractorArgs = "youtube:player_client=ios,tv,web";

    public static bool IsBilibiliUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("b23.tv", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".b23.tv", StringComparison.OrdinalIgnoreCase)
            || host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Browser headers for Bilibili search. Media downloads add
    /// <see cref="BilibiliCdnRangeHeader"/>; search stays on this list because a Range
    /// header can make a JSON API answer 206.
    /// </summary>
    public static readonly string[] BilibiliPageHeaders =
    [
        "User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        "Referer:https://www.bilibili.com/",
        "Accept-Language:zh-CN,zh-TW;q=0.9,zh;q=0.8,en;q=0.7",
    ];

    /// <summary>
    /// Tencent EdgeOne (<c>*.bilivideo.com</c>) answers HTTP 567 to a request that has no
    /// Range header, before a single byte is sent. <c>bytes=0-</c> is the whole file.
    /// yt-dlp still rewrites Range when it resumes a .part file.
    /// </summary>
    public const string BilibiliCdnRangeHeader = "Range:bytes=0-";

    public static IEnumerable<string> BilibiliMediaHeaders()
    {
        foreach (var header in BilibiliPageHeaders)
        {
            yield return header;
        }

        yield return BilibiliCdnRangeHeader;
    }

    /// <summary>
    /// Keep Bilibili's fragment count modest. A missing Range header is what makes
    /// EdgeOne return 567 immediately; extra parallel pulls still make a real throttle
    /// last longer. The download resumes from the .part file on the next attempt.
    /// YouTube stays low so extra connections do not trip HTTP 403.
    /// </summary>
    public static int ConcurrentFragments(string? url)
    {
        if (IsBilibiliUrl(url))
        {
            return 4;
        }

        if (IsYouTubeUrl(url))
        {
            return 4;
        }

        return 8;
    }

    public static bool IsYouTubeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            || host.Contains("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);
    }

    public static int ParseMaxHeight(string? mp4Quality)
    {
        var raw = (mp4Quality ?? "1080P").Trim().ToUpperInvariant();
        if (raw is "4K" or "2160" or "2160P")
        {
            return 2160;
        }

        if (raw.EndsWith('P'))
        {
            raw = raw[..^1];
        }

        return int.TryParse(raw, out var height) && height > 0 ? height : 1080;
    }

    public static string NormalizeQuality(string? quality)
    {
        var raw = (quality ?? "").Trim().ToUpperInvariant();
        if (raw is "4K" or "2160" or "2160P")
        {
            return "4K";
        }

        var digits = raw.EndsWith('P') ? raw[..^1] : raw;
        if (int.TryParse(digits, out var height) && height > 0)
        {
            return $"{height}P";
        }

        return "1080P";
    }

    public static string GetMp4FormatSelector(string? mp4Quality)
    {
        var maxHeight = ParseMaxHeight(mp4Quality);

        // Prefer H.264 + AAC. AV1/WebM (399+251) is more likely to 403 on YouTube.
        return $"bestvideo[height<={maxHeight}][vcodec^=avc1]+bestaudio[acodec^=mp4a]/" +
               $"bestvideo[height<={maxHeight}][ext=mp4]+bestaudio[ext=m4a]/" +
               $"best[height<={maxHeight}][ext=mp4]/" +
               $"bestvideo[height<={maxHeight}]+bestaudio/" +
               $"best[height<={maxHeight}]/best";
    }

    public static bool LooksLikeHttpForbidden(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase)
            || text.Contains("403: Forbidden", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 567 is not a real HTTP status. Tencent EdgeOne uses it when a media request has no
    /// Range header. Probed BV1LnW56bE9c on upos-sz-mirrorcosov.bilivideo.com: the same
    /// header-less request was still 567 at 0, 1, 2 and 3 minutes, with no Retry-After.
    /// A request that sent Range got 206 immediately afterwards, and 32 parallel ranged
    /// requests never opened a cooldown. Waiting a few minutes does not clear it.
    /// </summary>
    public const int MeasuredCdnCooldownMinutes = 0;

    public static bool LooksLikeCdnThrottle(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("HTTP Error 567", StringComparison.OrdinalIgnoreCase);

    public static string CdnThrottleHint() =>
        MeasuredCdnCooldownMinutes <= 0
            ? "Bilibili 的 CDN 回了 HTTP 567。這不是要先等幾分鐘的限流，空等不會解開。"
              + "請直接再按一次下載，已下載的部分會從中斷處續傳。"
            : "Bilibili 的 CDN 暫時擋下了這支影片（HTTP 567）。"
              + $"請先等 {MeasuredCdnCooldownMinutes} 分鐘，再按一次下載，會從中斷處續傳。";

    public static bool LooksLikeOutdatedYtDlp(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("older than 90 days", StringComparison.OrdinalIgnoreCase);

    public static string OutdatedYtDlpHint(bool isMac) =>
        isMac
            ? "yt-dlp \u7248\u672c\u904e\u820a\uff08\u5df2\u8d85\u904e 90 \u5929\uff09\uff0cYouTube \u5bb9\u6613\u51fa\u73fe 403\u3002\u8acb\u5728\u7d42\u7aef\u6a5f\u57f7\u884c\uff1abrew upgrade yt-dlp"
            : "yt-dlp \u7248\u672c\u904e\u820a\uff08\u5df2\u8d85\u904e 90 \u5929\uff09\uff0cYouTube \u5bb9\u6613\u51fa\u73fe 403\u3002\u8acb\u5728\u7d42\u7aef\u6a5f\u57f7\u884c\uff1awinget upgrade yt-dlp.yt-dlp\uff08\u6216 yt-dlp -U\uff09\u5f8c\u518d\u8a66\u3002";

    /// <summary>
    /// Shown when a 403 happens and yt-dlp itself reported being older than 90 days.
    /// Updating yt-dlp is then the actual fix; asking for cookies.txt first wastes the user's time.
    /// </summary>
    public static string OutdatedYtDlpFailureHint(bool isMac) =>
        isMac
            ? "YouTube 回傳 403，而且目前 yt-dlp 已超過 90 天未更新。請先更新：brew upgrade yt-dlp，更新後再試一次。"
            : "YouTube 回傳 403，而且目前 yt-dlp 已超過 90 天未更新。請先更新：winget upgrade yt-dlp.yt-dlp（或 yt-dlp -U），更新後再試一次。";

    public static string ForbiddenRetryHint() =>
        "YouTube \u62d2\u7d55\u4e0b\u8f09\uff08403\uff09\u3002\u6b63\u5728\u6539\u7528\u76f8\u5bb9\u756b\u8cea\u8207\u64ad\u653e\u5668\u5ba2\u6236\u7aef\u91cd\u8a66\u3002";

    public static string ForbiddenGiveUpHint(bool isMac) =>
        isMac
            ? "YouTube \u4ecd\u56de\u50b3 403\u3002\u8acb\u57f7\u884c brew upgrade yt-dlp\uff0c\u6216\u532f\u5165\u5df2\u767b\u5165\u7684 cookies.txt \u5f8c\u91cd\u8a66\u3002"
            : "YouTube \u4ecd\u56de\u50b3 403\u3002\u8acb\u66f4\u65b0 yt-dlp\uff0c\u6216\u532f\u5165\u5df2\u767b\u5165\u7684 cookies.txt \u5f8c\u91cd\u8a66\u3002";
}
