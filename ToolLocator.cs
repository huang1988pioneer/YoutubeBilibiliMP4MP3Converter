using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

internal static class ToolLocator
{
    private static readonly ConcurrentDictionary<string, string> FoundCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] UnixSearchPaths =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/usr/bin",
        "/bin"
    ];

    public static string? FindExecutable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (FoundCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var found = FindExecutableUncached(name);
        if (found is not null)
        {
            FoundCache[name] = found;
        }

        return found;
    }

    private static string? FindExecutableUncached(string name)
    {
        var executableNames = GetExecutableNames(name);
        var searchPaths = GetSearchPaths();

        foreach (var path in searchPaths)
        {
            foreach (var executableName in executableNames)
            {
                var candidate = IoPath.Combine(path, executableName);
                if (File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static void PrependToPath(IDictionary<string, string?> environment, params string[] executablePaths)
    {
        var directories = executablePaths
            .Select(IoPath.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (directories.Length == 0)
        {
            return;
        }

        var existingPath = environment.TryGetValue("PATH", out var path)
            ? path
            : Environment.GetEnvironmentVariable("PATH");

        environment["PATH"] = string.Join(IoPath.PathSeparator, directories.Concat(
            (existingPath ?? "").Split(IoPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries)));
    }

    private static IEnumerable<string> GetExecutableNames(string name)
    {
        yield return name;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IoPath.HasExtension(name))
        {
            yield break;
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var extension in extensions)
        {
            yield return $"{name}{extension.ToLowerInvariant()}";
        }
    }

    private static IEnumerable<string> GetSearchPaths()
    {
        IEnumerable<string> paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(IoPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            paths = paths.Concat(UnixSearchPaths);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
