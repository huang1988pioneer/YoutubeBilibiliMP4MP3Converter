namespace YoutubeOrBilibiliMP3Converter;

/// <summary>
/// Labels member-only and paid videos, and recognizes yt-dlp errors that need a logged-in cookie.
/// </summary>
internal static class VideoAccess
{
    internal enum Kind
    {
        Public,
        Member,
        Paid,
        NeedsLogin
    }

    public static Kind Classify(string? title, string? description, string? availability)
    {
        var text = $"{title}\n{description}";
        if (IsPaid(text, availability))
        {
            return Kind.Paid;
        }

        if (IsMember(text, availability))
        {
            return Kind.Member;
        }

        if (string.Equals(availability, "needs_auth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(availability, "private", StringComparison.OrdinalIgnoreCase))
        {
            return Kind.NeedsLogin;
        }

        return Kind.Public;
    }

    public static bool RequiresAccount(Kind kind) => kind != Kind.Public;

    public static string? Badge(Kind kind) => kind switch
    {
        Kind.Member => "會員影片",
        Kind.Paid => "付費影片",
        Kind.NeedsLogin => "需登入",
        _ => null
    };

    public static string AccountRequiredHint() =>
        "這是會員或付費影片。請用已登入、且有觀看權限的瀏覽器，或在設定匯入 cookies.txt，然後再下載。";

    public static bool LooksLikeAccountRequired(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("members-only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("members only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("member-only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("channel's members", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Join this channel", StringComparison.OrdinalIgnoreCase)
            || text.Contains("subscriber_only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("premium_only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("會員專屬", StringComparison.Ordinal)
            || text.Contains("会员专属", StringComparison.Ordinal)
            || text.Contains("充電專屬", StringComparison.Ordinal)
            || text.Contains("充电专属", StringComparison.Ordinal)
            || text.Contains("付費", StringComparison.Ordinal)
            || text.Contains("付费", StringComparison.Ordinal);
    }

    private static bool IsPaid(string text, string? availability) =>
        string.Equals(availability, "premium_only", StringComparison.OrdinalIgnoreCase)
        || text.Contains("充電專屬", StringComparison.Ordinal)
        || text.Contains("充电专属", StringComparison.Ordinal)
        || text.Contains("付費專屬", StringComparison.Ordinal)
        || text.Contains("付费专属", StringComparison.Ordinal)
        || text.Contains("付費影片", StringComparison.Ordinal)
        || text.Contains("付费视频", StringComparison.Ordinal);

    private static bool IsMember(string text, string? availability) =>
        string.Equals(availability, "subscriber_only", StringComparison.OrdinalIgnoreCase)
        || text.Contains("members-only", StringComparison.OrdinalIgnoreCase)
        || text.Contains("members only", StringComparison.OrdinalIgnoreCase)
        || text.Contains("member-only", StringComparison.OrdinalIgnoreCase)
        || text.Contains("會員專屬", StringComparison.Ordinal)
        || text.Contains("会员专属", StringComparison.Ordinal)
        || text.Contains("大會員", StringComparison.Ordinal)
        || text.Contains("大会员", StringComparison.Ordinal);
}
