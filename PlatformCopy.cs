using System.Runtime.InteropServices;

namespace YoutubeOrBilibiliMP3Converter;

internal static class PlatformCopy
{
    public static string DisplayVersion
    {
        get
        {
            var version = typeof(PlatformCopy).Assembly.GetName().Version;
            return version is null ? "1.3.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static string UiFontFamily =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "PingFang TC, PingFang HK, Hiragino Sans GB, SF Pro Text, Inter, sans-serif"
            : "Microsoft JhengHei UI, Segoe UI, Inter, sans-serif";

    public static string MonoFontFamily =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "SF Mono, Menlo, PingFang TC, monospace"
            : "Consolas, Microsoft JhengHei UI, monospace";

    public static string SupportedPlatformsLabel() =>
        SupportedPlatformsLabel(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "windows"
                    : "linux");

    public static string SupportedPlatformsLabel(string os) =>
        os switch
        {
            "osx" => $"\u7248\u672c\uff1a{DisplayVersion}  |  \u652f\u63f4 macOS 12+",
            "windows" => $"\u7248\u672c\uff1a{DisplayVersion}  |  \u652f\u63f4 Windows 10/11",
            _ => $"\u7248\u672c\uff1a{DisplayVersion}  |  \u652f\u63f4 Linux"
        };
}
