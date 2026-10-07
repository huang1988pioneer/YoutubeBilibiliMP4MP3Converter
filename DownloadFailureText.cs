namespace YoutubeOrBilibiliMP3Converter;

/// <summary>
/// Turns a yt-dlp log into the sentence shown when a download stops.
/// Progress status such as "正在轉換 1/1..." is not a reason.
/// </summary>
internal static class DownloadFailureText
{
    public static string Summarize(string? status, string? log, bool isMac)
    {
        var useful = PickUsefulLine(log);
        if (VideoAccess.LooksLikeAccountRequired(useful) || VideoAccess.LooksLikeAccountRequired(log))
        {
            return string.IsNullOrWhiteSpace(useful) || IsOwnHint(useful)
                ? VideoAccess.AccountRequiredHint()
                : VideoAccess.AccountRequiredHint() + "\n" + useful;
        }

        if (YoutubeDownloadPolicy.LooksLikeHttpForbidden(useful) || YoutubeDownloadPolicy.LooksLikeHttpForbidden(log))
        {
            return YoutubeDownloadPolicy.ForbiddenGiveUpHint(isMac);
        }

        if (!string.IsNullOrWhiteSpace(useful))
        {
            return useful;
        }

        var code = ExtractExitCode(log) ?? ExtractExitCode(status);
        return code is null
            ? "下載沒有完成。"
            : $"轉換失敗，結束碼 {code}。請確認影片仍可播放；會員或付費影片需要已登入的 cookies。";
    }

    public static string? PickUsefulLine(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return null;
        }

        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? fallback = null;
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || IsGeneric(line))
            {
                continue;
            }

            if (line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)
                || VideoAccess.LooksLikeAccountRequired(line)
                || YoutubeDownloadPolicy.LooksLikeHttpForbidden(line))
            {
                return Trim(line);
            }

            fallback ??= Trim(line);
        }

        return fallback;
    }

    private static bool IsOwnHint(string? line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("請用已登入", StringComparison.Ordinal);

    private static bool IsGeneric(string line) =>
        line.StartsWith("正在轉換", StringComparison.Ordinal)
        || line.StartsWith("轉換失敗，結束碼", StringComparison.Ordinal)
        || line.StartsWith("輸出", StringComparison.Ordinal)
        || line.StartsWith("MP4", StringComparison.Ordinal)
        || line.StartsWith("播放清單", StringComparison.Ordinal)
        || line.StartsWith("字幕", StringComparison.Ordinal)
        || line.StartsWith("Cookies:", StringComparison.Ordinal)
        || line.StartsWith("解析", StringComparison.Ordinal)
        || line.StartsWith("偵測到會員或付費限制", StringComparison.Ordinal)
        || line.StartsWith("這支影片需要登入，改用", StringComparison.Ordinal)
        || line.StartsWith("會員或付費影片，改用", StringComparison.Ordinal)
        || (line.StartsWith("[", StringComparison.Ordinal) && line.Contains("] http", StringComparison.Ordinal));

    private static string Trim(string line) => line.Length <= 280 ? line : line[..280];

    private static int? ExtractExitCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const string mark = "結束碼 ";
        var index = text.LastIndexOf(mark, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var digits = text[(index + mark.Length)..].TakeWhile(char.IsDigit).ToArray();
        return digits.Length > 0 && int.TryParse(new string(digits), out var code) ? code : null;
    }
}
