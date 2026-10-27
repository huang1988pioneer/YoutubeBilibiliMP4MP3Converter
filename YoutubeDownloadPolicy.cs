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
    /// Bilibili's CDN answers with a fake "567 Unknown Status" once it decides a client is
    /// pulling too much, and a large video then dies partway through with "Giving up after
    /// 8 retries". Sixteen parallel range requests get there much faster than four, so keep
    /// the count modest; the download resumes from the .part file on the next attempt.
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
    /// 567 is not a real HTTP status: Bilibili's CDN uses it to reject requests it does not
    /// like, most often too many parallel range requests for the same file.
    /// </summary>
    public static bool LooksLikeCdnThrottle(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("HTTP Error 567", StringComparison.OrdinalIgnoreCase);

    public static string CdnThrottleHint() =>
        "Bilibili 的 CDN 暫時擋下了這支影片（HTTP 567，是它自己的限流代號）。"
        + "先等 3–5 分鐘，再按一次下載就會從中斷的地方續傳；不要連按重試，那只會讓限流更久。";

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
