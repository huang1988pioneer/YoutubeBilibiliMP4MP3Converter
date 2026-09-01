namespace YoutubeOrBilibiliMP3Converter;

internal static class AppHttp
{
    internal const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    internal static readonly HttpClient Shared = CreateShared();

    private static HttpClient CreateShared()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "application/json, text/plain, */*");
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept-Language",
            "zh-CN,zh-TW;q=0.9,zh;q=0.8,en;q=0.7");
        return http;
    }

    internal static HttpRequestMessage CreateGet(string url, string? referer = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", referer);
        }

        if (!string.IsNullOrWhiteSpace(origin))
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return request;
    }

    internal static async Task<byte[]> GetBytesAsync(string url, string? referer = null, CancellationToken token = default)
    {
        using var request = CreateGet(url, referer);
        using var response = await Shared.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
    }
}
