using System.Runtime.InteropServices;
using System.Text.Json;
using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

internal sealed class RecentSearchSetting
{
    public string Query { get; set; } = "";
    public string Platform { get; set; } = "both";
    public DateTime? SearchedAtUtc { get; set; }
}

internal sealed class AppSettings
{
    private static readonly string SettingsDirectory = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YoutubeOrBilibiliMP3Converter");

    private static readonly string LegacySettingsPath = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YoutubeToMP3Converter",
        "settings.json");

    private static readonly string SettingsPath = IoPath.Combine(SettingsDirectory, "settings.json");

    public string LastOutputFolder { get; init; } = GetDefaultOutputFolder();
    public int UrlInputCount { get; init; } = 1;
    public string OutputFormat { get; init; } = "MP4";
    public string Mp4Quality { get; init; } = "1080P";
    public bool? IncludeSubtitles { get; init; } = false;
    public bool? DownloadPlaylist { get; init; } = false;
    public int TodayDownloadCount { get; init; }
    public DateOnly TodayDate { get; init; } = DateOnly.FromDateTime(DateTime.Now);
    public string? CookieFilePath { get; init; }
    public List<RecentSearchSetting>? RecentSearches { get; init; }
    public string? Theme { get; init; }

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (settings is not null)
                {
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    var recent = (settings.RecentSearches ?? [])
                        .Where(e => !string.IsNullOrWhiteSpace(e.Query))
                        .GroupBy(e => (
                            Query: e.Query.Trim().ToLowerInvariant(),
                            Platform: (e.Platform ?? "both").Trim().ToLowerInvariant()))
                        .Select(g => g.OrderByDescending(x => x.SearchedAtUtc ?? DateTime.MinValue).First())
                        .OrderByDescending(e => e.SearchedAtUtc ?? DateTime.MinValue)
                        .Take(12)
                        .Select(e => new RecentSearchSetting
                        {
                            Query = e.Query.Trim(),
                            Platform = e.Platform ?? "both",
                            SearchedAtUtc = e.SearchedAtUtc
                        })
                        .ToList();

                    return new AppSettings
                    {
                        LastOutputFolder = Directory.Exists(settings.LastOutputFolder)
                            ? settings.LastOutputFolder
                            : GetDefaultOutputFolder(),
                        UrlInputCount = settings.UrlInputCount is 1 or 3 or 7 ? settings.UrlInputCount : 1,
                        OutputFormat = string.Equals(settings.OutputFormat, "MP3", StringComparison.OrdinalIgnoreCase) ? "MP3" : "MP4",
                        Mp4Quality = YoutubeDownloadPolicy.NormalizeQuality(settings.Mp4Quality),
                        Theme = settings.Theme,
                        // Missing property in older settings.json => default off.
                        IncludeSubtitles = settings.IncludeSubtitles ?? false,
                        DownloadPlaylist = settings.DownloadPlaylist ?? false,
                        TodayDownloadCount = settings.TodayDate == today ? settings.TodayDownloadCount : 0,
                        TodayDate = today,
                        CookieFilePath = !string.IsNullOrEmpty(settings.CookieFilePath) && File.Exists(settings.CookieFilePath)
                            ? settings.CookieFilePath
                            : null,
                        RecentSearches = recent
                    };
                }
            }
        }
        catch
        {
            // Invalid settings should not stop the app from opening.
        }

        return new AppSettings();
    }

    public static void Save(
        string outputFolder,
        int urlInputCount,
        string outputFormat,
        string mp4Quality,
        int todayDownloadCount = 0,
        DateOnly? todayDate = null,
        bool includeSubtitles = false,
        string? cookieFilePath = null,
        bool downloadPlaylist = false,
        IReadOnlyList<RecentSearchSetting>? recentSearches = null,
        string? theme = null)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var settings = new AppSettings
            {
                LastOutputFolder = outputFolder,
                UrlInputCount = urlInputCount,
                OutputFormat = string.Equals(outputFormat, "MP3", StringComparison.OrdinalIgnoreCase) ? "MP3" : "MP4",
                Mp4Quality = YoutubeDownloadPolicy.NormalizeQuality(mp4Quality),
                IncludeSubtitles = includeSubtitles,
                DownloadPlaylist = downloadPlaylist,
                TodayDownloadCount = todayDownloadCount,
                TodayDate = todayDate ?? DateOnly.FromDateTime(DateTime.Now),
                CookieFilePath = cookieFilePath,
                RecentSearches = recentSearches?.Take(12).ToList() ?? [],
                Theme = theme
            };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Preferences are best-effort.
        }
    }

    public static string GetDefaultOutputFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var preferred = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? IoPath.Combine(home, "Downloads")
            : IoPath.Combine(home, "Videos", "Converted");
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && Directory.Exists(preferred))
            {
                return preferred;
            }

            Directory.CreateDirectory(preferred);
            return preferred;
        }
        catch
        {
            var downloads = IoPath.Combine(home, "Downloads");
            return Directory.Exists(downloads) ? downloads : home;
        }
    }

}
