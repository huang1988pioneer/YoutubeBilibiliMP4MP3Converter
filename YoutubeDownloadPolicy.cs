namespace YoutubeOrBilibiliMP3Converter;

internal static class YoutubeDownloadPolicy
{
    public const string StandardExtractorArgs = "youtube:player_client=default,ios,tv,web";
    public const string FallbackExtractorArgs = "youtube:player_client=ios,tv,web";

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

    public static string GetMp4FormatSelector(string? mp4Quality)
    {
        var maxHeight = (mp4Quality ?? "1080P").ToUpperInvariant() switch
        {
            "4K" => 2160,
            "720P" or "720" => 720,
            "480P" or "480" => 480,
            _ => 1080
        };

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

    public static bool LooksLikeOutdatedYtDlp(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("older than 90 days", StringComparison.OrdinalIgnoreCase);

    public static string OutdatedYtDlpHint(bool isMac) =>
        isMac
            ? "yt-dlp \u7248\u672c\u904e\u820a\uff0cYouTube \u5bb9\u6613\u51fa\u73fe 403\u3002\u8acb\u5728\u7d42\u7aef\u6a5f\u57f7\u884c\uff1abrew upgrade yt-dlp"
            : "yt-dlp \u7248\u672c\u904e\u820a\uff0cYouTube \u5bb9\u6613\u51fa\u73fe 403\u3002\u8acb\u66f4\u65b0 yt-dlp \u5f8c\u518d\u8a66\u3002";

    public static string ForbiddenRetryHint() =>
        "YouTube \u62d2\u7d55\u4e0b\u8f09\uff08403\uff09\u3002\u6b63\u5728\u6539\u7528\u76f8\u5bb9\u756b\u8cea\u8207\u64ad\u653e\u5668\u5ba2\u6236\u7aef\u91cd\u8a66\u3002";

    public static string ForbiddenGiveUpHint(bool isMac) =>
        isMac
            ? "YouTube \u4ecd\u56de\u50b3 403\u3002\u8acb\u57f7\u884c brew upgrade yt-dlp\uff0c\u6216\u532f\u5165\u5df2\u767b\u5165\u7684 cookies.txt \u5f8c\u91cd\u8a66\u3002"
            : "YouTube \u4ecd\u56de\u50b3 403\u3002\u8acb\u66f4\u65b0 yt-dlp\uff0c\u6216\u532f\u5165\u5df2\u767b\u5165\u7684 cookies.txt \u5f8c\u91cd\u8a66\u3002";
}
