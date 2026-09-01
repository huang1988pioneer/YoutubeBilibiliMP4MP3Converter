namespace YoutubeOrBilibiliMP3Converter;

internal static class CookieRetryPolicy
{
    public static bool ShouldUseAutomaticCookies(
        bool automaticCookiesRequested,
        bool automaticCookiesUnavailable,
        bool isBilibiliUrl,
        bool allowForAnySite = false) =>
        automaticCookiesRequested && !automaticCookiesUnavailable && (isBilibiliUrl || allowForAnySite);

    public static bool ShouldRetryWithoutAutomaticCookies(int exitCode, bool usedAutomaticCookies) =>
        exitCode != 0 && usedAutomaticCookies;
}
