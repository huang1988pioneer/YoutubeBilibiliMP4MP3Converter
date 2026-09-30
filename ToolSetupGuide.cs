using System.Runtime.InteropServices;

namespace YoutubeOrBilibiliMP3Converter;

/// <summary>
/// Platform-specific instructions for installing the external tools (yt-dlp + ffmpeg).
/// Kept free of UI code so the copy can be regression-tested.
/// </summary>
internal static class ToolSetupGuide
{
    public const string HomebrewInstallCommand =
        "/bin/bash -c \"$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\"";

    private static readonly string[] HomebrewPaths = ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"];

    public static string CurrentOs =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "windows"
                : "linux";

    public static bool HasHomebrew() =>
        HomebrewPaths.Any(File.Exists) || ToolLocator.FindExecutable("brew") is not null;

    /// <summary>One line the user can paste into a terminal.</summary>
    public static string GetInstallCommand(string os, bool hasHomebrew) =>
        os switch
        {
            "windows" => "winget install yt-dlp.yt-dlp Gyan.FFmpeg",
            "osx" when hasHomebrew => "brew install yt-dlp ffmpeg",
            "osx" => $"{HomebrewInstallCommand} && brew install yt-dlp ffmpeg",
            _ => "sudo apt install yt-dlp ffmpeg"
        };

    public static bool SupportsOneClickInstall(string os) => os is "osx" or "windows";

    public static string[] GetSteps(string os, bool hasHomebrew) =>
        os switch
        {
            "windows" =>
            [
                "按下「一鍵安裝」，出現「是否允許變更」時選「是」。",
                "終端機詢問是否同意條款時，輸入 Y 再按 Enter。",
                "看到安裝完成後關掉終端機，回到這裡按「重新檢查」。",
                "若顯示找不到 winget，請先從 Microsoft Store 安裝或更新「應用程式安裝程式」。"
            ],
            "osx" when hasHomebrew =>
            [
                "按下「一鍵安裝」，會開啟「終端機」並自動執行安裝指令。",
                "等待畫面出現「安裝完成」（約 1～3 分鐘）。",
                "回到這裡，程式會自動重新檢查；也可以按「重新檢查」。"
            ],
            "osx" =>
            [
                "按下「一鍵安裝」，會開啟「終端機」，先安裝 Homebrew（Mac 常用的免費套件管理工具）。",
                "要求輸入 Password 時，輸入你的 Mac 登入密碼後按 Enter（輸入時畫面不會顯示字元，是正常的）。",
                "出現「Press RETURN」時按 Enter 繼續，接著會自動安裝 yt-dlp 與 ffmpeg。",
                "看到「安裝完成」後回到這裡，程式會自動重新檢查。"
            ],
            _ =>
            [
                "開啟終端機，貼上下方指令後按 Enter（其他發行版請改用對應的套件管理工具）。",
                "安裝完成後回到這裡按「重新檢查」。"
            ]
        };

    /// <summary>zsh script run in Terminal.app by the macOS one-click install.</summary>
    public static string BuildMacInstallScript() =>
        """
        #!/bin/zsh
        clear
        echo "影音轉換大師：安裝配套工具 yt-dlp 與 ffmpeg"
        echo ""
        load_brew() {
          for p in /opt/homebrew/bin/brew /usr/local/bin/brew; do
            if [ -x "$p" ]; then eval "$("$p" shellenv)"; return 0; fi
          done
          return 1
        }
        if ! load_brew; then
          echo "第一步：安裝 Homebrew。要求輸入 Password 時，請輸入 Mac 登入密碼（畫面不會顯示字元）。"
          echo ""
          /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
          if ! load_brew; then
            echo ""
            echo "Homebrew 安裝未完成，請重新按「一鍵安裝」再試一次。"
            exit 1
          fi
        fi
        echo ""
        echo "正在安裝 yt-dlp 與 ffmpeg，請稍候..."
        if brew install yt-dlp ffmpeg; then
          echo ""
          echo "✅ 安裝完成！請回到「影音轉換大師」，程式會自動偵測。這個視窗可以關閉了。"
        else
          echo ""
          echo "❌ 安裝失敗，請把上面的訊息截圖回報。"
          exit 1
        fi
        """;
}
