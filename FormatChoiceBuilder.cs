using System.Globalization;
using System.Text.Json;

namespace YoutubeOrBilibiliMP3Converter;

/// <summary>
/// Turns a yt-dlp info JSON document into the format rows shown on the picker:
/// one mp4 row per video height, then an audio-only mp3 row.
/// </summary>
internal static class FormatChoiceBuilder
{
    internal sealed record Choice(string Kind, string Quality, string Label);

    public static List<Choice> FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromElement(doc.RootElement);
    }

    public static List<Choice> FromElement(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
        {
            return Fallback();
        }

        long? audioSize = null;
        double bestAudioScore = double.MinValue;
        var videos = new List<JsonElement>();
        foreach (var format in formats.EnumerateArray())
        {
            if (IsAudioOnly(format))
            {
                var score = ReadDouble(format, "abr") ?? ReadDouble(format, "tbr") ?? 0;
                if (score >= bestAudioScore)
                {
                    bestAudioScore = score;
                    audioSize = ReadSize(format);
                }

                continue;
            }

            if (IsVideo(format))
            {
                videos.Add(format);
            }
        }

        var heights = videos
            .Select(ReadHeight)
            .Where(h => h is > 0)
            .Select(h => h!.Value)
            .Distinct()
            .OrderByDescending(h => h)
            .Take(8)
            .ToList();

        var choices = new List<Choice>();
        foreach (var height in heights)
        {
            JsonElement? best = null;
            var bestScore = double.MinValue;
            foreach (var format in videos)
            {
                if (ReadHeight(format) != height)
                {
                    continue;
                }

                var score = ScoreVideo(format);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = format;
                }
            }

            if (best is null)
            {
                continue;
            }

            var muxed = HasAudio(best.Value);
            var videoSize = ReadSize(best.Value);
            long? total = videoSize;
            if (total is not null && !muxed && audioSize is not null)
            {
                total += audioSize;
            }

            choices.Add(new Choice("video", QualityFor(height), $"{height}p · mp4{SizeSuffix(total)}"));
        }

        if (choices.Count == 0)
        {
            return Fallback();
        }

        choices.Add(new Choice("audio", "", $"僅音訊 · mp3{SizeSuffix(audioSize)}"));
        return choices;
    }

    public static List<Choice> Fallback() =>
    [
        new("video", "4K", "2160p · mp4"),
        new("video", "1440P", "1440p · mp4"),
        new("video", "1080P", "1080p · mp4"),
        new("video", "720P", "720p · mp4"),
        new("video", "480P", "480p · mp4"),
        new("video", "360P", "360p · mp4"),
        new("audio", "", "僅音訊 · mp3")
    ];

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = value >= 10 || unit == 0
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
        return $"{text} {units[unit]}";
    }

    private static string QualityFor(int height) => height == 2160 ? "4K" : $"{height}P";

    private static string SizeSuffix(long? bytes) =>
        bytes is > 0 ? $" · ~{FormatBytes(bytes.Value)}" : "";

    private static bool IsVideo(JsonElement format)
    {
        var vcodec = ReadString(format, "vcodec");
        if (string.IsNullOrEmpty(vcodec) || vcodec is "none" or "images")
        {
            return false;
        }

        var ext = ReadString(format, "ext");
        if (ext is "mhtml" or "jpg" or "png")
        {
            return false;
        }

        return ReadHeight(format) is > 0;
    }

    private static bool IsAudioOnly(JsonElement format)
    {
        var acodec = ReadString(format, "acodec");
        if (string.IsNullOrEmpty(acodec) || acodec == "none")
        {
            return false;
        }

        var vcodec = ReadString(format, "vcodec");
        return string.IsNullOrEmpty(vcodec) || vcodec == "none";
    }

    private static bool HasAudio(JsonElement format)
    {
        var acodec = ReadString(format, "acodec");
        return !string.IsNullOrEmpty(acodec) && acodec != "none";
    }

    private static double ScoreVideo(JsonElement format)
    {
        var score = ReadDouble(format, "tbr") ?? 0;
        if (string.Equals(ReadString(format, "ext"), "mp4", StringComparison.OrdinalIgnoreCase))
        {
            score += 10_000;
        }

        var vcodec = ReadString(format, "vcodec");
        if (vcodec is not null && vcodec.StartsWith("avc", StringComparison.OrdinalIgnoreCase))
        {
            score += 5_000;
        }

        return score;
    }

    private static int? ReadHeight(JsonElement format)
    {
        if (!format.TryGetProperty("height", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var height) ? height : (int)value.GetDouble();
    }

    private static long? ReadSize(JsonElement format)
    {
        foreach (var name in new[] { "filesize", "filesize_approx" })
        {
            if (!format.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var size = value.TryGetInt64(out var whole) ? whole : (long)value.GetDouble();
            if (size > 0)
            {
                return size;
            }
        }

        return null;
    }

    private static double? ReadDouble(JsonElement format, string name)
    {
        if (!format.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var number = value.GetDouble();
        return number > 0 ? number : null;
    }

    private static string? ReadString(JsonElement format, string name) =>
        format.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
