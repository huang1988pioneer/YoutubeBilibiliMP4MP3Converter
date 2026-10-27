using System.Text;
using Avalonia;
using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

internal static class Program
{
    private static readonly string CrashLogPath = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YoutubeOrBilibiliMP3Converter",
        "crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            var directory = IoPath.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var text = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception?.GetType().FullName ?? "unknown"}")
                .AppendLine(exception?.ToString() ?? "(no exception object)")
                .AppendLine(new string('-', 60))
                .ToString();

            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
            // Logging must never mask the original failure.
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
