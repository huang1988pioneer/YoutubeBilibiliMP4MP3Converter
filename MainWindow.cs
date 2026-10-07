using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

public sealed partial class MainWindow : Window
{
    private static readonly string[] Mp4QualityOptions = ["480P", "720P", "1080P", "4K"];
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding SystemAnsiEncoding = GetSystemAnsiEncoding();
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(?<percent>\d+(?:\.\d+)?)%.*?at\s+(?<speed>\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DestinationRegex = new(
        @"(?:Destination:\s+|Merging formats into "")(?<path>.+?)(?:""|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] PreferredSubLangTokens =
    [
        "zh-Hant", "zh-TW", "zh-HK", "zh-Hans", "zh-CN", "zh", "en"
    ];

    private static readonly IBrush BgApp = Brush.Parse("#EEF4FB");
    private static readonly IBrush BgCard = Brush.Parse("#FFFFFF");
    private static readonly IBrush BorderSoft = Brush.Parse("#D7E4F5");
    private static readonly IBrush TextPrimary = Brush.Parse("#1A2332");
    private static readonly IBrush TextSecondary = Brush.Parse("#5B6B7C");
    private static readonly IBrush TextMuted = Brush.Parse("#8A97A8");
    private static readonly IBrush Blue = Brush.Parse("#2F7BFF");
    private static readonly IBrush BlueSoft = Brush.Parse("#E8F1FF");
    private static readonly IBrush Green = Brush.Parse("#22C55E");
    private static readonly IBrush GreenSoft = Brush.Parse("#E8F9EE");
    private static readonly IBrush RedYouTube = Brush.Parse("#FF0000");
    private static readonly IBrush PinkBili = Brush.Parse("#FB7299");
    private static readonly IBrush PinkBiliSoft = Brush.Parse("#FFE8F0");
    // WebView2 initialization can throw asynchronously on Windows when its profile
    // directory is unavailable. Keep the converter usable and open the original page
    // in the system browser instead of allowing that failure to terminate the app.
    private static readonly bool EmbeddedPreviewEnabled = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly Lazy<Bitmap?> AppIconBitmap = new(LoadAppIcon);

    private const int MaxRecentSearches = 12;

    private readonly Dictionary<string, CollapsibleSection> _sections = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedSections = new(StringComparer.Ordinal);
    private readonly List<DownloadItemView> _downloadItems = [];
    private readonly List<SearchVideoResult> _searchResults = [];
    private readonly List<RecentSearchEntry> _recentSearches = [];
    private readonly StackPanel _downloadListPanel;
    private readonly StackPanel _searchResultsPanel;
    private readonly WrapPanel _searchHistoryPanel;
    private readonly StackPanel _mainHost;
    private readonly TextBox _urlBox;
    private readonly TextBox _outputBox;
    private readonly TextBox _searchBox;
    private readonly ComboBox _qualityCombo;
    private readonly ComboBox _searchPlatformCombo;
    private readonly ComboBox _searchCountCombo;
    private readonly Button _parseButton;
    private readonly Button _convertButton;
    private readonly Button _pasteButton;
    private readonly Button _browseButton;
    private readonly Button _clearQueueButton;
    private readonly Button _searchButton;
    private readonly Button _clearSearchHistoryButton;
    private readonly TextBlock _searchStatusText;
    private readonly TextBlock _searchHistoryEmptyText;
    private readonly Border _mp4Card;
    private readonly Border _mp3Card;
    private readonly Border _previewCard;
    private readonly Border _previewPlayerHost;
    private readonly NativeWebView _previewWebView;
    private readonly Border _previewOverlay;
    private readonly Image _previewImage;
    private readonly TextBlock _previewThumbPlaceholder;
    private readonly Button _previewPlayButton;
    private readonly Button _previewStopButton;
    private readonly Button _previewBrowserButton;
    private readonly TextBlock _previewTitle;
    private readonly TextBlock _previewDuration;
    private readonly TextBlock _previewViews;
    private readonly TextBlock _previewChannelFollowers;
    private readonly TextBlock _previewDate;
    private readonly TextBlock _previewStatus;
    private readonly TextBlock _queueCountText;
    private readonly TextBlock _statusText;
    private readonly Border _parseErrorPanel;
    private readonly Border _toolSetupPanel;
    private readonly StackPanel _toolSetupBody;
    private bool _toolsMissing;
    private readonly TextBlock _parseErrorText;
    private readonly TextBlock _footerStats;
    private readonly TextBox _logText;
    private readonly Ellipse _mp4Radio;
    private readonly Ellipse _mp3Radio;
    private readonly CheckBox _subtitleCheckBox;
    private readonly CheckBox _playlistCheckBox;
    private readonly TextBlock _cookiesPathLabel;
    private readonly Button _cookiesBrowseButton;
    private readonly Button _cookiesClearButton;
    private string? _cookiesFilePath;
    private string? _lastParseErrorDetail;
    private bool _automaticBrowserCookiesUnavailable;

    private string _outputFormat = "MP4";
    private string _mp4Quality = "1080P";
    private bool _includeSubtitles = false;
    private bool _downloadPlaylist = false;
    private string _searchPlatform = "both";
    private int _searchResultLimit = 12;
    private int _todayDownloads;
    private string _lastSpeed = "-";
    private ParsedVideoInfo? _parsedInfo;
    private CancellationTokenSource? _conversionTokenSource;
    private CancellationTokenSource? _searchTokenSource;
    private DownloadItemView? _activeDownload;
    private string? _lastMediaOutputPath;
    private Bitmap? _previewBitmap;
    private int _thumbnailLoadVersion;
    private bool _embeddedPreviewActive;
    private bool _previewAdapterReady;
    private string? _previewReferer;
    private string? _pendingEmbedHtml;
    private Uri? _pendingEmbedBaseUri;
    private Uri? _pendingDirectEmbedUri;
    private CancellationTokenSource? _previewLoadCts;
    private int _previewStreamVersion;
    private bool _lastRunForbidden;
    private bool _warnedOutdatedYtDlp;

    // UI update throttling — high-frequency yt-dlp output previously flooded the UI thread
    // and froze/crashed the app during download.
    private readonly object _logLock = new();
    private readonly StringBuilder _pendingLogChunk = new();
    private bool _logFlushScheduled;
    private DateTime _lastProgressUiUtc = DateTime.MinValue;
    private DateTime _lastFooterUiUtc = DateTime.MinValue;
    private const int MaxLogCharacters = 180_000;
    private const int ProgressUiIntervalMs = 250;

    public MainWindow()
    {
        var settings = AppSettings.Load();
        _outputFormat = settings.OutputFormat;
        _mp4Quality = NormalizeMp4Quality(settings.Mp4Quality);
        _includeSubtitles = settings.IncludeSubtitles ?? false;
        _downloadPlaylist = settings.DownloadPlaylist ?? false;
        _todayDownloads = settings.TodayDownloadCount;
        if (settings.TodayDate != DateOnly.FromDateTime(DateTime.Now))
        {
            _todayDownloads = 0;
        }

        _shellTheme = ParseShellTheme(settings.Theme);

        Title = $"\u5f71\u97f3\u8f49\u63db\u5927\u5e2b v{PlatformCopy.DisplayVersion}";
        if (AppIconBitmap.Value is { } appIcon)
        {
            Icon = new WindowIcon(appIcon);
        }
        Width = 1040;
        Height = 700;
        MinWidth = 880;
        MinHeight = 600;
        Background = Brush.Parse("#101418");
        FontFamily = new FontFamily(PlatformCopy.UiFontFamily);

        _urlBox = CreateInputBox("https://www.youtube.com/watch?v=... \u6216 Bilibili \u5f71\u7247\u7db2\u5740");
        _outputBox = CreateInputBox("\u9078\u64c7\u8f38\u51fa\u8cc7\u6599\u593e");
        _outputBox.Text = settings.LastOutputFolder;
        _outputBox.LostFocus += (_, _) => SaveSettingsIfPossible();

        _searchBox = CreateInputBox("\u8f38\u5165\u95dc\u9375\u5b57\u641c\u5c0b YouTube / Bilibili \u5f71\u7247...");
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SearchVideosCoreAsync();
            }
        };

        _searchPlatformCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                "YouTube + Bilibili",
                "YouTube",
                "Bilibili"
            },
            SelectedIndex = 0,
            MinWidth = 160,
            MinHeight = 36,
            FontSize = 13
        };
        _searchPlatformCombo.SelectionChanged += (_, _) =>
        {
            _searchPlatform = _searchPlatformCombo.SelectedIndex switch
            {
                1 => "youtube",
                2 => "bilibili",
                _ => "both"
            };
        };

        _searchCountCombo = new ComboBox
        {
            ItemsSource = new[] { "6", "12", "20", "30" },
            SelectedItem = "12",
            MinWidth = 80,
            MinHeight = 36,
            FontSize = 13
        };
        _searchCountCombo.SelectionChanged += (_, _) =>
        {
            if (_searchCountCombo.SelectedItem is string raw
                && int.TryParse(raw, out var count)
                && count > 0)
            {
                _searchResultLimit = Math.Clamp(count, 1, 50);
            }
        };

        _searchButton = CreatePrimaryButton("\u641c\u5c0b\u5f71\u7247", 120);
        _searchButton.Click += async (_, _) => await SearchVideosCoreAsync();

        _searchStatusText = new TextBlock
        {
            Text = "\u8f38\u5165\u95dc\u9375\u5b57\uff0c\u641c\u5c0b YouTube \u6216 Bilibili \u5f71\u7247",
            FontSize = 13,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap
        };
        _searchResultsPanel = new StackPanel { Spacing = 10 };
        _searchHistoryPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };
        _searchHistoryEmptyText = new TextBlock
        {
            Text = "\u5c1a\u7121\u641c\u5c0b\u7d00\u9304",
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        _clearSearchHistoryButton = CreateSoftButton("\u6e05\u9664\u7d00\u9304", 96);
        _clearSearchHistoryButton.MinHeight = 32;
        _clearSearchHistoryButton.Click += (_, _) =>
        {
            _recentSearches.Clear();
            RebuildSearchHistoryPanel();
            SaveSettingsIfPossible();
            SetStatus("\u5df2\u6e05\u9664\u6700\u8fd1\u641c\u5c0b\u7d00\u9304");
        };

        // Restore recent searches from settings.
        if (settings.RecentSearches is { Count: > 0 })
        {
            foreach (var entry in settings.RecentSearches
                         .Where(e => !string.IsNullOrWhiteSpace(e.Query))
                         .Take(MaxRecentSearches))
            {
                _recentSearches.Add(new RecentSearchEntry(
                    entry.Query.Trim(),
                    NormalizeSearchPlatform(entry.Platform),
                    entry.SearchedAtUtc ?? DateTime.UtcNow));
            }
        }

        _qualityCombo = new ComboBox
        {
            ItemsSource = Mp4QualityOptions,
            SelectedItem = _mp4Quality,
            MinWidth = 110,
            MinHeight = 32,
            FontSize = 13
        };
        _qualityCombo.SelectionChanged += (_, _) =>
        {
            if (_qualityCombo.SelectedItem is string q)
            {
                _mp4Quality = NormalizeMp4Quality(q);
                SaveSettingsIfPossible();
            }
        };

        _pasteButton = CreateSoftButton("\u8cbc\u4e0a", 88);
        _pasteButton.Click += PasteUrlAsync;

        _parseButton = CreatePrimaryButton("\u89e3\u6790\u7db2\u5740", 140);
        _parseButton.Click += ParseUrlAsync;

        _browseButton = CreateSoftButton("\u700f\u89bd", 88);
        _browseButton.Click += ChooseFolderAsync;

        _convertButton = new Button
        {
            Content = "\u958b\u59cb\u8f49\u63db",
            MinHeight = 40,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Background = Green,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(16, 10)
        };
        _convertButton.Click += ConvertOrCancelAsync;

        _clearQueueButton = CreateIconTextButton("\u6e05\u7a7a");
        _clearQueueButton.Click += (_, _) =>
        {
            if (_conversionTokenSource is not null)
            {
                return;
            }

            _downloadItems.Clear();
            RebuildDownloadList();
            SetStatus("\u4e0b\u8f09\u6e05\u55ae\u5df2\u6e05\u7a7a");
        };

        _mp4Radio = CreateRadioDot(true);
        _mp3Radio = CreateRadioDot(false);
        _mp4Card = CreateFormatCard("MP4", Blue, BlueSoft, _mp4Radio, true);
        _mp3Card = CreateFormatCard("MP3", Green, GreenSoft, _mp3Radio, false);
        _mp4Card.PointerPressed += (_, _) => SetOutputFormat("MP4");
        _mp3Card.PointerPressed += (_, _) => SetOutputFormat("MP3");

        _subtitleCheckBox = new CheckBox
        {
            Content = "\u642d\u914d\u5b57\u5e55\uff08\u5916\u639b .srt\uff0cMP4 \u4e26\u5167\u5d4c\uff1bMP3 \u4e26\u751f\u6210 .lrc\uff09",
            IsChecked = _includeSubtitles,
            FontSize = 13,
            Foreground = TextPrimary,
            MinHeight = 24
        };
        _subtitleCheckBox.IsCheckedChanged += (_, _) =>
        {
            _includeSubtitles = _subtitleCheckBox.IsChecked == true;
            SaveSettingsIfPossible();
            SetStatus(_includeSubtitles
                ? "\u5df2\u958b\u555f\u5b57\u5e55\u642d\u914d\uff1a\u6703\u4e0b\u8f09\u4e26\u5c0d\u9f4a\u5f71\u97f3\u6a94"
                : "\u5df2\u95dc\u9589\u5b57\u5e55\u642d\u914d");
        };

        _playlistCheckBox = new CheckBox
        {
            Content = "\u4e0b\u8f09\u6574\u4efd\u64ad\u653e\u6e05\u55ae\uff08\u9810\u8a2d\u95dc\u9589\uff0c\u50c5\u4e0b\u7576\u524d\u5f71\u7247\uff09",
            IsChecked = _downloadPlaylist,
            FontSize = 13,
            Foreground = TextPrimary,
            MinHeight = 24
        };
        _playlistCheckBox.IsCheckedChanged += (_, _) =>
        {
            _downloadPlaylist = _playlistCheckBox.IsChecked == true;
            SaveSettingsIfPossible();
            SetStatus(_downloadPlaylist
                ? "\u5df2\u958b\u555f\u64ad\u653e\u6e05\u55ae\u4e0b\u8f09\uff1a\u6703\u4e0b\u8f09\u7db2\u5740\u6240\u5c6c\u6574\u4efd\u6e05\u55ae"
                : "\u5df2\u95dc\u9589\u64ad\u653e\u6e05\u55ae\u4e0b\u8f09\uff1a\u50c5\u8f49\u63db\u55ae\u4e00\u5f71\u7247");
        };

        _cookiesFilePath = settings.CookieFilePath;

        _cookiesPathLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(_cookiesFilePath) ? "\u672a\u8a2d\u5b9a" : IoPath.GetFileName(_cookiesFilePath),
            FontSize = 12,
            Foreground = string.IsNullOrEmpty(_cookiesFilePath) ? TextMuted : TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _cookiesBrowseButton = CreateSoftButton("\u532f\u5165 cookies.txt", 72);
        _cookiesBrowseButton.Click += ChooseCookiesFileAsync;

        _cookiesClearButton = CreateSoftButton("\u6e05\u9664", 72);
        _cookiesClearButton.Click += (_, _) =>
        {
            _cookiesFilePath = null;
            _cookiesPathLabel.Text = "\u672a\u8a2d\u5b9a";
            _cookiesPathLabel.Foreground = TextMuted;
            SaveSettingsIfPossible();
            SetStatus("\u5df2\u6e05\u9664 Cookies \u6a94\u6848");
            RefreshShellChrome();
        };

        _previewTitle = new TextBlock
        {
            Text = "\u5c1a\u672a\u89e3\u6790\u5f71\u7247",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2
        };
        _previewDuration = MetaLine("\u6642\u9577", "-");
        _previewViews = MetaLine("\u6b21\u6578", "-");
        _previewChannelFollowers = MetaLine("\u983b\u9053\u95dc\u6ce8", "-");
        _previewDate = MetaLine("\u65e5\u671f", "-");
        _previewStatus = new TextBlock
        {
            Text = "\u7b49\u5f85\u89e3\u6790",
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };

        _previewImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _previewThumbPlaceholder = new TextBlock
        {
            Text = "\u5f71\u7247\u5167\u5d4c\u9810\u89bd",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _previewWebView = new NativeWebView
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Desktop WebViews often omit a browser-like UA; some players refuse embeds.
            UserAgent =
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15"
        };
        _previewWebView.AdapterCreated += (_, _) =>
        {
            _previewAdapterReady = true;
            FlushPendingEmbeddedPreview();
        };
        _previewWebView.AdapterDestroyed += (_, _) =>
        {
            _previewAdapterReady = false;
        };
        _previewWebView.NavigationCompleted += OnPreviewNavigationCompleted;
        _previewWebView.WebResourceRequested += OnPreviewWebResourceRequested;
        _previewWebView.NewWindowRequested += (_, e) =>
        {
            // Keep playback inside the embedded player when possible.
            e.Handled = true;
            try
            {
                if (e.Request is not null)
                {
                    _previewWebView.Navigate(e.Request);
                }
            }
            catch
            {
                // ignore
            }
        };

        _previewOverlay = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#89C2FF"), 0),
                    new GradientStop(Color.Parse("#F8C1DE"), 0.55),
                    new GradientStop(Color.Parse("#FFE6A7"), 1)
                }
            },
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        _previewPlayerHost = new Border
        {
            Height = 180,
            MinHeight = 140,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Background = Brush.Parse("#0F172A"),
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1)
        };
        _previewPlayButton = CreatePrimaryButton("\u5167\u5d4c\u64ad\u653e", 120);
        _previewPlayButton.IsEnabled = false;
        _previewPlayButton.Click += PlayOriginalVideoAsync;
        _previewStopButton = CreateSoftButton("\u505c\u6b62\u9810\u89bd", 110);
        _previewStopButton.IsEnabled = false;
        _previewStopButton.Click += (_, _) => StopEmbeddedPreview();
        _previewBrowserButton = CreateSoftButton("\u539f\u9801", 88);
        _previewBrowserButton.IsEnabled = false;
        _previewBrowserButton.Click += OpenOriginalInBrowser;

        _previewCard = BuildPreviewCard();
        _queueCountText = new TextBlock
        {
            Text = "\u4e0b\u8f09\u6e05\u55ae (0)",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusText = new TextBlock
        {
            Text = "\u6e96\u5099\u5c31\u7dd2",
            FontSize = 13,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        };
        _parseErrorText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush.Parse("#912018"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };
        var parseErrorBody = new StackPanel { Spacing = 4 };
        parseErrorBody.Children.Add(new TextBlock
        {
            Text = "\u7121\u6cd5\u53d6\u5f97\u5f71\u7247\u8cc7\u8a0a",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#7A271A")
        });
        parseErrorBody.Children.Add(_parseErrorText);
        _parseErrorPanel = new Border
        {
            IsVisible = false,
            Background = Brush.Parse("#FFF4F2"),
            BorderBrush = Brush.Parse("#FECDCA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = parseErrorBody
        };
        _toolSetupBody = new StackPanel { Spacing = 8 };
        _toolSetupPanel = new Border
        {
            IsVisible = false,
            Background = Brush.Parse("#FFFBEB"),
            BorderBrush = Brush.Parse("#FCD34D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12),
            Child = _toolSetupBody
        };
        _footerStats = new TextBlock
        {
            Text = BuildFooterStats(),
            FontSize = 12,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _logText = new TextBox
        {
            Text = "",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(PlatformCopy.MonoFontFamily),
            FontSize = 11,
            Foreground = Brush.Parse("#D1D5DB"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Brush.Parse("#D1D5DB"),
            SelectionBrush = Brush.Parse("#3B82F6"),
            MinHeight = 100
        };
        _downloadListPanel = new StackPanel { Spacing = 10 };
        _mainHost = new StackPanel { Spacing = 10 };

        Content = BuildShell();
        SetOutputFormat(_outputFormat);
        Opened += (_, _) => CheckTools();
        // Users install tools in another window; re-detect when they come back.
        Activated += (_, _) =>
        {
            if (_toolsMissing)
            {
                RefreshToolSetup();
            }
        };
        Closing += (_, _) => StopEmbeddedPreview(clearStatus: false);
    }

    private Control BuildShell() => CreateShellRoot();

    private Control BuildSectionsPanel()
    {
        _sections.Clear();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(CreateSection("search", "\u641c\u5c0b\u5f71\u7247",
            "\u641c\u5c0b YouTube \u6216 Bilibili\uff0c\u9ede\u9078\u7d50\u679c\u5373\u53ef\u89e3\u6790\u6216\u8f49\u63db",
            BuildSearchSectionContent, rebuildOnExpand: false));
        panel.Children.Add(CreateSection("downloading", "\u4e0b\u8f09\u4e2d", "\u76ee\u524d\u9032\u884c\u4e2d\u7684\u8f49\u63db\u4efb\u52d9",
            () => BuildQueueSectionContent("downloading"), rebuildOnExpand: true));
        panel.Children.Add(CreateSection("done", "\u5df2\u5b8c\u6210", "\u5df2\u6210\u529f\u5b8c\u6210\u7684\u6a94\u6848",
            () => BuildQueueSectionContent("done"), rebuildOnExpand: true));
        panel.Children.Add(CreateSection("files", "\u6a94\u6848\u7ba1\u7406", "\u958b\u555f\u8f38\u51fa\u8cc7\u6599\u593e\uff0c\u7ba1\u7406\u5df2\u8f49\u63db\u7684\u6a94\u6848",
            BuildFilesSectionContent, rebuildOnExpand: true));
        panel.Children.Add(CreateSection("history", "\u6b77\u53f2\u8a18\u9304", "\u6240\u6709\u8f49\u63db\u7d00\u9304",
            () => BuildQueueSectionContent("history"), rebuildOnExpand: true));
        panel.Children.Add(CreateSection("fav", "\u6211\u7684\u6700\u611b", "\u6536\u85cf\u5e38\u7528\u5f71\u7247\u8207\u64ad\u653e\u6e05\u55ae",
            () => new TextBlock
            {
                Text = "\u4e4b\u5f8c\u53ef\u6536\u85cf\u5e38\u7528\u5f71\u7247\u8207\u64ad\u653e\u6e05\u55ae\u3002",
                FontSize = 13,
                Foreground = TextMuted
            }, rebuildOnExpand: false));
        RefreshSectionHeaders();
        return panel;
    }

    /// <summary>
    /// Collapsible block replacing a former sidebar page. Collapsed by default;
    /// the open/closed state survives home rebuilds within a session.
    /// </summary>
    private Control CreateSection(string id, string title, string subtitle, Func<Control> buildContent, bool rebuildOnExpand)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        };
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(titleText);
        header.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap
        });

        var expanded = _expandedSections.Contains(id);
        var expander = new Expander
        {
            Header = header,
            IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = BgCard,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10)
        };
        if (!rebuildOnExpand || expanded)
        {
            expander.Content = buildContent();
        }

        expander.PropertyChanged += (_, e) =>
        {
            if (e.Property != Expander.IsExpandedProperty)
            {
                return;
            }

            if (expander.IsExpanded)
            {
                _expandedSections.Add(id);
                if (rebuildOnExpand)
                {
                    expander.Content = buildContent();
                }
            }
            else
            {
                _expandedSections.Remove(id);
            }
        };

        _sections[id] = new CollapsibleSection(expander, titleText, title, buildContent, rebuildOnExpand);
        return expander;
    }

    private void OpenSection(string id)
    {
        if (!_sections.TryGetValue(id, out var section))
        {
            return;
        }

        section.Expander.IsExpanded = true;
        Dispatcher.UIThread.Post(() => section.Expander.BringIntoView(), DispatcherPriority.Background);
    }

    /// <summary>Refreshes counts in section headers and the content of open queue sections.</summary>
    private void RefreshQueueSections()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshSectionHeaders();
            foreach (var id in new[] { "downloading", "done", "history" })
            {
                if (_sections.TryGetValue(id, out var section) && section.Expander.IsExpanded)
                {
                    section.Expander.Content = section.BuildContent();
                }
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshSectionHeaders()
    {
        foreach (var id in new[] { "downloading", "done", "history" })
        {
            if (_sections.TryGetValue(id, out var section))
            {
                var count = _downloadItems.Count(item => MatchesQueueFilter(item, id));
                section.TitleText.Text = count > 0 ? $"{section.Title} ({count})" : section.Title;
            }
        }
    }

    private void ScrollToTop()
    {
    }

    private void ShowHomePage()
    {
        // Shared controls are reused across rebuilds. Avalonia forbids adding a control
        // that still has a visual parent, so detach them before clearing the host.
        // NativeWebView needs an explicit reparent scope while its host moves.
        RebuildMainHost(() =>
        {
            _mainHost.Children.Add(BuildHeader());
            _mainHost.Children.Add(_toolSetupPanel);
            _mainHost.Children.Add(BuildUrlCard());
            _mainHost.Children.Add(BuildOptionsAndPreviewRow());
            _mainHost.Children.Add(BuildQueueAndUtilsRow());
            _mainHost.Children.Add(BuildSectionsPanel());
            _mainHost.Children.Add(BuildLogCard());
            RebuildDownloadList();
        });
    }

    private static bool MatchesQueueFilter(DownloadItemView item, string sectionId) =>
        sectionId switch
        {
            "downloading" => item.State is DownloadState.Queued or DownloadState.Running or DownloadState.Paused,
            "done" => item.State == DownloadState.Completed,
            _ => true
        };

    private Control BuildQueueSectionContent(string sectionId)
    {
        var filtered = _downloadItems.Where(item => MatchesQueueFilter(item, sectionId)).Reverse().ToList();
        if (filtered.Count == 0)
        {
            return new TextBlock
            {
                Text = "\u76ee\u524d\u6c92\u6709\u9805\u76ee\u3002\u5f9e\u4e0a\u65b9\u8cbc\u4e0a\u7db2\u5740\u4e26\u958b\u59cb\u8f49\u63db\u3002",
                FontSize = 13,
                Foreground = TextMuted,
                TextWrapping = TextWrapping.Wrap
            };
        }

        // Download rows (item.Root) already live in the queue card above, and a control
        // can only have one parent, so sections show a compact summary row instead.
        var list = new StackPanel { Spacing = 8 };
        foreach (var item in filtered)
        {
            list.Children.Add(BuildQueueSummaryRow(item));
        }

        return list;
    }

    private Control BuildQueueSummaryRow(DownloadItemView item)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };

        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            Foreground = TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var meta = item.Format == "MP4" ? $"{item.Format} \u00b7 {item.Quality}" : item.Format;
        if (item.State == DownloadState.Running)
        {
            meta += $" \u00b7 {item.Progress:0}%";
        }
        info.Children.Add(new TextBlock { Text = meta, FontSize = 11, Foreground = TextMuted });
        row.Children.Add(info);

        var (stateText, stateBrush) = item.State switch
        {
            DownloadState.Running => ("\u4e0b\u8f09\u4e2d", Blue),
            DownloadState.Completed => ("\u5b8c\u6210", Green),
            DownloadState.Failed => ("\u5931\u6557", Brush.Parse("#EF4444")),
            DownloadState.Cancelled => ("\u53d6\u6d88", TextMuted),
            DownloadState.Paused => ("\u66ab\u505c", Brush.Parse("#F59E0B")),
            _ => ("\u6392\u968a", Blue)
        };
        var badge = new TextBlock
        {
            Text = stateText,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = stateBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        var hasFile = item.State == DownloadState.Completed
            && !string.IsNullOrWhiteSpace(item.OutputPath)
            && File.Exists(item.OutputPath);
        if (hasFile)
        {
            var openBtn = CreateSoftButton("\u958b\u555f\u6a94\u6848", 90);
            openBtn.MinHeight = 32;
            openBtn.Click += (_, _) => OpenMediaFile(item.OutputPath, item.Format);
            Grid.SetColumn(openBtn, 2);
            row.Children.Add(openBtn);
            var revealBtn = CreateSoftButton("\u5728\u8cc7\u6599\u593e", 90);
            revealBtn.MinHeight = 32;
            revealBtn.Click += (_, _) => RevealInFolder(item.OutputPath);
            Grid.SetColumn(revealBtn, 3);
            row.Children.Add(revealBtn);
        }

        return row;
    }

    private Control BuildFilesSectionContent()
    {
        var path = _outputBox.Text?.Trim() ?? "";
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(path) ? "\u5c1a\u672a\u8a2d\u5b9a\u8f38\u51fa\u8cc7\u6599\u593e" : path,
            FontSize = 14,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap
        });

        var openBtn = CreatePrimaryButton("\u958b\u555f\u8cc7\u6599\u593e", 140);
        openBtn.Click += (_, _) => OpenOutputFolder(path);
        panel.Children.Add(openBtn);

        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            var recent = Directory.EnumerateFiles(path)
                .Where(f =>
                {
                    var ext = IoPath.GetExtension(f);
                    return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(12)
                .ToList();

            if (recent.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "\u6700\u8fd1\u6a94\u6848",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextPrimary,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                foreach (var file in recent)
                {
                    var filePath = file;
                    var row = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                        ColumnSpacing = 8
                    };
                    row.Children.Add(new TextBlock
                    {
                        Text = IoPath.GetFileName(filePath),
                        FontSize = 12,
                        Foreground = TextPrimary,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var openFileBtn = CreateSoftButton("\u958b\u555f\u6a94\u6848", 96);
                    openFileBtn.Click += (_, _) => OpenMediaFile(filePath, IoPath.GetExtension(filePath));
                    Grid.SetColumn(openFileBtn, 1);
                    row.Children.Add(openFileBtn);
                    var revealBtn = CreateSoftButton("\u5728\u8cc7\u6599\u593e", 96);
                    revealBtn.Click += (_, _) => RevealInFolder(filePath);
                    Grid.SetColumn(revealBtn, 2);
                    row.Children.Add(revealBtn);
                    panel.Children.Add(row);
                }
            }
        }

        return panel;
    }

    private Control BuildSearchSectionContent()
    {
        var content = new StackPanel { Spacing = 12 };

        var form = new StackPanel { Spacing = 12 };

        form.Children.Add(new TextBlock
        {
            Text = "\u95dc\u9375\u5b57",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        });

        var searchRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        searchRow.Children.Add(_searchBox);
        Grid.SetColumn(_searchButton, 1);
        searchRow.Children.Add(_searchButton);
        form.Children.Add(searchRow);

        var filterRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*"),
            ColumnSpacing = 10
        };
        filterRow.Children.Add(new TextBlock
        {
            Text = "\u5e73\u53f0",
            FontSize = 13,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_searchPlatformCombo, 1);
        filterRow.Children.Add(_searchPlatformCombo);
        var countLabel = new TextBlock
        {
            Text = "\u6bcf\u5e73\u53f0\u7d50\u679c\u6578",
            FontSize = 13,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(countLabel, 2);
        filterRow.Children.Add(countLabel);
        Grid.SetColumn(_searchCountCombo, 3);
        filterRow.Children.Add(_searchCountCombo);
        form.Children.Add(filterRow);

        form.Children.Add(_searchStatusText);

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        chips.Children.Add(PlatformChip("YouTube", RedYouTube, Brush.Parse("#FFECEC")));
        chips.Children.Add(PlatformChip("bilibili", PinkBili, PinkBiliSoft));
        chips.Children.Add(new TextBlock
        {
            Text = "YouTube \u7d93 yt-dlp\uff1bBilibili \u7d93\u5b98\u65b9\u641c\u5c0b API",
            FontSize = 11,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });
        form.Children.Add(chips);

        // Recent search history
        var historyHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 6, 0, 0)
        };
        historyHeader.Children.Add(new TextBlock
        {
            Text = "\u6700\u8fd1\u641c\u5c0b\u7d00\u9304",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_clearSearchHistoryButton, 1);
        historyHeader.Children.Add(_clearSearchHistoryButton);
        form.Children.Add(historyHeader);
        form.Children.Add(_searchHistoryPanel);
        RebuildSearchHistoryPanel();

        content.Children.Add(form);

        var resultsBody = new StackPanel { Spacing = 10 };
        resultsBody.Children.Add(new TextBlock
        {
            Text = "\u641c\u5c0b\u7d50\u679c",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        });
        resultsBody.Children.Add(_searchResultsPanel);
        if (_searchResultsPanel.Children.Count == 0)
        {
            _searchResultsPanel.Children.Add(new TextBlock
            {
                Text = "\u5c1a\u7121\u7d50\u679c\u3002\u8f38\u5165\u95dc\u9375\u5b57\u5f8c\u6309\u300c\u641c\u5c0b\u5f71\u7247\u300d\u3002",
                FontSize = 13,
                Foreground = TextMuted,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        content.Children.Add(new Border { Height = 1, Background = BorderSoft, Margin = new Thickness(0, 4) });
        content.Children.Add(resultsBody);
        return content;
    }

    private void OpenOutputFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            SetStatus("\u8acb\u5148\u9078\u64c7\u6709\u6548\u7684\u8f38\u51fa\u8cc7\u6599\u593e");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus("\u7121\u6cd5\u958b\u555f\u8cc7\u6599\u593e");
            AppendLog(ex.Message);
        }
    }

    private void OpenMediaFile(string? path, string? formatHint = null)
    {
        path = ResolveOpenableMediaPath(path, formatHint);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetStatus("\u627e\u4e0d\u5230\u6a94\u6848\uff0c\u8acb\u5148\u78ba\u8a8d\u5df2\u8f49\u63db\u6210\u529f");
            AppendLog("\u958b\u555f\u6a94\u6848\u5931\u6557: \u8def\u5f91\u4e0d\u5b58\u5728");
            return;
        }

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                ErrorDialog = true
            });
            if (started is null)
            {
                // Some shells return null even when launch is handed off successfully.
                SetStatus($"\u5df2\u8acb\u6c42\u958b\u555f: {IoPath.GetFileName(path)}");
            }
            else
            {
                SetStatus($"\u5df2\u958b\u555f: {IoPath.GetFileName(path)}");
            }

            AppendLog($"\u958b\u555f\u6a94\u6848: {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"\u958b\u555f\u6a94\u6848\u5931\u6557: {ex.Message}");
            // Fallback: select the file in Explorer so user can open with another player.
            if (RevealInFolder(path))
            {
                SetStatus("\u7121\u6cd5\u76f4\u63a5\u64ad\u653e\uff0c\u5df2\u5728\u8cc7\u6599\u593e\u6a19\u793a\u6a94\u6848");
            }
            else
            {
                SetStatus("\u7121\u6cd5\u958b\u555f\u6a94\u6848\uff08\u53ef\u80fd\u6c92\u6709\u9810\u8a2d\u64ad\u653e\u5668\uff09");
            }
        }
    }

    private bool RevealInFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { "-R", path },
                    UseShellExecute = false
                });
                return true;
            }

            var dir = IoPath.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"\u958b\u555f\u8cc7\u6599\u593e\u5931\u6557: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveOpenableMediaPath(string? path, string? formatHint)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            // Intermediate audio downloads may still point at .m4a/.webm before rename.
            if (IsIntermediateAudioPath(path))
            {
                var mp3 = IoPath.ChangeExtension(path, ".mp3");
                if (File.Exists(mp3))
                {
                    return mp3;
                }
            }

            return path;
        }

        if (IsIntermediateAudioPath(path) || LooksLikeMp3Hint(formatHint))
        {
            var mp3 = IoPath.ChangeExtension(path, ".mp3");
            if (File.Exists(mp3))
            {
                return mp3;
            }
        }

        return path;
    }

    private static bool IsIntermediateAudioPath(string path) =>
        path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMp3Hint(string? formatHint) =>
        !string.IsNullOrWhiteSpace(formatHint)
        && (formatHint.Contains("MP3", StringComparison.OrdinalIgnoreCase)
            || formatHint.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Clears and rebuilds the main content host. On platforms where embedded
    /// WebView is enabled, reparenting is wrapped so the native view survives
    /// host moves. On Windows the WebView is intentionally left alone — accessing
    /// it during page switches can terminate the process.
    /// </summary>
    private void RebuildMainHost(Action build)
    {
        if (EmbeddedPreviewEnabled)
        {
            try
            {
                using var reparent = _previewWebView.BeginReparenting();
                DetachSharedControls();
                _mainHost.Children.Clear();
                build();
                return;
            }
            catch (Exception ex)
            {
                AppendLog($"\u91cd\u5efa\u4e3b\u756b\u9762\u5931\u6557\uff08WebView reparent\uff09: {ex.Message}");
            }
        }

        DetachSharedControls();
        _mainHost.Children.Clear();
        // Detach again after Clear — intermediate parents may still hold shared
        // controls if Clear only dropped the outer cards.
        DetachSharedControls();
        build();
    }

    /// <summary>
    /// Removes a control from its current visual parent so it can be reparented safely.
    /// </summary>
    private static void DetachFromParent(Control? control)
    {
        if (control is null)
        {
            return;
        }

        // Walk up until unparented (handles odd intermediate hosts).
        for (var i = 0; i < 4 && control.Parent is not null; i++)
        {
            switch (control.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(control);
                    break;
                case Decorator decorator:
                    if (ReferenceEquals(decorator.Child, control))
                    {
                        decorator.Child = null;
                    }
                    else
                    {
                        return;
                    }

                    break;
                case ContentControl contentControl:
                    if (ReferenceEquals(contentControl.Content, control))
                    {
                        contentControl.Content = null;
                    }
                    else
                    {
                        return;
                    }

                    break;
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Shared field-backed controls keep their intermediate parents after
    /// <c>_mainHost.Children.Clear()</c>. Detach them before rebuilding pages.
    /// </summary>
    private void DetachSharedControls()
    {
        // Every field-backed control reused across pages MUST be listed here.
        // Missing one causes: "The control X already has a visual parent".
        DetachFromParent(_urlBox);
        DetachFromParent(_pasteButton);
        DetachFromParent(_parseButton);
        DetachFromParent(_mp4Card);
        DetachFromParent(_mp3Card);
        DetachFromParent(_qualityCombo);
        DetachFromParent(_subtitleCheckBox);
        DetachFromParent(_playlistCheckBox);
        DetachFromParent(_cookiesPathLabel);
        DetachFromParent(_cookiesBrowseButton);
        DetachFromParent(_cookiesClearButton);
        DetachFromParent(_outputBox);
        DetachFromParent(_browseButton);
        DetachFromParent(_convertButton);
        DetachFromParent(_statusText);
        DetachFromParent(_parseErrorPanel);
        DetachFromParent(_toolSetupPanel);
        DetachFromParent(_previewCard);
        DetachFromParent(_queueCountText);
        DetachFromParent(_clearQueueButton);
        DetachFromParent(_downloadListPanel);
        DetachFromParent(_logText);
        DetachFromParent(_searchBox);
        DetachFromParent(_searchButton);
        DetachFromParent(_searchPlatformCombo);
        DetachFromParent(_searchCountCombo);
        DetachFromParent(_searchStatusText);
        DetachFromParent(_searchResultsPanel);
        DetachFromParent(_searchHistoryPanel);
        DetachFromParent(_clearSearchHistoryButton);
        DetachFromParent(_searchHistoryEmptyText);

        foreach (var item in _downloadItems)
        {
            DetachFromParent(item.Root);
        }
    }

    private Control BuildHeader()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var titlePanel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(ColoredWord("YouTube", RedYouTube, 20, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord(" / ", TextPrimary, 20, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord("Bilibili", PinkBili, 20, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord(" \u5f71\u7247\u8f49 ", TextPrimary, 20, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord("MP4", Blue, 20, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord(" / ", TextPrimary, 20, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord("MP3", Green, 20, FontWeight.Bold));
        titlePanel.Children.Add(new TextBlock
        {
            Text = "\u9ad8\u756b\u8cea\u4e0b\u8f09 \u00b7 \u5feb\u901f\u8f49\u63db \u00b7 \u6279\u91cf\u8655\u7406",
            FontSize = 12,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 3, 0, 0)
        });
        row.Children.Add(titlePanel);

        var settingsBtn = CreateSoftButton("\u8a2d\u5b9a", 72);
        settingsBtn.MinHeight = 32;
        settingsBtn.Click += (_, _) =>
        {
            OpenSection("files");
            SetStatus("\u53ef\u5728\u6b64\u7ba1\u7406\u8f38\u51fa\u8cc7\u6599\u593e");
        };
        Grid.SetColumn(settingsBtn, 1);
        settingsBtn.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(settingsBtn);
        return row;
    }

    private static Bitmap? LoadAppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://YoutubeOrBilibiliMP3Converter/Assets/app-icon.png"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Control CreateAppIconView(double size, double radius)
    {
        if (AppIconBitmap.Value is { } bitmap)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(radius),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Image
                {
                    Source = bitmap,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.UniformToFill
                }
            };
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Background = RedYouTube,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = ">",
                FontSize = Math.Max(12, size * 0.45),
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static TextBlock ColoredWord(string text, IBrush color, double size, FontWeight weight) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Control BuildUrlCard()
    {
        var body = new StackPanel { Spacing = 8 };

        var labelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        labelRow.Children.Add(new TextBlock
        {
            Text = "\u8cbc\u4e0a YouTube \u6216 Bilibili \u5f71\u7247\u7db2\u5740\uff08\u53ef\u591a\u884c\u6279\u91cf\uff09",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        chips.Children.Add(PlatformChip("YouTube", RedYouTube, Brush.Parse("#FFECEC")));
        chips.Children.Add(PlatformChip("bilibili", PinkBili, PinkBiliSoft));
        Grid.SetColumn(chips, 1);
        labelRow.Children.Add(chips);
        body.Children.Add(labelRow);

        var urlRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };
        urlRow.Children.Add(_urlBox);
        Grid.SetColumn(_pasteButton, 1);
        urlRow.Children.Add(_pasteButton);
        Grid.SetColumn(_parseButton, 2);
        urlRow.Children.Add(_parseButton);
        body.Children.Add(urlRow);

        return Card(body);
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = TextPrimary,
        MinWidth = 72,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Control BuildOptionsAndPreviewRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10
        };

        var left = new StackPanel { Spacing = 8 };

        var formatRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        formatRow.Children.Add(_mp4Card);
        Grid.SetColumn(_mp3Card, 1);
        formatRow.Children.Add(_mp3Card);
        left.Children.Add(formatRow);

        var qualityRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        qualityRow.Children.Add(FieldLabel("\u756b\u8cea"));
        qualityRow.Children.Add(_qualityCombo);
        qualityRow.Children.Add(new TextBlock
        {
            Text = "\u50c5 MP4 \u00b7 \u5efa\u8b70 1080P",
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });
        left.Children.Add(qualityRow);
        left.Children.Add(_subtitleCheckBox);
        left.Children.Add(_playlistCheckBox);

        var cookiesRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        cookiesRow.Children.Add(FieldLabel("Cookies"));
        Grid.SetColumn(_cookiesPathLabel, 1);
        cookiesRow.Children.Add(_cookiesPathLabel);
        Grid.SetColumn(_cookiesBrowseButton, 2);
        cookiesRow.Children.Add(_cookiesBrowseButton);
        Grid.SetColumn(_cookiesClearButton, 3);
        cookiesRow.Children.Add(_cookiesClearButton);
        left.Children.Add(cookiesRow);

        var pathRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8
        };
        pathRow.Children.Add(FieldLabel("\u5132\u5b58\u4f4d\u7f6e"));
        Grid.SetColumn(_outputBox, 1);
        pathRow.Children.Add(_outputBox);
        Grid.SetColumn(_browseButton, 2);
        pathRow.Children.Add(_browseButton);
        left.Children.Add(pathRow);

        left.Children.Add(_convertButton);
        left.Children.Add(_statusText);
        left.Children.Add(_parseErrorPanel);

        var leftCard = Card(left);
        row.Children.Add(leftCard);
        Grid.SetColumn(_previewCard, 1);
        row.Children.Add(_previewCard);
        return row;
    }

    private Border BuildPreviewCard()
    {
        var body = new StackPanel { Spacing = 8 };

        var overlayContent = new Grid();
        overlayContent.Children.Add(_previewImage);
        overlayContent.Children.Add(_previewThumbPlaceholder);
        var playHint = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            Background = Brush.Parse("#99000000"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "\u25b6",
                FontSize = 24,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0)
            }
        };
        overlayContent.Children.Add(playHint);
        _previewOverlay.Child = overlayContent;
        _previewOverlay.PointerPressed += async (_, e) =>
        {
            if (e.GetCurrentPoint(_previewOverlay).Properties.IsLeftButtonPressed
                && _previewPlayButton.IsEnabled)
            {
                await PlayOriginalVideoCoreAsync();
            }
        };

        var playerGrid = new Grid();
        if (EmbeddedPreviewEnabled)
        {
            playerGrid.Children.Add(_previewWebView);
        }
        playerGrid.Children.Add(_previewOverlay);
        _previewPlayerHost.Child = playerGrid;
        body.Children.Add(_previewPlayerHost);

        var actionRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        actionRow.Children.Add(_previewPlayButton);
        Grid.SetColumn(_previewStopButton, 2);
        actionRow.Children.Add(_previewStopButton);
        Grid.SetColumn(_previewBrowserButton, 3);
        actionRow.Children.Add(_previewBrowserButton);
        body.Children.Add(actionRow);

        body.Children.Add(_previewTitle);

        var meta = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 4
        };
        meta.Children.Add(_previewDuration);
        Grid.SetColumn(_previewViews, 1);
        meta.Children.Add(_previewViews);
        Grid.SetRow(_previewChannelFollowers, 1);
        meta.Children.Add(_previewChannelFollowers);
        Grid.SetRow(_previewDate, 1);
        Grid.SetColumn(_previewDate, 1);
        meta.Children.Add(_previewDate);
        body.Children.Add(meta);

        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        statusRow.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Green,
            VerticalAlignment = VerticalAlignment.Center
        });
        statusRow.Children.Add(_previewStatus);
        body.Children.Add(statusRow);

        return Card(body);
    }

    private Control BuildQueueAndUtilsRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,200"),
            ColumnSpacing = 10
        };

        var queueBody = new StackPanel { Spacing = 8 };
        var queueHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };
        queueHeader.Children.Add(_queueCountText);
        var refreshHint = new TextBlock
        {
            Text = "\u5373\u6642\u9032\u5ea6",
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(refreshHint, 1);
        queueHeader.Children.Add(refreshHint);
        Grid.SetColumn(_clearQueueButton, 2);
        queueHeader.Children.Add(_clearQueueButton);
        queueBody.Children.Add(queueHeader);
        queueBody.Children.Add(new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _downloadListPanel
        });

        var queueCard = Card(queueBody);
        row.Children.Add(queueCard);

        var utils = new StackPanel { Spacing = 8 };
        utils.Children.Add(new TextBlock
        {
            Text = "\u5be6\u7528\u529f\u80fd",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        });

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 8,
            ColumnSpacing = 8
        };
        grid.Children.Add(UtilButton(IconSearch, "\u641c\u5c0b\u5f71\u7247", () =>
        {
            OpenSection("search");
            SetStatus("\u53ef\u641c\u5c0b YouTube / Bilibili \u5f71\u7247");
            _searchBox.Focus();
        }));
        var sub = UtilButton(IconSubtitle, "\u5b57\u5e55\u642d\u914d", () =>
        {
            _includeSubtitles = !_includeSubtitles;
            _subtitleCheckBox.IsChecked = _includeSubtitles;
            SaveSettingsIfPossible();
            SetStatus(_includeSubtitles
                ? "\u5b57\u5e55\u642d\u914d\u5df2\u958b\uff1a.srt \u5916\u639b + MP4 \u5167\u5d4c / MP3 .lrc"
                : "\u5b57\u5e55\u642d\u914d\u5df2\u95dc");
        });
        Grid.SetColumn(sub, 1);
        grid.Children.Add(sub);
        var audio = UtilButton(IconMusic, "\u97f3\u6a02\u63d0\u53d6", () =>
        {
            SetOutputFormat("MP3");
            SetStatus("\u5df2\u5207\u63db MP3 \u97f3\u6a02\u63d0\u53d6");
        });
        Grid.SetRow(audio, 1);
        grid.Children.Add(audio);
        var batch = UtilButton(IconBatch, "\u6279\u91cf\u4e0b\u8f09", () =>
        {
            SetStatus("\u53ef\u5728\u7db2\u5740\u6b04\u8cbc\u591a\u884c\u7db2\u5740\uff08\u6bcf\u884c\u4e00\u500b\uff09\u5f8c\u958b\u59cb\u8f49\u63db");
            _urlBox.Focus();
        });
        Grid.SetRow(batch, 1);
        Grid.SetColumn(batch, 1);
        grid.Children.Add(batch);
        utils.Children.Add(grid);

        var utilsCard = Card(utils);
        Grid.SetColumn(utilsCard, 1);
        row.Children.Add(utilsCard);
        return row;
    }

    private Control BuildLogCard()
    {
        var body = new StackPanel { Spacing = 8 };

        var copyBtn = CreateIconTextButton("\u8907\u88fd\u5168\u90e8");
        copyBtn.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null || string.IsNullOrEmpty(_logText.Text))
                {
                    SetStatus("\u6c92\u6709\u53ef\u8907\u88fd\u7684\u8a18\u9304");
                    return;
                }

                await clipboard.SetTextAsync(_logText.Text);
                SetStatus("\u5df2\u8907\u88fd\u57f7\u884c\u8a18\u9304");
            }
            catch (Exception ex)
            {
                SetStatus("\u8907\u88fd\u5931\u6557");
                AppendLog(ex.Message);
            }
        };

        var logHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        logHeader.Children.Add(new TextBlock
        {
            Text = "\u57f7\u884c\u8a18\u9304",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(copyBtn, 1);
        logHeader.Children.Add(copyBtn);
        body.Children.Add(logHeader);

        body.Children.Add(new Border
        {
            Background = Brush.Parse("#111827"),
            BorderBrush = Brush.Parse("#273449"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6),
            MinHeight = 88,
            MaxHeight = 160,
            Child = _logText
        });
        return Card(body);
    }

    private Control BuildFooter()
    {
        var bar = new Border
        {
            Background = Brush.Parse("#F8FBFF"),
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 8)
        };

        var left = new TextBlock
        {
            Text = PlatformCopy.SupportedPlatformsLabel(),
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        right.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Green,
            VerticalAlignment = VerticalAlignment.Center
        });
        right.Children.Add(new TextBlock
        {
            Text = "\u5b89\u5168\u7121\u6bd2",
            FontSize = 12,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(_footerStats, 1);
        grid.Children.Add(_footerStats);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        bar.Child = grid;
        return bar;
    }

    private Border CreateFormatCard(string label, IBrush accent, IBrush softBg, Ellipse radio, bool selected)
    {
        var card = new Border
        {
            Background = selected ? softBg : BgCard,
            BorderBrush = selected ? accent : BorderSoft,
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            MinHeight = 48
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var glyph = VectorIcon(label == "MP4" ? IconVideo : IconMusic, accent, 18);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            Background = softBg,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            Child = glyph
        });
        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            Foreground = accent
        });
        labelStack.Children.Add(new TextBlock
        {
            Text = label == "MP4" ? "\u5f71\u7247" : "\u97f3\u8a0a",
            FontSize = 11,
            Foreground = TextMuted
        });
        Grid.SetColumn(labelStack, 1);
        row.Children.Add(labelStack);
        Grid.SetColumn(radio, 2);
        row.Children.Add(radio);
        card.Child = row;
        return card;
    }

    private static Ellipse CreateRadioDot(bool selected) => new()
    {
        Width = 18,
        Height = 18,
        Stroke = selected ? Blue : BorderSoft,
        StrokeThickness = selected ? 5 : 2,
        Fill = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Border UtilButton((string Stroke, string? Fill) icon, string label, Action onClick)
    {
        var border = new Border
        {
            Background = BlueSoft,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            MinHeight = 56
        };
        var stack = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var glyph = VectorIcon(icon, Blue, 20);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(glyph);
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        border.Child = stack;
        border.PointerPressed += (_, _) => onClick();
        border.PointerEntered += (_, _) => border.Background = Brush.Parse("#D9E8FF");
        border.PointerExited += (_, _) => border.Background = BlueSoft;
        return border;
    }

    // 24x24 line icons: Stroke is drawn as 2px rounded lines, Fill as solid shapes.
    private static readonly (string Stroke, string? Fill) IconSearch =
        ("M10.5,4 A6.5,6.5 0 1 1 10.5,17 A6.5,6.5 0 1 1 10.5,4 Z M15.5,15.5 L20,20", null);
    private static readonly (string Stroke, string? Fill) IconSubtitle =
        ("M5,5 H19 A2,2 0 0 1 21,7 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 V7 A2,2 0 0 1 5,5 Z M7,12 H10 M13,12 H17 M7,15.5 H15", null);
    private static readonly (string Stroke, string? Fill) IconMusic =
        ("M9,17.5 V5.5 L20,3.5 V15.5", "M6,15 A2.8,2.8 0 1 1 6,20.6 A2.8,2.8 0 1 1 6,15 Z M17,13 A2.8,2.8 0 1 1 17,18.6 A2.8,2.8 0 1 1 17,13 Z");
    private static readonly (string Stroke, string? Fill) IconBatch =
        ("M4,6 H14 M4,11 H14 M4,16 H10 M18,9 V19 M14.5,15.5 L18,19 L21.5,15.5", null);
    private static readonly (string Stroke, string? Fill) IconVideo =
        ("M4,6 H14 A2,2 0 0 1 16,8 V16 A2,2 0 0 1 14,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M16,10.5 L21.5,7.5 V16.5 L16,13.5", "M7.5,9.5 L11.5,12 L7.5,14.5 Z");

    private static Control VectorIcon((string Stroke, string? Fill) icon, IBrush brush, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(icon.Stroke),
            Stroke = brush,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        });
        if (icon.Fill is not null)
        {
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(icon.Fill),
                Fill = brush
            });
        }

        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static Border PlatformChip(string text, IBrush fg, IBrush bg) => new()
    {
        Background = bg,
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(10, 3),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = fg
        }
    };

    private static Border Card(Control child) => new()
    {
        Background = BgCard,
        BorderBrush = BorderSoft,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14, 12),
        Child = child
    };

    private static TextBox CreateInputBox(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        FontSize = 14,
        MinHeight = 40,
        Padding = new Thickness(12, 8)
    };

    private static Button CreatePrimaryButton(string text, double minWidth) => new()
    {
        Content = text,
        MinWidth = minWidth,
        MinHeight = 36,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.White,
        Background = Blue,
        CornerRadius = new CornerRadius(10),
        Cursor = new Cursor(StandardCursorType.Hand),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(12, 6)
    };

    private static Button CreateSoftButton(string text, double minWidth) => new()
    {
        Content = text,
        MinWidth = minWidth,
        MinHeight = 36,
        FontSize = 13,
        Foreground = TextPrimary,
        Background = BlueSoft,
        BorderBrush = BorderSoft,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Cursor = new Cursor(StandardCursorType.Hand),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(10, 6)
    };

    private static Button CreateIconTextButton(string label) => new()
    {
        Content = label,
        MinHeight = 30,
        FontSize = 12,
        Foreground = TextSecondary,
        Background = Brushes.Transparent,
        Cursor = new Cursor(StandardCursorType.Hand),
        Padding = new Thickness(8, 4)
    };

    private static TextBlock MetaLine(string label, string text) => new()
    {
        Text = $"{label}:  {text}",
        FontSize = 12,
        Foreground = TextSecondary
    };

    private void SetOutputFormat(string format)
    {
        _outputFormat = format is "MP4" or "MP3" ? format : "MP4";
        var mp4 = _outputFormat == "MP4";

        _mp4Card.Background = mp4 ? BlueSoft : BgCard;
        _mp4Card.BorderBrush = mp4 ? Blue : BorderSoft;
        _mp4Card.BorderThickness = new Thickness(mp4 ? 2 : 1);
        _mp4Radio.Stroke = mp4 ? Blue : BorderSoft;
        _mp4Radio.StrokeThickness = mp4 ? 5 : 2;

        _mp3Card.Background = !mp4 ? GreenSoft : BgCard;
        _mp3Card.BorderBrush = !mp4 ? Green : BorderSoft;
        _mp3Card.BorderThickness = new Thickness(!mp4 ? 2 : 1);
        _mp3Radio.Stroke = !mp4 ? Green : BorderSoft;
        _mp3Radio.StrokeThickness = !mp4 ? 5 : 2;

        _qualityCombo.IsEnabled = mp4;
        SaveSettingsIfPossible();
    }

    private async void PasteUrlAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetStatus("\u7121\u6cd5\u5b58\u53d6\u526a\u8cbc\u7c3f");
                return;
            }

            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("\u526a\u8cbc\u7c3f\u662f\u7a7a\u7684");
                return;
            }

            _urlBox.Text = text.Trim();
            SetStatus("\u5df2\u8cbc\u4e0a\u7db2\u5740\uff0c\u6b63\u5728\u89e3\u6790\u9810\u89bd...");
            if (GetInputUrls().Length > 0)
            {
                await ParseUrlCoreAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus("\u8cbc\u4e0a\u5931\u6557");
            AppendLog(ex.Message);
        }
    }

    private async void ChooseFolderAsync(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "\u9078\u64c7\u8f38\u51fa\u8cc7\u6599\u593e",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is not null && folder.TryGetLocalPath() is { } path)
        {
            _outputBox.Text = path;
            SaveSettingsIfPossible();
            SetStatus("\u8f38\u51fa\u8cc7\u6599\u593e\u5df2\u66f4\u65b0");
            RefreshShellChrome();
        }
    }

    private async void ChooseCookiesFileAsync(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "\u9078\u64c7 cookies.txt \u6a94\u6848",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Cookie Files") { Patterns = ["*.txt"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is not null && file.TryGetLocalPath() is { } path)
        {
            _cookiesFilePath = path;
            _cookiesPathLabel.Text = IoPath.GetFileName(path);
            _cookiesPathLabel.Foreground = TextPrimary;
            SaveSettingsIfPossible();
            SetStatus($"\u5df2\u532f\u5165 Cookies: {IoPath.GetFileName(path)}");
            AppendLog($"Cookies file: {path}");
            RefreshShellChrome();
        }
    }

    private async void ParseUrlAsync(object? sender, RoutedEventArgs e) => await ParseUrlCoreAsync();

    private async Task SearchVideosCoreAsync()
    {
        var query = (_searchBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchStatusText.Text = "\u8acb\u8f38\u5165\u641c\u5c0b\u95dc\u9375\u5b57";
            _searchStatusText.Foreground = Brush.Parse("#EF4444");
            SetStatus("\u8acb\u8f38\u5165\u641c\u5c0b\u95dc\u9375\u5b57");
            return;
        }

        _searchTokenSource?.Cancel();
        _searchTokenSource?.Dispose();
        _searchTokenSource = new CancellationTokenSource();
        var token = _searchTokenSource.Token;

        _searchButton.IsEnabled = false;
        _searchStatusText.Text = "\u641c\u5c0b\u4e2d...";
        _searchStatusText.Foreground = TextSecondary;
        SetStatus($"\u6b63\u5728\u641c\u5c0b\uff1a{query}");
        AppendLog($"\u641c\u5c0b [{_searchPlatform}]: {query}");

        // Record the query as soon as the user starts a search.
        RememberRecentSearch(query, _searchPlatform);

        _searchResults.Clear();
        _searchResultsPanel.Children.Clear();
        _searchResultsPanel.Children.Add(new TextBlock
        {
            Text = "\u6b63\u5728\u641c\u5c0b\uff0c\u8acb\u7a0d\u5019...",
            FontSize = 13,
            Foreground = TextMuted
        });

        try
        {
            var tasks = new List<Task<IReadOnlyList<SearchVideoResult>>>();
            if (_searchPlatform is "youtube" or "both")
            {
                tasks.Add(SearchYouTubeAsync(query, _searchResultLimit, token));
            }

            if (_searchPlatform is "bilibili" or "both")
            {
                tasks.Add(SearchBilibiliAsync(query, _searchResultLimit, token));
            }

            var batches = await Task.WhenAll(tasks);
            token.ThrowIfCancellationRequested();

            // Preserve each platform's original search ranking (relevance).
            // Do NOT re-sort by view count — that pushes popular but off-topic videos to the top.
            // Also require the full keyword to appear in title or description.
            var ordered = new List<SearchVideoResult>();
            var youtube = new List<SearchVideoResult>();
            var bilibili = new List<SearchVideoResult>();
            var droppedOffTopic = 0;
            foreach (var batch in batches)
            {
                foreach (var item in batch)
                {
                    if (!IsUsableSearchResult(item))
                    {
                        continue;
                    }

                    if (!MatchesSearchKeyword(item, query))
                    {
                        droppedOffTopic++;
                        continue;
                    }

                    if (item.Platform.Equals("Bilibili", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bilibili.Count < _searchResultLimit)
                        {
                            bilibili.Add(item);
                        }
                    }
                    else if (youtube.Count < _searchResultLimit)
                    {
                        youtube.Add(item);
                    }
                }
            }

            // When both platforms are searched, interleave by rank so the top
            // relevant hit from each side appears early (YT1, Bili1, YT2, Bili2...).
            if (youtube.Count > 0 && bilibili.Count > 0)
            {
                var max = Math.Max(youtube.Count, bilibili.Count);
                for (var i = 0; i < max; i++)
                {
                    if (i < youtube.Count)
                    {
                        ordered.Add(youtube[i]);
                    }

                    if (i < bilibili.Count)
                    {
                        ordered.Add(bilibili[i]);
                    }
                }
            }
            else
            {
                ordered.AddRange(youtube);
                ordered.AddRange(bilibili);
            }

            _searchResults.AddRange(ordered);

            RebuildSearchResultsPanel();

            if (_searchResults.Count == 0)
            {
                _searchStatusText.Text = droppedOffTopic > 0
                    ? $"\u6c92\u6709\u6a19\u984c/\u4ecb\u7d39\u542b\u300c{query}\u300d\u7684\u5f71\u7247\uff08\u5df2\u904e\u6ffe {droppedOffTopic} \u7b46\u4e0d\u76f8\u95dc\uff09"
                    : "\u627e\u4e0d\u5230\u76f8\u95dc\u5f71\u7247\uff0c\u8acb\u63db\u500b\u95dc\u9375\u5b57\u6216\u5e73\u53f0";
                _searchStatusText.Foreground = Brush.Parse("#F59E0B");
                SetStatus("\u641c\u5c0b\u7121\u7d50\u679c");
            }
            else
            {
                var ytCount = _searchResults.Count(r => r.Platform == "YouTube");
                var biliCount = _searchResults.Count(r => r.Platform == "Bilibili");
                var filterHint = droppedOffTopic > 0
                    ? $"\uff0c\u5df2\u904e\u6ffe {droppedOffTopic} \u7b46\u4e0d\u542b\u95dc\u9375\u5b57"
                    : "";
                _searchStatusText.Text =
                    $"\u627e\u5230 {_searchResults.Count} \u7b46\u7d50\u679c\uff08YouTube {ytCount} \u00b7 Bilibili {biliCount}{filterHint}\uff09\n\u50c5\u986f\u793a\u6a19\u984c\u6216\u4ecb\u7d39\u542b\u300c{query}\u300d\u7684\u5f71\u7247";
                _searchStatusText.Foreground = Green;
                SetStatus($"\u641c\u5c0b\u5b8c\u6210\uff1a{_searchResults.Count} \u7b46\uff08\u5df2\u904e\u6ffe\u4e0d\u76f8\u95dc\uff09");
                AppendLog($"\u641c\u5c0b\u5b8c\u6210: YT={ytCount}, Bili={biliCount}, dropped={droppedOffTopic}");
            }
        }
        catch (OperationCanceledException)
        {
            _searchStatusText.Text = "\u641c\u5c0b\u5df2\u53d6\u6d88";
            _searchStatusText.Foreground = TextMuted;
            SetStatus("\u641c\u5c0b\u5df2\u53d6\u6d88");
        }
        catch (Exception ex)
        {
            _searchStatusText.Text = $"\u641c\u5c0b\u5931\u6557\uff1a{ex.Message}";
            _searchStatusText.Foreground = Brush.Parse("#EF4444");
            SetStatus("\u641c\u5c0b\u5931\u6557");
            AppendLog($"\u641c\u5c0b\u932f\u8aa4: {ex.Message}");
            _searchResultsPanel.Children.Clear();
            _searchResultsPanel.Children.Add(new TextBlock
            {
                Text = "\u641c\u5c0b\u6642\u767c\u751f\u932f\u8aa4\uff0c\u8acb\u6aa2\u67e5\u7db2\u8def\u6216 yt-dlp\u3002",
                FontSize = 13,
                Foreground = Brush.Parse("#EF4444")
            });
        }
        finally
        {
            _searchButton.IsEnabled = true;
        }
    }

    private void RebuildSearchResultsPanel()
    {
        _searchResultsPanel.Children.Clear();
        if (_searchResults.Count == 0)
        {
            _searchResultsPanel.Children.Add(new TextBlock
            {
                Text = "\u6c92\u6709\u7b26\u5408\u7684\u5f71\u7247\u3002",
                FontSize = 13,
                Foreground = TextMuted
            });
            return;
        }

        foreach (var item in _searchResults)
        {
            _searchResultsPanel.Children.Add(BuildSearchResultCard(item));
        }
    }

    private void RememberRecentSearch(string query, string platform)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        platform = NormalizeSearchPlatform(platform);
        _recentSearches.RemoveAll(e =>
            string.Equals(e.Query, query, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Platform, platform, StringComparison.OrdinalIgnoreCase));
        _recentSearches.Insert(0, new RecentSearchEntry(query, platform, DateTime.UtcNow));
        while (_recentSearches.Count > MaxRecentSearches)
        {
            _recentSearches.RemoveAt(_recentSearches.Count - 1);
        }

        RebuildSearchHistoryPanel();
        SaveSettingsIfPossible();
    }

    private void RebuildSearchHistoryPanel()
    {
        _searchHistoryPanel.Children.Clear();
        DetachFromParent(_searchHistoryEmptyText);
        _clearSearchHistoryButton.IsEnabled = _recentSearches.Count > 0;

        if (_recentSearches.Count == 0)
        {
            _searchHistoryPanel.Children.Add(_searchHistoryEmptyText);
            return;
        }

        foreach (var entry in _recentSearches)
        {
            _searchHistoryPanel.Children.Add(BuildRecentSearchChip(entry));
        }
    }

    private Control BuildRecentSearchChip(RecentSearchEntry entry)
    {
        var platformLabel = entry.Platform switch
        {
            "youtube" => "YT",
            "bilibili" => "Bili",
            _ => "YT+Bili"
        };
        var accent = entry.Platform switch
        {
            "youtube" => RedYouTube,
            "bilibili" => PinkBili,
            _ => Blue
        };
        var soft = entry.Platform switch
        {
            "youtube" => Brush.Parse("#FFECEC"),
            "bilibili" => PinkBiliSoft,
            _ => BlueSoft
        };

        var chip = new Border
        {
            Background = soft,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 0, 0)
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        row.Children.Add(new TextBlock
        {
            Text = platformLabel,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = entry.Query,
            FontSize = 12,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        chip.Child = row;

        var snap = entry;
        chip.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
            {
                return;
            }

            e.Handled = true;
            _searchBox.Text = snap.Query;
            ApplySearchPlatformToCombo(snap.Platform);
            SetStatus($"\u5df2\u9078\u7528\u641c\u5c0b\u7d00\u9304\uff1a{snap.Query}");
            await SearchVideosCoreAsync();
        };

        // Right-click / secondary: remove single entry.
        chip.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Right)
            {
                return;
            }

            e.Handled = true;
            _recentSearches.RemoveAll(x =>
                string.Equals(x.Query, snap.Query, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Platform, snap.Platform, StringComparison.OrdinalIgnoreCase));
            RebuildSearchHistoryPanel();
            SaveSettingsIfPossible();
            SetStatus($"\u5df2\u79fb\u9664\u7d00\u9304\uff1a{snap.Query}");
        };

        ToolTip.SetTip(chip, $"{entry.Query}\n\u5e73\u53f0: {platformLabel}\n\u5de6\u9375\u91cd\u641c\uff0c\u53f3\u9375\u79fb\u9664");
        return chip;
    }

    private void ApplySearchPlatformToCombo(string platform)
    {
        _searchPlatform = NormalizeSearchPlatform(platform);
        _searchPlatformCombo.SelectedIndex = _searchPlatform switch
        {
            "youtube" => 1,
            "bilibili" => 2,
            _ => 0
        };
    }

    private static string NormalizeSearchPlatform(string? platform) =>
        platform?.Trim().ToLowerInvariant() switch
        {
            "youtube" or "yt" => "youtube",
            "bilibili" or "bili" => "bilibili",
            _ => "both"
        };

    private Control BuildSearchResultCard(SearchVideoResult item)
    {
        var isYouTube = item.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase);
        var accent = isYouTube ? RedYouTube : PinkBili;
        var soft = isYouTube ? Brush.Parse("#FFECEC") : PinkBiliSoft;

        var root = new Border
        {
            Background = BgCard,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            ColumnSpacing = 12
        };

        var thumbHost = new Border
        {
            Width = 120,
            Height = 68,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = Brush.Parse("#0F172A"),
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1)
        };
        var thumbPlaceholder = new TextBlock
        {
            Text = isYouTube ? "YT" : "Bili",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var thumbImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsVisible = false
        };
        var thumbLayer = new Grid();
        thumbLayer.Children.Add(thumbPlaceholder);
        thumbLayer.Children.Add(thumbImage);
        thumbHost.Child = thumbLayer;
        grid.Children.Add(thumbHost);

        if (!string.IsNullOrWhiteSpace(item.ThumbnailUrl))
        {
            _ = LoadSearchThumbnailAsync(item.ThumbnailUrl, thumbImage, thumbPlaceholder, isYouTube);
        }

        var body = new StackPanel { Spacing = 6 };

        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        titleRow.Children.Add(PlatformChip(item.Platform, accent, soft));
        titleRow.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        // put title in column 1
        Grid.SetColumn(titleRow.Children[^1], 1);
        body.Children.Add(titleRow);

        var meta = $"{item.Uploader ?? "-"}  ·  {FormatDuration(item.DurationSeconds)}  ·  {FormatCount(item.ViewCount)} \u6b21\u89c0\u770b";
        body.Children.Add(new TextBlock
        {
            Text = meta,
            FontSize = 11,
            Foreground = TextMuted,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        body.Children.Add(new TextBlock
        {
            Text = item.Url,
            FontSize = 11,
            Foreground = TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var useBtn = CreateSoftButton("\u4f7f\u7528\u7db2\u5740", 96);
        useBtn.MinHeight = 34;
        useBtn.Click += (_, e) =>
        {
            e.Handled = true;
            ScheduleSearchResultAction(item, parse: false, convert: false);
        };

        var parseBtn = CreatePrimaryButton("\u89e3\u6790\u9810\u89bd", 100);
        parseBtn.MinHeight = 34;
        parseBtn.Click += (_, e) =>
        {
            e.Handled = true;
            ScheduleSearchResultAction(item, parse: true, convert: false);
        };

        var convertBtn = new Button
        {
            Content = "\u958b\u59cb\u8f49\u63db",
            MinWidth = 100,
            MinHeight = 34,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Background = Green,
            CornerRadius = new CornerRadius(10),
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(12, 6)
        };
        convertBtn.Click += (_, e) =>
        {
            e.Handled = true;
            ScheduleSearchResultAction(item, parse: false, convert: true);
        };

        var openBtn = CreateSoftButton("\u539f\u9801", 72);
        openBtn.MinHeight = 34;
        openBtn.Click += (_, e) =>
        {
            e.Handled = true;
            OpenOriginalPage(item.Url);
        };

        actions.Children.Add(useBtn);
        actions.Children.Add(parseBtn);
        actions.Children.Add(convertBtn);
        actions.Children.Add(openBtn);
        body.Children.Add(actions);

        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        root.Child = grid;

        // Click the card body (not a button) => fill URL and go home for convert/parse.
        root.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left
                && e.Source is Control source
                && !IsDescendantOfButton(source, root))
            {
                e.Handled = true;
                ScheduleSearchResultAction(item, parse: false, convert: false);
            }
        };

        return root;
    }

    private static bool IsDescendantOfButton(Control source, Control root)
    {
        Control? current = source;
        while (current is not null && !ReferenceEquals(current, root))
        {
            if (current is Button)
            {
                return true;
            }

            current = current.Parent as Control;
        }

        return false;
    }

    /// <summary>
    /// Defer page navigation until after the current input event finishes.
    /// Navigating (and tearing down the result card) inside the click handler
    /// can crash Avalonia / native controls.
    /// </summary>
    private void ScheduleSearchResultAction(SearchVideoResult item, bool parse, bool convert)
    {
        var snapshot = item;
        // Input priority runs soon after the pointer/click event completes,
        // without waiting for idle Background work that may never flush while UI is busy.
        Dispatcher.UIThread.Post(
            () => _ = ApplySearchResultActionAsync(snapshot, parse, convert),
            DispatcherPriority.Input);
    }

    private async Task ApplySearchResultActionAsync(SearchVideoResult item, bool parse, bool convert)
    {
        try
        {
            ApplySearchSelection(item);

            // Search lives on the same page; jump back up to the URL / preview area.
            ScrollToTop();
            ApplySeededPreviewUi(item);
            SetStatus($"\u5df2\u9078\u7528\uff1a{item.Title}");
            AppendLog($"\u641c\u5c0b\u9078\u7528 [{item.Platform}]: {item.Url}");
            AppendLog($"\u7db2\u5740: {item.Url}");

            // Yield one frame so home layout is attached before long-running work.
            await Task.Yield();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // Confirm URL landed in the home input.
            if (string.IsNullOrWhiteSpace(_urlBox.Text))
            {
                _urlBox.Text = item.Url;
            }

            if (parse)
            {
                await ParseUrlCoreAsync();
            }

            if (convert)
            {
                await ConvertOrCancelCoreAsync();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\u641c\u5c0b\u9078\u7528\u5931\u6557: {ex}");
            SetStatus("\u64cd\u4f5c\u5931\u6557\uff0c\u8acb\u91cd\u8a66");
            try
            {
                // Last-resort: still put the URL in the box even if navigation failed.
                _urlBox.Text = item.Url;
                ScrollToTop();
            }
            catch
            {
                // ignore secondary failures
            }
        }
    }

    private void ApplySearchSelection(SearchVideoResult item)
    {
        _urlBox.Text = item.Url;
        // Seed lightweight metadata so the download queue shows a real title
        // even before the user runs full URL parsing.
        _parsedInfo = new ParsedVideoInfo(
            Title: item.Title,
            DurationSeconds: item.DurationSeconds,
            ViewCount: item.ViewCount,
            ChannelFollowerCount: null,
            UploadDate: null,
            Url: item.Url,
            ThumbnailUrl: item.ThumbnailUrl,
            WebpageUrl: item.Url,
            VideoId: item.VideoId,
            ExtractorKey: item.Platform.Equals("Bilibili", StringComparison.OrdinalIgnoreCase) ? "BiliBili" : "Youtube",
            ChannelName: item.Uploader);
    }

    private void ApplySeededPreviewUi(SearchVideoResult item)
    {
        try
        {
            _previewTitle.Text = item.Title;
            _previewDuration.Text = $"\u6642\u9577:  {FormatDuration(item.DurationSeconds)}";
            _previewViews.Text = $"\u6b21\u6578:  {FormatCount(item.ViewCount)}";
            _previewChannelFollowers.Text = $"\u983b\u9053:  {item.Uploader ?? "-"}";
            _previewDate.Text = "\u65e5\u671f:  -";
            _previewStatus.Text = "\u5df2\u5f9e\u641c\u5c0b\u9078\u7528\uff0c\u53ef\u89e3\u6790\u6216\u8f49\u63db";
            _previewStatus.Foreground = Blue;
            _previewPlayButton.IsEnabled = true;
            _previewBrowserButton.IsEnabled = true;
            _ = LoadPreviewThumbnailAsync(item.ThumbnailUrl);
        }
        catch (Exception ex)
        {
            AppendLog($"\u9810\u89bd\u8cc7\u8a0a\u66f4\u65b0\u5931\u6557: {ex.Message}");
        }
    }

    private async Task LoadSearchThumbnailAsync(
        string thumbnailUrl,
        Image image,
        TextBlock placeholder,
        bool isYouTube)
    {
        try
        {
            var resolvedUrl = NormalizeThumbnailUrl(thumbnailUrl);
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return;
            }

            var bytes = await AppHttp.GetBytesAsync(
                resolvedUrl,
                isYouTube ? null : "https://www.bilibili.com/");
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                image.Source = bitmap;
                image.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }
        catch
        {
            // Thumbnail is optional; keep placeholder.
        }
    }

    private async Task<IReadOnlyList<SearchVideoResult>> SearchYouTubeAsync(
        string query,
        int limit,
        CancellationToken token)
    {
        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        if (ytDlpPath is null)
        {
            AppendLog("YouTube \u641c\u5c0b\u5931\u6557: \u627e\u4e0d\u5230 yt-dlp");
            return [];
        }

        limit = Math.Clamp(limit, 1, 50);
        // Over-fetch so keyword filtering still leaves enough hits.
        var fetchLimit = Math.Clamp(limit * 3, limit, 50);
        var searchUrl = $"ytsearch{fetchLimit}:{query}";
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        startInfo.ArgumentList.Add(searchUrl);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return [];
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AppendLog($"YouTube \u641c\u5c0b\u932f\u8aa4: {stderr.Trim()}");
            }

            return [];
        }

        return ParseYtDlpSearchJson(stdout, "YouTube");
    }

    private async Task<IReadOnlyList<SearchVideoResult>> SearchBilibiliAsync(
        string query,
        int limit,
        CancellationToken token)
    {
        limit = Math.Clamp(limit, 1, 50);
        var fetchLimit = Math.Clamp(limit * 3, limit, 50);

        // Prefer official search API (more reliable than bilisearch when anti-bot is active).
        try
        {
            var apiResults = await SearchBilibiliApiAsync(query, fetchLimit, token);
            if (apiResults.Count > 0)
            {
                return apiResults;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendLog($"Bilibili API \u641c\u5c0b\u5931\u6557: {ex.Message}");
        }

        // Fallback: yt-dlp bilisearch (may fail with HTTP 412 in some networks).
        return await SearchBilibiliViaYtDlpAsync(query, fetchLimit, token);
    }

    private async Task<IReadOnlyList<SearchVideoResult>> SearchBilibiliApiAsync(
        string query,
        int limit,
        CancellationToken token)
    {
        var pageSize = Math.Clamp(limit, 1, 50);
        var url =
            "https://api.bilibili.com/x/web-interface/search/type"
            + "?search_type=video"
            + $"&keyword={Uri.EscapeDataString(query)}"
            + "&page=1"
            + $"&page_size={pageSize}"
            + "&order=totalrank";

        using var request = AppHttp.CreateGet(
            url,
            referer: "https://search.bilibili.com",
            origin: "https://search.bilibili.com");
        using var response = await AppHttp.Shared.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            AppendLog($"Bilibili API HTTP {(int)response.StatusCode}: {TruncateForLog(body, 200)}");
            return [];
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            AppendLog($"Bilibili API code error: {msg}");
            return [];
        }

        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<SearchVideoResult>();
        foreach (var entry in result.EnumerateArray())
        {
            if (entry.TryGetProperty("type", out var typeEl))
            {
                var type = typeEl.GetString();
                if (!string.IsNullOrWhiteSpace(type)
                    && !type.Equals("video", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var bvid = entry.TryGetProperty("bvid", out var bv) ? bv.GetString() : null;
            if (string.IsNullOrWhiteSpace(bvid))
            {
                continue;
            }

            var titleRaw = entry.TryGetProperty("title", out var t) ? t.GetString() : null;
            var title = StripHtml(titleRaw) ?? bvid;
            var descRaw = entry.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(descRaw) && entry.TryGetProperty("desc", out var desc2))
            {
                descRaw = desc2.GetString();
            }

            var description = StripHtml(descRaw);
            var author = entry.TryGetProperty("author", out var a) ? a.GetString() : null;
            long? views = null;
            if (entry.TryGetProperty("play", out var play))
            {
                views = play.ValueKind == JsonValueKind.Number
                    ? play.GetInt64()
                    : long.TryParse(play.GetString(), out var p) ? p : null;
            }

            double? duration = null;
            if (entry.TryGetProperty("duration", out var durEl))
            {
                duration = ParseDurationText(durEl.GetString());
            }

            var pic = entry.TryGetProperty("pic", out var picEl) ? picEl.GetString() : null;
            var webpage = $"https://www.bilibili.com/video/{bvid}/";

            list.Add(new SearchVideoResult(
                Platform: "Bilibili",
                Title: title,
                Url: webpage,
                Uploader: author,
                DurationSeconds: duration,
                ViewCount: views,
                ThumbnailUrl: NormalizeThumbnailUrl(pic),
                VideoId: bvid,
                Description: description));

            if (list.Count >= limit)
            {
                break;
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<SearchVideoResult>> SearchBilibiliViaYtDlpAsync(
        string query,
        int limit,
        CancellationToken token)
    {
        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        if (ytDlpPath is null)
        {
            return [];
        }

        limit = Math.Clamp(limit, 1, 50);
        var searchUrl = $"bilisearch{limit}:{query}";
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        // Reuse the same browser-like headers used for video pages.
        startInfo.ArgumentList.Add("--add-headers");
        startInfo.ArgumentList.Add("User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        startInfo.ArgumentList.Add("--add-headers");
        startInfo.ArgumentList.Add("Referer:https://www.bilibili.com/");
        startInfo.ArgumentList.Add("--add-headers");
        startInfo.ArgumentList.Add("Accept-Language:zh-CN,zh-TW;q=0.9,zh;q=0.8,en;q=0.7");
        startInfo.ArgumentList.Add(searchUrl);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return [];
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AppendLog($"Bilibili yt-dlp \u641c\u5c0b\u932f\u8aa4: {stderr.Trim()}");
            }

            return [];
        }

        return ParseYtDlpSearchJson(stdout, "Bilibili");
    }

    private static List<SearchVideoResult> ParseYtDlpSearchJson(string stdout, string platform)
    {
        var list = new List<SearchVideoResult>();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                // Single result fallback.
                if (TryMapSearchEntry(root, platform) is { } single)
                {
                    list.Add(single);
                }

                return list;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (TryMapSearchEntry(entry, platform) is { } item)
                {
                    list.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            // Caller logs at higher level if needed; keep silent here.
            _ = ex;
        }

        return list;
    }

    private static SearchVideoResult? TryMapSearchEntry(JsonElement entry, string platform)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var title = entry.TryGetProperty("title", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string? url = null;
        if (entry.TryGetProperty("webpage_url", out var wu))
        {
            url = wu.GetString();
        }

        if (string.IsNullOrWhiteSpace(url) && entry.TryGetProperty("url", out var u))
        {
            url = u.GetString();
        }

        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(id))
        {
            url = platform.Equals("Bilibili", StringComparison.OrdinalIgnoreCase)
                ? (id.StartsWith("BV", StringComparison.OrdinalIgnoreCase)
                    ? $"https://www.bilibili.com/video/{id}/"
                    : $"https://www.bilibili.com/video/av{id}/")
                : $"https://www.youtube.com/watch?v={id}";
        }

        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Flat playlist entries sometimes put the raw id into url without scheme.
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var uploader = entry.TryGetProperty("uploader", out var up) ? up.GetString() : null;
        if (string.IsNullOrWhiteSpace(uploader) && entry.TryGetProperty("channel", out var ch))
        {
            uploader = ch.GetString();
        }

        double? duration = entry.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetDouble()
            : null;
        long? views = entry.TryGetProperty("view_count", out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64()
            : null;

        string? thumb = null;
        if (entry.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
        {
            foreach (var th in thumbs.EnumerateArray())
            {
                if (th.TryGetProperty("url", out var thUrl) && thUrl.GetString() is { Length: > 0 } rawThumb)
                {
                    thumb = rawThumb;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(thumb) && entry.TryGetProperty("thumbnail", out var singleThumb))
        {
            thumb = singleThumb.GetString();
        }

        string? description = null;
        if (entry.TryGetProperty("description", out var descEl))
        {
            description = descEl.GetString();
        }

        if (string.IsNullOrWhiteSpace(description) && entry.TryGetProperty("desc", out var desc2))
        {
            description = desc2.GetString();
        }

        description = StripHtml(description);

        return new SearchVideoResult(
            Platform: platform,
            Title: title ?? id ?? "\u672a\u77e5\u6a19\u984c",
            Url: url,
            Uploader: uploader,
            DurationSeconds: duration,
            ViewCount: views,
            ThumbnailUrl: NormalizeThumbnailUrl(thumb),
            VideoId: id,
            Description: description);
    }

    private static string? NormalizeThumbnailUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return "https://" + url.TrimStart('/');
    }

    private static string? StripHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Bilibili search titles wrap keywords in <em class="keyword">...</em>
        var noTags = Regex.Replace(text, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(noTags).Trim();
    }

    private static double? ParseDurationText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        try
        {
            if (parts.Length == 2
                && int.TryParse(parts[0], out var m)
                && int.TryParse(parts[1], out var s))
            {
                return m * 60 + s;
            }

            if (parts.Length == 3
                && int.TryParse(parts[0], out var h)
                && int.TryParse(parts[1], out var m2)
                && int.TryParse(parts[2], out var s2))
            {
                return h * 3600 + m2 * 60 + s2;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string FormatCount(long? value) =>
        value is null ? "-" : value.Value.ToString("N0");

    private static int PlatformRank(string platform) =>
        platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ? 0
        : platform.Equals("Bilibili", StringComparison.OrdinalIgnoreCase) ? 1
        : 2;

    private static bool IsUsableSearchResult(SearchVideoResult item)
    {
        if (string.IsNullOrWhiteSpace(item.Url)
            || !item.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            return false;
        }

        // Drop playlist/meta shells that sometimes leak into flat-search entries.
        if (item.Url.Contains("ytsearch", StringComparison.OrdinalIgnoreCase)
            || item.Url.Contains("bilisearch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Title or description must contain the full keyword phrase
    /// (e.g. 「煙とブルー」 — not just a loose related word like 「煙」).
    /// </summary>
    private static bool MatchesSearchKeyword(SearchVideoResult item, string query)
    {
        var keyword = NormalizeSearchText(query);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var title = NormalizeSearchText(item.Title);
        var description = NormalizeSearchText(item.Description);
        var haystack = $"{title}\n{description}";

        return haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        // Decode HTML entities / strip tags if any remain.
        var cleaned = StripHtml(text) ?? text;
        cleaned = cleaned
            .Replace('\u3000', ' ') // ideographic space
            .Replace('\u00A0', ' ')
            .Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned;
    }

    private async Task ParseUrlCoreAsync()
    {
        var urls = GetInputUrls();
        if (urls.Length == 0)
        {
            HideParseError();
            SetStatus("\u8acb\u5148\u8cbc\u4e0a YouTube \u6216 Bilibili \u7db2\u5740");
            return;
        }

        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        if (ytDlpPath is null)
        {
            ShowParseError("\u7f3a\u5c11 yt-dlp\uff0c\u56e0\u6b64\u7121\u6cd5\u8b80\u53d6\u5f71\u7247\u8cc7\u8a0a\u3002\u8acb\u4f9d\u756b\u9762\u4e0a\u65b9\u300c\u5b89\u88dd\u914d\u5957\u5de5\u5177\u300d\u7684\u6b65\u9a5f\u5b89\u88dd\uff0c\u88dd\u597d\u5f8c\u4e0d\u9700\u91cd\u555f\u7a0b\u5f0f\u3002");
            SetStatus("\u89e3\u6790\u5931\u6557\uff1a\u627e\u4e0d\u5230 yt-dlp");
            AppendInstallHint();
            return;
        }

        _parseButton.IsEnabled = false;
        _previewPlayButton.IsEnabled = false;
        _previewStopButton.IsEnabled = false;
        _previewBrowserButton.IsEnabled = false;
        StopEmbeddedPreview(clearStatus: false);
        HideParseError();
        _lastParseErrorDetail = null;
        SetStatus("\u6b63\u5728\u89e3\u6790\u7db2\u5740...");
        AppendLog($"\u89e3\u6790: {urls[0]}");

        try
        {
            var info = IsYouTubeChannelUrl(urls[0])
                ? await DumpChannelInfoAsync(ytDlpPath, urls[0])
                : await DumpVideoInfoAsync(ytDlpPath, urls[0]);
            if (info is null)
            {
                var detail = _lastParseErrorDetail
                    ?? "\u7121\u6cd5\u8b80\u53d6\u6b64\u7db2\u5740\u3002\u8acb\u78ba\u8a8d\u539f\u9801\u4ecd\u53ef\u64ad\u653e\uff1b\u82e5\u662f\u6703\u54e1\u6216\u767b\u5165\u9650\u5236\u5167\u5bb9\uff0c\u8acb\u532f\u5165 cookies.txt \u5f8c\u91cd\u8a66\u3002";
                SetStatus("\u89e3\u6790\u5931\u6557\uff1a\u8acb\u67e5\u770b\u4e0b\u65b9\u8655\u7406\u65b9\u5f0f");
                ShowParseError(detail);
                _parsedInfo = null;
                _previewTitle.Text = "\u7121\u6cd5\u53d6\u5f97\u5f71\u7247\u8cc7\u8a0a";
                _previewStatus.Text = "\u8acb\u4f9d\u5de6\u5074\u63d0\u793a\u8655\u7406\u5f8c\u91cd\u8a66";
                _previewStatus.Foreground = Brush.Parse("#B42318");
                ClearPreviewThumbnail();
                return;
            }

            _parsedInfo = info;
            _previewTitle.Text = info.Title;
            _previewDuration.Text = $"\u6642\u9577:  {FormatDuration(info.DurationSeconds)}";
            if (info.IsPreviewOnly && info.AvailableDurationSeconds is not null)
            {
                _previewDuration.Text +=
                    $"  (\u53ef\u4e0b\u8f09\u7d04 {FormatDuration(info.AvailableDurationSeconds)})";
            }

            _previewViews.Text = info.IsChannel
                ? "\u5f71\u7247\u89c0\u770b:  -"
                : $"\u6b21\u6578:  {info.ViewCount?.ToString("N0") ?? "-"}";
            _previewChannelFollowers.Text = $"\u983b\u9053\u95dc\u6ce8:  {info.ChannelFollowerCount?.ToString("N0") ?? "-"}";
            _previewDate.Text = $"\u65e5\u671f:  {info.UploadDate ?? "-"}";
            if (!string.IsNullOrWhiteSpace(info.AccessWarning))
            {
                _previewStatus.Text = info.AccessWarning;
                _previewStatus.Foreground = Brush.Parse("#F59E0B");
                SetStatus(info.AccessWarning);
            }
            else
            {
                _previewStatus.Text = info.IsChannel
                    ? "\u983b\u9053\u8cc7\u8a0a\u89e3\u6790\u6210\u529f"
                    : "\u89e3\u6790\u6210\u529f \u00b7 \u53ef\u5167\u5d4c\u64ad\u653e";
                _previewStatus.Foreground = Green;
                SetStatus($"\u89e3\u6790\u6210\u529f\uff1a{info.Title}");
            }

            _previewPlayButton.IsEnabled = !info.IsChannel;
            _previewBrowserButton.IsEnabled = true;
            AppendLog($"\u6a19\u984c: {info.Title}");
            if (info.DurationSeconds is not null)
            {
                AppendLog($"\u6642\u9577: {FormatDuration(info.DurationSeconds)}");
            }
            if (info.AvailableDurationSeconds is not null
                && info.DurationSeconds is not null
                && info.AvailableDurationSeconds + 5 < info.DurationSeconds)
            {
                AppendLog(
                    $"\u53ef\u4e0b\u8f09\u4e32\u6d41\u6642\u9577: {FormatDuration(info.AvailableDurationSeconds)} (\u53ef\u80fd\u70ba\u8a66\u770b\u7247\u6bb5)");
            }

            if (!string.IsNullOrWhiteSpace(info.AccessWarning))
            {
                AppendLog($"\u8b66\u544a: {info.AccessWarning}");
            }

            if (info.ViewCount is not null)
            {
                AppendLog($"\u89c0\u770b\u6b21\u6578: {info.ViewCount.Value:N0}");
            }
            if (info.ChannelFollowerCount is not null)
            {
                AppendLog($"\u983b\u9053\u95dc\u6ce8: {info.ChannelFollowerCount.Value:N0}");
            }

            _ = LoadPreviewThumbnailAsync(info.ThumbnailUrl);
            // Do not auto-start NativeWebView. On macOS it is a native overlay that
            // does not scroll with Avalonia content and clips the convert button.
            if (!info.IsChannel)
            {
                _previewStatus.Text = EmbeddedPreviewEnabled
                    ? "\u89e3\u6790\u6210\u529f \u00b7 \u53ef\u9ede\u5167\u5d4c\u64ad\u653e"
                    : "\u89e3\u6790\u6210\u529f \u00b7 \u8acb\u7528\u300c\u539f\u9801\u300d\u64ad\u653e";
                _previewStatus.Foreground = Green;
            }
        }
        catch (Exception ex)
        {
            var detail = "\u7a0b\u5f0f\u5728\u89e3\u6790\u6642\u767c\u751f\u672a\u9810\u671f\u932f\u8aa4\u3002\u8acb\u91cd\u8a66\uff1b\u82e5\u6301\u7e8c\u767c\u751f\uff0c\u8acb\u67e5\u770b\u300c\u904b\u884c\u8a18\u9304\u300d\u7684\u8a73\u7d30\u8cc7\u8a0a\u3002";
            ShowParseError(detail);
            SetStatus("\u89e3\u6790\u5931\u6557\uff1a\u7a0b\u5f0f\u767c\u751f\u672a\u9810\u671f\u932f\u8aa4");
            AppendLog(ex.Message);
        }
        finally
        {
            try
            {
                _parseButton.IsEnabled = true;
            }
            catch
            {
                // ignore if control was torn down mid-operation
            }
        }
    }

    private async Task<ParsedVideoInfo?> DumpVideoInfoAsync(
        string ytDlpPath,
        string url,
        bool allowAutomaticBrowserCookies = true,
        bool allowAutomaticForAnySite = false,
        bool recordCookieFailure = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        AddYoutubeCompatibilityArguments(startInfo, url, fallback: false);
        AddBilibiliBrowserHeaders(startInfo, url);
        var cookieSelection = AddCookieArguments(
            startInfo,
            url,
            allowAutomaticBrowserCookies,
            allowAutomaticForAnySite);
        if (cookieSelection is not null)
        {
            AppendLog($"Cookies: {cookieSelection.Label}");
        }

        startInfo.ArgumentList.Add(NormalizeMediaUrl(url));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AppendLog(stderr.Trim());
            }

            // Browser sessions are optional for public Bilibili videos. If automatic
            // extraction fails (locked/corrupt/stale profile), retry anonymously so an
            // unrelated local browser problem cannot block public video parsing.
            if (!allowAutomaticForAnySite && VideoAccess.LooksLikeAccountRequired(stderr))
            {
                AppendLog("這支影片需要登入，改用瀏覽器 Cookies 再讀一次。");
                var unlocked = await DumpVideoInfoAsync(
                    ytDlpPath,
                    url,
                    allowAutomaticBrowserCookies: true,
                    allowAutomaticForAnySite: true,
                    recordCookieFailure: false);
                if (unlocked is not null)
                {
                    return unlocked;
                }
            }

            if (cookieSelection?.IsAutomaticBrowser == true && recordCookieFailure)
            {
                _automaticBrowserCookiesUnavailable = true;
                AppendLog("\u81ea\u52d5\u8b80\u53d6\u700f\u89bd\u5668 Cookies \u5931\u6557\uff0c\u6539\u7528\u4e0d\u767b\u5165\u6a21\u5f0f\u91cd\u8a66\u3002");
                return await DumpVideoInfoAsync(ytDlpPath, url, allowAutomaticBrowserCookies: false);
            }

            _lastParseErrorDetail = ClassifyParseFailure(stderr, url, cookieSelection);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "\u672a\u77e5\u6a19\u984c" : "\u672a\u77e5\u6a19\u984c";
            double? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetDouble()
                : null;
            long? views = root.TryGetProperty("view_count", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt64()
                : null;
            long? channelFollowers = root.TryGetProperty("channel_follower_count", out var cf)
                && cf.ValueKind == JsonValueKind.Number
                ? cf.GetInt64()
                : null;
            string? upload = null;
            if (root.TryGetProperty("upload_date", out var ud) && ud.GetString() is { Length: 8 } raw)
            {
                upload = $"{raw[..4]}-{raw[4..6]}-{raw[6..8]}";
            }

            var thumbnail = ExtractThumbnailUrl(root);
            var webpage = root.TryGetProperty("webpage_url", out var wu) ? wu.GetString() : null;
            if (string.IsNullOrWhiteSpace(webpage))
            {
                webpage = root.TryGetProperty("original_url", out var ou) ? ou.GetString() : url;
            }

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var extractor = root.TryGetProperty("extractor_key", out var ek)
                ? ek.GetString()
                : root.TryGetProperty("extractor", out var ex) ? ex.GetString() : null;

            var channelName = root.TryGetProperty("channel", out var channel) ? channel.GetString() : null;
            if (string.IsNullOrWhiteSpace(channelName) && root.TryGetProperty("uploader", out var uploader))
            {
                channelName = uploader.GetString();
            }

            var availableDuration = ExtractMaxFormatDurationSeconds(root);
            var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null;
            var availability = root.TryGetProperty("availability", out var av) ? av.GetString() : null;
            var (isPreviewOnly, accessWarning, accessKind) = DetectLimitedAccess(
                title,
                description,
                availability,
                duration,
                availableDuration,
                IsBilibiliVideoUrl(url));

            _formatChoices = FormatChoiceBuilder.FromElement(root);

            if (!allowAutomaticForAnySite
                && cookieSelection is null
                && VideoAccess.RequiresAccount(accessKind))
            {
                AppendLog("會員或付費影片，改用瀏覽器 Cookies 讀取可下載畫質。");
                var unlocked = await DumpVideoInfoAsync(
                    ytDlpPath,
                    url,
                    allowAutomaticBrowserCookies: true,
                    allowAutomaticForAnySite: true,
                    recordCookieFailure: false);
                if (unlocked is not null)
                {
                    return unlocked;
                }
            }

            return new ParsedVideoInfo(
                title,
                duration ?? availableDuration,
                views,
                channelFollowers,
                upload,
                url,
                thumbnail,
                webpage,
                id,
                extractor,
                channelName,
                ExpectedDurationSeconds: duration ?? availableDuration,
                AvailableDurationSeconds: availableDuration,
                IsPreviewOnly: isPreviewOnly,
                AccessWarning: accessWarning,
                Access: accessKind);
        }
        catch (Exception ex)
        {
            AppendLog($"JSON \u89e3\u6790\u5931\u6557: {ex.Message}");
            _lastParseErrorDetail = "\u5df2\u6536\u5230\u5f71\u7247\u8cc7\u6599\uff0c\u4f46\u683c\u5f0f\u7121\u6cd5\u8b80\u53d6\u3002\u8acb\u66f4\u65b0 yt-dlp \u5f8c\u91cd\u8a66\u3002";
            return null;
        }
    }

    private void ShowParseError(string detail)
    {
        _parseErrorText.Text = detail;
        _parseErrorPanel.IsVisible = true;
    }

    private void HideParseError()
    {
        _parseErrorPanel.IsVisible = false;
        _parseErrorText.Text = "";
    }

    private static string ClassifyParseFailure(
        string stderr,
        string url,
        CookieSelection? cookieSelection)
    {
        var error = stderr ?? "";
        if (error.Contains("cookies database", StringComparison.OrdinalIgnoreCase)
            || error.Contains("failed to decrypt", StringComparison.OrdinalIgnoreCase)
            || error.Contains("could not copy", StringComparison.OrdinalIgnoreCase))
        {
            return "\u7121\u6cd5\u8b80\u53d6\u700f\u89bd\u5668\u7684\u767b\u5165\u8cc7\u6599\u3002\u8acb\u532f\u51fa cookies.txt \u4e26\u5728\u300cCookies \u6a94\u6848\u300d\u532f\u5165\uff0c\u518d\u91cd\u65b0\u89e3\u6790\u3002";
        }

        if (error.Contains("Failed to establish a new connection", StringComparison.OrdinalIgnoreCase)
            || error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Temporary failure", StringComparison.OrdinalIgnoreCase))
        {
            return "\u7121\u6cd5\u9023\u7dda\u5230\u5f71\u7247\u7db2\u7ad9\u3002\u8acb\u6aa2\u67e5\u7db2\u8def\u3001VPN \u6216\u9632\u706b\u7246\u8a2d\u5b9a\uff0c\u78ba\u8a8d\u539f\u9801\u53ef\u958b\u555f\u5f8c\u518d\u8a66\u3002";
        }

        if (YoutubeDownloadPolicy.IsYouTubeUrl(url) && YoutubeDownloadPolicy.LooksLikeHttpForbidden(error))
        {
            return YoutubeDownloadPolicy.ForbiddenGiveUpHint(
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
        }

        if (VideoAccess.LooksLikeAccountRequired(error)
            || error.Contains("login", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Sign in", StringComparison.OrdinalIgnoreCase))
        {
            return VideoAccess.AccountRequiredHint();
        }

        if (error.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return YoutubeDownloadPolicy.IsYouTubeUrl(url)
                ? YoutubeDownloadPolicy.ForbiddenGiveUpHint(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                : "網站回傳 403。請更新 yt-dlp，或匯入已登入的 cookies.txt 後再試。";
        }

        if (error.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase))
        {
            return "\u9019\u4e0d\u662f\u53ef\u8fa8\u8b58\u7684 YouTube \u6216 Bilibili \u5f71\u7247\u7db2\u5740\u3002\u8acb\u5f9e\u5f71\u7247\u539f\u9801\u8907\u88fd\u5b8c\u6574\u7db2\u5740\u5f8c\u91cd\u8a66\u3002";
        }

        var cookieHint = cookieSelection is not null
            ? "\u82e5\u5167\u5bb9\u6709\u767b\u5165\u9650\u5236\uff0c\u8acb\u66f4\u65b0 cookies.txt \u5f8c\u91cd\u8a66\u3002"
            : "\u82e5\u5167\u5bb9\u6709\u767b\u5165\u9650\u5236\uff0c\u8acb\u532f\u5165 cookies.txt \u5f8c\u91cd\u8a66\u3002";
        return IsBilibiliVideoUrl(url)
            ? $"Bilibili \u672a\u56de\u50b3\u53ef\u7528\u7684\u5f71\u7247\u8cc7\u8a0a\u3002\u8acb\u78ba\u8a8d\u539f\u9801\u4ecd\u53ef\u64ad\u653e\u3002{cookieHint}"
            : $"YouTube \u672a\u56de\u50b3\u53ef\u7528\u7684\u5f71\u7247\u8cc7\u8a0a\u3002\u8acb\u78ba\u8a8d\u539f\u9801\u4ecd\u53ef\u64ad\u653e\u3002{cookieHint}";
    }

    private static double? ExtractMaxFormatDurationSeconds(JsonElement root)
    {
        double? max = null;
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var format in formats.EnumerateArray())
        {
            if (format.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
            {
                var value = d.GetDouble();
                if (value > 0 && (max is null || value > max))
                {
                    max = value;
                }
            }
        }

        return max;
    }

    private static (bool IsPreviewOnly, string? AccessWarning, VideoAccess.Kind Access) DetectLimitedAccess(
        string title,
        string? description,
        string? availability,
        double? metadataDuration,
        double? availableDuration,
        bool isBilibili)
    {
        var access = VideoAccess.Classify(title, description, availability);
        var previewInText = $"{title}\n{description}".Contains("試看", StringComparison.Ordinal)
            || $"{title}\n{description}".Contains("试看", StringComparison.Ordinal);

        // Stream shorter than claimed length => free preview / locked full video.
        var durationMismatch = metadataDuration is > 30
            && availableDuration is > 0
            && availableDuration + 15 < metadataDuration * 0.85;

        if (!durationMismatch && access == VideoAccess.Kind.Public && !previewInText)
        {
            return (false, null, VideoAccess.Kind.Public);
        }

        if (!(isBilibili || access != VideoAccess.Kind.Public || durationMismatch || previewInText))
        {
            return (false, null, access);
        }

        var claimed = FormatDuration(metadataDuration);
        var available = FormatDuration(availableDuration ?? metadataDuration);
        var badge = VideoAccess.Badge(access);
        string warning;
        if (durationMismatch)
        {
            var name = badge ?? "限制";
            warning = $"{name}：完整約 {claimed}，目前讀到約 {available}。仍可下載，程式會帶已登入的 cookies 盡量取得完整版。";
        }
        else if (access == VideoAccess.Kind.Paid)
        {
            warning = "付費影片。仍可下載；帳號需已購買或開通，並使用已登入的 cookies。";
        }
        else if (access == VideoAccess.Kind.Member)
        {
            warning = "會員影片。仍可下載；帳號需有這個頻道的會員資格，並使用已登入的 cookies。";
        }
        else if (access == VideoAccess.Kind.NeedsLogin)
        {
            warning = "這支影片需要登入。仍可下載；請用已登入的瀏覽器 cookies，或在設定匯入 cookies.txt。";
        }
        else
        {
            warning = $"可能是試看片段（約 {available}）。仍可下載；完整版需要已登入的 cookies。";
        }

        return (durationMismatch, warning, access);
    }

    private async Task<ParsedVideoInfo?> DumpChannelInfoAsync(string ytDlpPath, string channelUrl)
    {
        var sampleVideoUrl = await FindChannelSampleVideoUrlAsync(ytDlpPath, channelUrl);
        if (string.IsNullOrWhiteSpace(sampleVideoUrl))
        {
            AppendLog("\u7121\u6cd5\u5f9e\u983b\u9053\u53d6\u5f97\u516c\u958b\u5f71\u7247\uff0c\u7121\u6cd5\u8b80\u53d6\u8a02\u95b1\u6578\u3002");
            return null;
        }

        var sampleInfo = await DumpVideoInfoAsync(ytDlpPath, sampleVideoUrl);
        if (sampleInfo is null)
        {
            return null;
        }

        var channelName = sampleInfo.ChannelName ?? sampleInfo.Title;
        return sampleInfo with
        {
            Title = $"\u983b\u9053\uff1a{channelName}",
            DurationSeconds = null,
            ViewCount = null,
            UploadDate = null,
            Url = channelUrl,
            WebpageUrl = channelUrl,
            IsChannel = true
        };
    }

    private async Task<string?> FindChannelSampleVideoUrlAsync(string ytDlpPath, string channelUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--playlist-end");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("%(webpage_url)s");
        startInfo.ArgumentList.Add(channelUrl);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AppendLog(stderr.Trim());
            }

            return null;
        }

        return stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(value => Uri.TryCreate(value, UriKind.Absolute, out _));
    }

    private static string? ExtractThumbnailUrl(JsonElement root)
    {
        if (root.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.String)
        {
            var direct = thumb.GetString();
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }
        }

        if (root.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
        {
            string? best = null;
            var bestArea = -1;
            foreach (var item in thumbs.EnumerateArray())
            {
                var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var w = item.TryGetProperty("width", out var ww) && ww.ValueKind == JsonValueKind.Number
                    ? ww.GetInt32()
                    : 0;
                var h = item.TryGetProperty("height", out var hh) && hh.ValueKind == JsonValueKind.Number
                    ? hh.GetInt32()
                    : 0;
                var area = w * h;
                if (area >= bestArea)
                {
                    bestArea = area;
                    best = url;
                }
            }

            return best;
        }

        return null;
    }

    private async void PlayOriginalVideoAsync(object? sender, RoutedEventArgs e) => await PlayOriginalVideoCoreAsync();

    private async Task PlayOriginalVideoCoreAsync()
    {
        var url = _parsedInfo?.Url ?? GetInputUrls().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("\u8acb\u5148\u8cbc\u4e0a\u4e26\u89e3\u6790\u5f71\u7247\u7db2\u5740");
            return;
        }

        // Ensure metadata is ready when user plays right after pasting.
        if (_parsedInfo is null || !string.Equals(_parsedInfo.Url, url, StringComparison.OrdinalIgnoreCase))
        {
            await ParseUrlCoreAsync();
            if (_parsedInfo is null)
            {
                return;
            }
        }

        await StartEmbeddedPreviewAsync(autoplay: true);
    }

    private async Task StartEmbeddedPreviewAsync(bool autoplay)
    {
        if (_parsedInfo is null)
        {
            SetStatus("\u8acb\u5148\u89e3\u6790\u5f71\u7247");
            return;
        }

        if (!EmbeddedPreviewEnabled)
        {
            // Never touch NativeWebView on Windows — property access can crash the process.
            _embeddedPreviewActive = false;
            _previewOverlay.IsVisible = true;
            _previewStopButton.IsEnabled = false;
            _previewStatus.Text = "Windows \u5df2\u6539\u7528\u7a69\u5b9a\u7684\u539f\u9801\u64ad\u653e";
            _previewStatus.Foreground = TextMuted;
            SetStatus("\u5167\u5d4c\u9810\u89bd\u5728 Windows \u5df2\u505c\u7528\uff1b\u8acb\u7528\u300c\u539f\u9801\u300d\u958b\u555f\u5f71\u7247");

            if (autoplay)
            {
                OpenOriginalPage(_parsedInfo.WebpageUrl ?? _parsedInfo.Url);
            }

            return;
        }

        var version = Interlocked.Increment(ref _previewStreamVersion);
        _previewLoadCts?.Cancel();
        _previewLoadCts?.Dispose();
        _previewLoadCts = new CancellationTokenSource();
        var token = _previewLoadCts.Token;

        try
        {
            SafeSetPreviewWebViewVisible(true);
            _previewOverlay.IsVisible = false;
            _embeddedPreviewActive = true;
            _previewStopButton.IsEnabled = true;
            _previewPlayButton.IsEnabled = true;
            _previewStatus.Text = "\u6b63\u5728\u53d6\u5f97\u9810\u89bd\u4e32\u6d41...";
            _previewStatus.Foreground = Blue;
            SetStatus("\u6b63\u5728\u53d6\u5f97\u53ef\u64ad\u653e\u7684\u9810\u89bd\u4e32\u6d41...");

            // Prefer progressive stream + HTML5 <video>. YouTube iframe embeds often fail in
            // desktop WebViews with Error 152-4 / 153 ("watch on YouTube" / config error).
            var stream = await ResolvePreviewStreamAsync(_parsedInfo, token).ConfigureAwait(true);
            if (version != _previewStreamVersion || token.IsCancellationRequested)
            {
                return;
            }

            if (stream is not null)
            {
                LoadHtml5VideoPreview(stream, autoplay);
                _previewStatus.Text = autoplay ? "\u9810\u89bd\u64ad\u653e\u4e2d" : "\u9810\u89bd\u5df2\u5c31\u7dd2\uff0c\u53ef\u9ede\u64ad\u653e";
                _previewStatus.Foreground = Green;
                SetStatus(autoplay
                    ? "\u6b63\u5728\u64ad\u653e\u9810\u89bd\u4e32\u6d41"
                    : "\u9810\u89bd\u4e32\u6d41\u5df2\u8f09\u5165\uff0c\u53ef\u76f4\u63a5\u9ede\u64ad\u653e");
                AppendLog($"\u9810\u89bd\u4e32\u6d41: {TruncateForLog(stream.Url, 120)}");
                return;
            }

            // Fallback: platform embed player (mostly useful for Bilibili).
            var embed = TryBuildEmbedUri(_parsedInfo, autoplay);
            if (embed is null)
            {
                SetStatus("\u7121\u6cd5\u5efa\u7acb\u9810\u89bd\uff0c\u6539\u70ba\u958b\u555f\u539f\u9801");
                OpenOriginalPage(_parsedInfo.WebpageUrl ?? _parsedInfo.Url);
                return;
            }

            LoadEmbeddedPlayer(embed);
            _previewStatus.Text = autoplay
                ? "\u5167\u5d4c\u64ad\u653e\u4e2d"
                : "\u5167\u5d4c\u64ad\u653e\u5668\u5df2\u8f09\u5165";
            _previewStatus.Foreground = Blue;
            SetStatus(autoplay
                ? "\u6b63\u5728\u5167\u5d4c\u64ad\u653e\u539f\u5f71\u7247"
                : "\u5df2\u5167\u5d4c\u539f\u5f71\u7247\u64ad\u653e\u5668\uff0c\u53ef\u76f4\u63a5\u9ede\u64ad\u653e");
            AppendLog($"\u5167\u5d4c\u9810\u89bd\uff08\u5099\u7528\uff09: {embed}");
        }
        catch (OperationCanceledException)
        {
            // ignored — user stopped or re-parsed
        }
        catch (Exception ex)
        {
            AppendLog($"\u9810\u89bd\u555f\u52d5\u5931\u6557: {ex.Message}");
            SetStatus("\u9810\u89bd\u4e0d\u53ef\u7528\uff0c\u8acb\u7528\u300c\u539f\u9801\u300d\u958b\u555f");
            _previewOverlay.IsVisible = true;
            SafeSetPreviewWebViewVisible(false);
            if (autoplay && _parsedInfo is not null)
            {
                OpenOriginalPage(_parsedInfo.WebpageUrl ?? _parsedInfo.Url);
            }
        }
    }

    private void SafeSetPreviewWebViewVisible(bool visible)
    {
        if (!EmbeddedPreviewEnabled)
        {
            return;
        }

        try
        {
            _previewWebView.IsVisible = visible;
        }
        catch
        {
            // ignore — WebView may not be ready
        }
    }

    private async Task<PreviewStreamInfo?> ResolvePreviewStreamAsync(ParsedVideoInfo info, CancellationToken token)
    {
        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        if (ytDlpPath is null)
        {
            return null;
        }

        var pageUrl = info.WebpageUrl ?? info.Url;
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        // Prefer a single progressive A+V file for HTML5 <video>. Avoid pure-audio
        // "best" as the first choice — audio-only in <video> freezes some WebViews.
        // If only audio is available, BuildHtml5VideoPlayerHtml switches to <audio>.
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(
            "best[vcodec!=none][acodec!=none][height<=720]/best[vcodec!=none][acodec!=none]/bestaudio/best");
        startInfo.ArgumentList.Add("-g");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        AddBilibiliBrowserHeaders(startInfo, pageUrl);
        _ = AddCookieArguments(startInfo, pageUrl);
        startInfo.ArgumentList.Add(NormalizeMediaUrl(pageUrl));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return null;
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    AppendLog($"\u9810\u89bd\u4e32\u6d41\u5931\u6557: {TruncateForLog(stderr.Trim(), 240)}");
                }

                return null;
            }

            var streamUrl = stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line =>
                    line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(streamUrl) || !Uri.TryCreate(streamUrl, UriKind.Absolute, out _))
            {
                return null;
            }

            var referer = GetPageReferer(pageUrl);
            return new PreviewStreamInfo(streamUrl, referer);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }

            throw;
        }
    }

    private void LoadHtml5VideoPreview(PreviewStreamInfo stream, bool autoplay)
    {
        _previewReferer = stream.Referer;
        _pendingEmbedHtml = BuildHtml5VideoPlayerHtml(stream.Url, autoplay);
        _pendingEmbedBaseUri = Uri.TryCreate(stream.Referer, UriKind.Absolute, out var baseUri)
            ? baseUri
            : new Uri("https://www.youtube.com/");
        _pendingDirectEmbedUri = null;
        FlushPendingEmbeddedPreview();
    }

    private void LoadEmbeddedPlayer(Uri embedUri)
    {
        var baseUri = GetEmbedBaseUri(embedUri);
        _previewReferer = baseUri.AbsoluteUri;

        // Bilibili player page works best as a top-level navigation.
        // Avoid nested iframes for YouTube (Error 152-4 / 153 in many WebViews).
        if (IsBilibiliPlayerUri(embedUri))
        {
            _pendingEmbedHtml = null;
            _pendingEmbedBaseUri = null;
            _pendingDirectEmbedUri = embedUri;
        }
        else if (IsYouTubeEmbedUri(embedUri))
        {
            // Direct top-level embed as last-resort fallback only.
            _pendingEmbedHtml = null;
            _pendingEmbedBaseUri = null;
            _pendingDirectEmbedUri = embedUri;
        }
        else
        {
            _pendingEmbedHtml = null;
            _pendingEmbedBaseUri = null;
            _pendingDirectEmbedUri = embedUri;
        }

        FlushPendingEmbeddedPreview();
    }

    private static bool IsYouTubeEmbedUri(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return (host.Contains("youtube.com", StringComparison.Ordinal)
                || host.Contains("youtube-nocookie.com", StringComparison.Ordinal))
               && uri.AbsolutePath.Contains("/embed/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBilibiliPlayerUri(Uri uri) =>
        uri.Host.Contains("player.bilibili.com", StringComparison.OrdinalIgnoreCase);

    private void FlushPendingEmbeddedPreview()
    {
        if (_pendingDirectEmbedUri is not null)
        {
            var direct = _pendingDirectEmbedUri;
            try
            {
                _previewWebView.Source = direct;
                _pendingDirectEmbedUri = null;
            }
            catch (Exception ex)
            {
                if (!_previewAdapterReady)
                {
                    return;
                }

                AppendLog($"\u76f4\u63a5\u5167\u5d4c\u5931\u6557: {ex.Message}");
                _pendingDirectEmbedUri = null;
            }

            return;
        }

        if (_pendingEmbedHtml is null || _pendingEmbedBaseUri is null)
        {
            return;
        }

        var html = _pendingEmbedHtml;
        var baseUri = _pendingEmbedBaseUri;

        try
        {
            _previewWebView.NavigateToString(html, baseUri);
            _pendingEmbedHtml = null;
            _pendingEmbedBaseUri = null;
        }
        catch (Exception ex)
        {
            if (!_previewAdapterReady)
            {
                return;
            }

            AppendLog($"NavigateToString \u5931\u6557: {ex.Message}");
            _pendingEmbedHtml = null;
            _pendingEmbedBaseUri = null;
        }
    }

    private static Uri GetEmbedBaseUri(Uri embedUri)
    {
        var host = embedUri.Host.ToLowerInvariant();
        if (host.Contains("bilibili", StringComparison.Ordinal)
            || host.Contains("bilivideo", StringComparison.Ordinal)
            || host.Contains("hdslb", StringComparison.Ordinal))
        {
            return new Uri("https://www.bilibili.com/");
        }

        if (host.Contains("youtu", StringComparison.Ordinal)
            || host.Contains("google", StringComparison.Ordinal)
            || host.Contains("ytimg", StringComparison.Ordinal)
            || host.Contains("ggpht", StringComparison.Ordinal)
            || host.Contains("googlevideo", StringComparison.Ordinal))
        {
            return new Uri("https://www.youtube.com/");
        }

        return new Uri($"{embedUri.Scheme}://{embedUri.Host}/");
    }

    private static string GetPageReferer(string pageUrl)
    {
        if (IsBilibiliVideoUrl(pageUrl))
        {
            return "https://www.bilibili.com/";
        }

        if (IsYouTubeUrl(pageUrl))
        {
            return "https://www.youtube.com/";
        }

        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}/";
        }

        return "https://www.youtube.com/";
    }

    private static string BuildHtml5VideoPlayerHtml(string streamUrl, bool autoplay)
    {
        var src = System.Net.WebUtility.HtmlEncode(streamUrl);
        var autoplayAttr = autoplay ? " autoplay" : "";
        // Audio-only progressive URLs (mp3/m4a/opus, or googlevideo mime=audio) hang or
        // crash some desktop WebViews when forced into a <video> element.
        if (LooksLikeAudioOnlyStream(streamUrl))
        {
            return
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
                "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, maximum-scale=1\" />" +
                "<meta name=\"referrer\" content=\"origin\" />" +
                "<style>html,body{margin:0;padding:0;width:100%;height:100%;background:#0F172A;overflow:hidden;" +
                "display:flex;align-items:center;justify-content:center}" +
                "audio{width:90%;max-width:520px}</style></head><body>" +
                "<audio controls preload=\"metadata\"" + autoplayAttr +
                " src=\"" + src + "\"></audio>" +
                "</body></html>";
        }

        return
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, maximum-scale=1\" />" +
            "<meta name=\"referrer\" content=\"origin\" />" +
            "<style>html,body{margin:0;padding:0;width:100%;height:100%;background:#000;overflow:hidden}" +
            "video{width:100%;height:100%;display:block;background:#000;object-fit:contain}</style></head><body>" +
            "<video controls playsinline webkit-playsinline preload=\"metadata\"" + autoplayAttr +
            " src=\"" + src + "\"></video>" +
            "</body></html>";
    }

    private static bool LooksLikeAudioOnlyStream(string streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return false;
        }

        if (streamUrl.Contains("mime=audio", StringComparison.OrdinalIgnoreCase)
            || streamUrl.Contains("mime%3Daudio", StringComparison.OrdinalIgnoreCase)
            || streamUrl.Contains("/audio/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".weba", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
               && streamUrl.Contains("audio", StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncateForLog(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars] + "...";
    }

    private void OnPreviewWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_previewReferer) || e.Request?.Headers is null)
        {
            return;
        }

        try
        {
            // Stream CDNs (googlevideo / bilivideo) often require a matching Referer.
            e.Request.Headers.TrySet("Referer", _previewReferer);
        }
        catch
        {
            // Headers may be read-only for some request types/platforms.
        }
    }

    private void StopEmbeddedPreview(bool clearStatus = true)
    {
        Interlocked.Increment(ref _previewStreamVersion);
        try
        {
            _previewLoadCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _pendingEmbedHtml = null;
        _pendingEmbedBaseUri = null;
        _pendingDirectEmbedUri = null;
        _previewReferer = null;

        if (EmbeddedPreviewEnabled)
        {
            try
            {
                if (_embeddedPreviewActive || _previewWebView.IsVisible)
                {
                    _previewWebView.Stop();
                    _previewWebView.Source = new Uri("about:blank");
                }
            }
            catch
            {
                // ignore stop failures
            }

            SafeSetPreviewWebViewVisible(false);
        }

        _embeddedPreviewActive = false;
        _previewOverlay.IsVisible = true;
        _previewStopButton.IsEnabled = false;

        if (_parsedInfo is not null)
        {
            _previewPlayButton.IsEnabled = true;
            _previewStatus.Text = EmbeddedPreviewEnabled
                ? "\u89e3\u6790\u6210\u529f \u00b7 \u53ef\u5167\u5d4c\u64ad\u653e"
                : "\u89e3\u6790\u6210\u529f \u00b7 \u8acb\u7528\u300c\u539f\u9801\u300d\u64ad\u653e";
            _previewStatus.Foreground = Green;
            if (clearStatus)
            {
                SetStatus("\u5df2\u505c\u6b62\u5167\u5d4c\u9810\u89bd");
            }
        }
    }

    private void OnPreviewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!_embeddedPreviewActive)
        {
            return;
        }

        if (e.IsSuccess)
        {
            var status = _previewStatus.Text ?? "";
            if (status.Contains("\u4e32\u6d41", StringComparison.Ordinal)
                || status.Contains("\u9810\u89bd", StringComparison.Ordinal))
            {
                // Keep stream-specific status.
                return;
            }

            _previewStatus.Text = "\u5167\u5d4c\u64ad\u653e\u5668\u5df2\u5c31\u7dd2";
            _previewStatus.Foreground = Green;
            return;
        }

        _previewStatus.Text = "\u5167\u5d4c\u8f09\u5165\u5931\u6557";
        _previewStatus.Foreground = Brush.Parse("#EF4444");
        AppendLog("\u5167\u5d4c\u7db2\u9801\u8f09\u5165\u5931\u6557\uff0c\u53ef\u6539\u7528\u300c\u539f\u9801\u300d");
        if (e.Request is not null)
        {
            AppendLog($"\u5167\u5d4c\u5931\u6557 URL: {e.Request}");
        }
    }

    private static Uri? TryBuildEmbedUri(ParsedVideoInfo info, bool autoplay)
    {
        var ap = autoplay ? "1" : "0";
        var extractor = info.ExtractorKey ?? "";
        var id = info.VideoId;
        var page = info.WebpageUrl ?? info.Url;

        // Prefer platform IDs from yt-dlp when available.
        if (!string.IsNullOrWhiteSpace(id))
        {
            if (extractor.Contains("Youtube", StringComparison.OrdinalIgnoreCase)
                || extractor.Contains("YouTube", StringComparison.OrdinalIgnoreCase)
                || IsYouTubeUrl(page))
            {
                return new Uri(
                    $"https://www.youtube.com/embed/{Uri.EscapeDataString(id)}?autoplay={ap}&rel=0&modestbranding=1&playsinline=1");
            }

            if (extractor.Contains("Bili", StringComparison.OrdinalIgnoreCase) || IsBilibiliVideoUrl(page))
            {
                if (id.StartsWith("BV", StringComparison.OrdinalIgnoreCase))
                {
                    return new Uri(
                        $"https://player.bilibili.com/player.html?isOutside=true&bvid={Uri.EscapeDataString(id)}&autoplay={ap}&high_quality=1&danmaku=0&as_wide=1");
                }

                if (id.StartsWith("av", StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(id.AsSpan(2), out _))
                {
                    return new Uri(
                        $"https://player.bilibili.com/player.html?isOutside=true&aid={Uri.EscapeDataString(id[2..])}&autoplay={ap}&high_quality=1&danmaku=0&as_wide=1");
                }

                if (long.TryParse(id, out _))
                {
                    return new Uri(
                        $"https://player.bilibili.com/player.html?isOutside=true&aid={Uri.EscapeDataString(id)}&autoplay={ap}&high_quality=1&danmaku=0&as_wide=1");
                }
            }
        }

        return TryBuildEmbedUriFromPageUrl(page, autoplay);
    }

    private static Uri? TryBuildEmbedUriFromPageUrl(string? pageUrl, bool autoplay)
    {
        if (string.IsNullOrWhiteSpace(pageUrl) || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var ap = autoplay ? "1" : "0";
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;

        if (host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var id = path.Trim('/');
            if (!string.IsNullOrWhiteSpace(id))
            {
                return new Uri(
                    $"https://www.youtube.com/embed/{Uri.EscapeDataString(id)}?autoplay={ap}&rel=0&modestbranding=1&playsinline=1");
            }
        }

        if (host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Contains("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
        {
            var id = GetQueryParameter(uri, "v");
            if (string.IsNullOrWhiteSpace(id))
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && (parts[0].Equals("embed", StringComparison.OrdinalIgnoreCase)
                        || parts[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)
                        || parts[0].Equals("live", StringComparison.OrdinalIgnoreCase)))
                {
                    id = parts[1];
                }
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                return new Uri(
                    $"https://www.youtube.com/embed/{Uri.EscapeDataString(id)}?autoplay={ap}&rel=0&modestbranding=1&playsinline=1");
            }
        }

        if (host.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase)
            || host.Contains("b23.tv", StringComparison.OrdinalIgnoreCase))
        {
            var bv = Regex.Match(pageUrl, @"BV[0-9A-Za-z]+", RegexOptions.IgnoreCase);
            if (bv.Success)
            {
                return new Uri(
                    $"https://player.bilibili.com/player.html?isOutside=true&bvid={Uri.EscapeDataString(bv.Value)}&autoplay={ap}&high_quality=1&danmaku=0&as_wide=1");
            }

            var av = Regex.Match(pageUrl, @"av(\d+)", RegexOptions.IgnoreCase);
            if (av.Success)
            {
                return new Uri(
                    $"https://player.bilibili.com/player.html?isOutside=true&aid={av.Groups[1].Value}&autoplay={ap}&high_quality=1&danmaku=0&as_wide=1");
            }
        }

        // Last resort: load original page inside the embedded webview.
        return uri;
    }

    private static bool IsYouTubeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYouTubeChannelUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !(uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.StartsWith("/@", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/channel/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/c/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/user/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        var query = uri.Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[0]);
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
        }

        return null;
    }

    private void OpenOriginalInBrowser(object? sender, RoutedEventArgs e)
    {
        var url = _parsedInfo?.WebpageUrl
            ?? _parsedInfo?.Url
            ?? GetInputUrls().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("\u8acb\u5148\u8cbc\u4e0a\u5f71\u7247\u7db2\u5740");
            return;
        }

        OpenOriginalPage(url);
    }

    private void OpenOriginalPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            SetStatus("\u5df2\u5728\u700f\u89bd\u5668\u958b\u555f\u539f\u5f71\u7247");
            AppendLog($"\u700f\u89bd\u5668\u958b\u555f: {url}");
        }
        catch (Exception ex)
        {
            SetStatus("\u7121\u6cd5\u958b\u555f\u700f\u89bd\u5668");
            AppendLog(ex.Message);
        }
    }

    private async Task LoadPreviewThumbnailAsync(string? thumbnailUrl)
    {
        var version = Interlocked.Increment(ref _thumbnailLoadVersion);
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            ClearPreviewThumbnail();
            return;
        }

        try
        {
            var referer = _parsedInfo is not null && IsBilibiliVideoUrl(_parsedInfo.Url)
                ? "https://www.bilibili.com/"
                : null;
            var bytes = await AppHttp.GetBytesAsync(thumbnailUrl, referer);
            if (version != _thumbnailLoadVersion)
            {
                return;
            }

            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _thumbnailLoadVersion)
                {
                    bitmap.Dispose();
                    return;
                }

                _previewBitmap?.Dispose();
                _previewBitmap = bitmap;
                _previewImage.Source = bitmap;
                _previewImage.IsVisible = true;
                _previewThumbPlaceholder.IsVisible = false;
            });
        }
        catch (Exception ex)
        {
            if (version == _thumbnailLoadVersion)
            {
                AppendLog($"\u7e2e\u5716\u8f09\u5165\u5931\u6557: {ex.Message}");
                ClearPreviewThumbnail();
            }
        }
    }

    private void ClearPreviewThumbnail()
    {
        void Apply()
        {
            _previewImage.Source = null;
            _previewImage.IsVisible = false;
            _previewThumbPlaceholder.IsVisible = true;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private string[] GetInputUrls()
    {
        var text = _urlBox.Text ?? "";
        return text
            .Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(u => NormalizeMediaUrl(u, preservePlaylistParams: _downloadPlaylist))
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async void ConvertOrCancelAsync(object? sender, RoutedEventArgs e)
    {
        if (_conversionTokenSource is not null)
        {
            _conversionTokenSource.Cancel();
            return;
        }

        await ConvertOrCancelCoreAsync();
    }

    private async Task ConvertOrCancelCoreAsync()
    {
        if (_shellCancelRequested)
        {
            return;
        }

        if (_conversionTokenSource is not null)
        {
            SetStatus("\u5df2\u6709\u8f49\u63db\u4efb\u52d9\u9032\u884c\u4e2d");
            return;
        }

        var urls = GetInputUrls();
        if (urls.Length == 0)
        {
            SetStatus("\u8acb\u81f3\u5c11\u8f38\u5165\u4e00\u500b YouTube \u6216 Bilibili \u7db2\u5740");
            return;
        }

        var outputPath = _outputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
        {
            SetStatus("\u8acb\u9078\u64c7\u6709\u6548\u7684\u8f38\u51fa\u8cc7\u6599\u593e");
            return;
        }

        SaveSettingsIfPossible();

        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        var ffmpegPath = ToolLocator.FindExecutable("ffmpeg");
        var ffprobePath = ToolLocator.FindExecutable("ffprobe");
        if (ytDlpPath is null || ffmpegPath is null || ffprobePath is null)
        {
            SetStatus("\u627e\u4e0d\u5230\u8f49\u6a94\u5de5\u5177\uff0c\u8acb\u5148\u5b89\u88dd\u5f8c\u518d\u8a66");
            AppendInstallHint();
            AppendLog($"yt-dlp: {ytDlpPath ?? "\u627e\u4e0d\u5230"}");
            AppendLog($"ffmpeg: {ffmpegPath ?? "\u627e\u4e0d\u5230"}");
            AppendLog($"ffprobe: {ffprobePath ?? "\u627e\u4e0d\u5230"}");
            return;
        }

        foreach (var url in urls)
        {
            var title = _parsedInfo is not null
                && (string.Equals(_parsedInfo.Url, url, StringComparison.OrdinalIgnoreCase)
                    || UrlsLikelySameVideo(_parsedInfo.Url, url)
                    || UrlsLikelySameVideo(_parsedInfo.WebpageUrl, url))
                ? _parsedInfo.Title
                : url;
            if (_downloadPlaylist && LooksLikePlaylistUrl(url))
            {
                title = "\u64ad\u653e\u6e05\u55ae \u00b7 " + title;
            }

            var existing = _downloadItems.FirstOrDefault(i =>
                UrlsLikelySameVideo(i.Url, url)
                && string.Equals(i.Format, _outputFormat, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.State is DownloadState.Queued or DownloadState.Running or DownloadState.Paused)
                {
                    AppendLog($"\u5df2\u5728\u6e05\u55ae\u4e2d\uff0c\u7565\u904e\u91cd\u8907\u9805\u76ee: {existing.Title}");
                    continue;
                }

                existing.ResetForRetry();
                continue;
            }

            var item = new DownloadItemView(title, url, _outputFormat, _mp4Quality);
            item.OnRemove = () =>
            {
                if (item.State == DownloadState.Running)
                {
                    return;
                }

                _downloadItems.Remove(item);
                RebuildDownloadList();
            };
            item.OnCancel = () =>
            {
                if (_activeDownload == item)
                {
                    _conversionTokenSource?.Cancel();
                }
                else if (item.State == DownloadState.Queued)
                {
                    item.SetState(DownloadState.Cancelled);
                }
            };
            item.OnOpen = () => OpenMediaFile(item.OutputPath, item.Format);
            item.OnStateChanged = RefreshQueueSections;
            _downloadItems.Add(item);
        }

        RebuildDownloadList();
        _conversionTokenSource = new CancellationTokenSource();
        SetBusy(true);
        AppendLog($"\u8f38\u51fa\u8cc7\u6599\u593e: {outputPath}");
        AppendLog($"\u8f38\u51fa\u683c\u5f0f: {_outputFormat}");
        if (_outputFormat == "MP4")
        {
            AppendLog($"MP4 \u756b\u8cea: {_mp4Quality}");
        }

        AppendLog(_downloadPlaylist
            ? "\u64ad\u653e\u6e05\u55ae: \u958b\u555f\uff08\u6574\u4efd\u4e0b\u8f09\uff09"
            : "\u64ad\u653e\u6e05\u55ae: \u95dc\u9589\uff08\u50c5\u55ae\u4e00\u5f71\u7247\uff09");

        AppendLog(_includeSubtitles
            ? "\u5b57\u5e55: \u958b\u555f\uff08\u4e2d\u6587\u512a\u5148\uff0c\u5916\u639b .srt\uff1bMP4 \u5167\u5d4c\uff1bMP3 \u751f\u6210 .lrc\uff09"
            : "\u5b57\u5e55: \u95dc\u9589");

        try
        {
            var successCount = 0;
            var pending = _downloadItems.Where(i => i.State == DownloadState.Queued).ToList();
            for (var index = 0; index < pending.Count; index++)
            {
                var item = pending[index];
                if (item.State == DownloadState.Cancelled)
                {
                    continue;
                }

                _activeDownload = item;
                _lastMediaOutputPath = null;
                item.SetState(DownloadState.Running);
                SetStatus($"\u6b63\u5728\u8f49\u63db {index + 1}/{pending.Count}...");
                AppendLog("");
                AppendLog($"[{index + 1}/{pending.Count}] {item.Url}");

                // Phase 1: media first (never blocked by subtitle rate-limits).
                var code = await RunYtDlpAsync(
                    ytDlpPath,
                    ffmpegPath,
                    ffprobePath,
                    item.Url,
                    outputPath,
                    item.Format,
                    item.Quality,
                    includeSubtitles: false,
                    _conversionTokenSource.Token,
                    item);

                if (code == 0 && _includeSubtitles)
                {
                    AppendLog("\u5b57\u5e55: \u5f71\u97f3\u5b8c\u6210\uff0c\u958b\u59cb\u53e6\u884c\u4e0b\u8f09\u5b57\u5e55\uff08\u5931\u6557\u4e0d\u5f71\u97ff\u5f71\u97f3\uff09...");
                    await RunSubtitleDownloadAsync(
                        ytDlpPath,
                        ffmpegPath,
                        ffprobePath,
                        item.Url,
                        outputPath,
                        item.Format,
                        _conversionTokenSource.Token,
                        item);

                    // Run pairing/embed off the UI thread so ffmpeg WaitForExit cannot freeze the window.
                    var mediaHint = _lastMediaOutputPath;
                    var format = item.Format;
                    await Task.Run(
                        () => PairSubtitlesWithMedia(outputPath, mediaHint, format, ffmpegPath),
                        _conversionTokenSource.Token);
                }

                // Flush any buffered log lines after each item.
                FlushLogToUi(force: true);

                if (code == 0)
                {
                    successCount++;
                    var finalPath = ResolveMediaPath(outputPath, _lastMediaOutputPath, item.Format);
                    if (string.Equals(item.Format, "MP3", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(finalPath)
                        && File.Exists(finalPath))
                    {
                        // Ensure the finished MP3 is openable by common Windows players
                        // (strip broken cover art / nonstandard tags that block shell open).
                        finalPath = await SanitizeMp3ForPlaybackAsync(ffmpegPath, finalPath)
                            .ConfigureAwait(true) ?? finalPath;
                    }

                    item.OutputPath = finalPath;
                    item.SetState(DownloadState.Completed);
                    item.SetProgress(100, "\u5b8c\u6210");
                    if (!string.IsNullOrWhiteSpace(finalPath))
                    {
                        AppendLog($"\u8f38\u51fa\u6a94\u6848: {finalPath}");
                    }
                    else
                    {
                        AppendLog("\u8b66\u544a: \u8f49\u63db\u6210\u529f\u4f46\u627e\u4e0d\u5230\u8f38\u51fa\u6a94\uff0c\u8acb\u81ea\u8a02\u8f38\u51fa\u8cc7\u6599\u593e\u6aa2\u67e5");
                    }

                    await WarnIfDownloadedDurationIsShortAsync(
                        ffprobePath,
                        outputPath,
                        item.Format,
                        item.Url);
                    _todayDownloads++;
                    SaveSettingsIfPossible();
                    UpdateFooter();
                }
                else if (_conversionTokenSource.IsCancellationRequested)
                {
                    item.SetState(DownloadState.Cancelled);
                    item.SetProgress(item.Progress, "\u5df2\u53d6\u6d88");
                }
                else
                {
                    item.SetState(DownloadState.Failed);
                    item.SetProgress(item.Progress, "\u5931\u6557");
                    AppendLog($"\u8f49\u63db\u5931\u6557\uff0c\u7d50\u675f\u78bc {code}");
                }
            }

            SetStatus(successCount == pending.Count
                ? $"\u5b8c\u6210\uff0c\u5df2\u8f38\u51fa {successCount} \u500b {_outputFormat}"
                : $"\u5b8c\u6210 {successCount}/{pending.Count} \u500b\uff0c\u8acb\u67e5\u770b\u8a18\u9304");
        }
        catch (OperationCanceledException)
        {
            SetStatus("\u5df2\u53d6\u6d88");
            AppendLog("\u4f7f\u7528\u8005\u53d6\u6d88\u8f49\u63db\u3002");
            if (_activeDownload is not null && _activeDownload.State == DownloadState.Running)
            {
                _activeDownload.SetState(DownloadState.Cancelled);
            }
        }
        catch (Exception ex)
        {
            SetStatus("\u8f49\u63db\u6642\u767c\u751f\u932f\u8aa4");
            AppendLog(ex.Message);
        }
        finally
        {
            FlushLogToUi(force: true);
            _activeDownload = null;
            _conversionTokenSource?.Dispose();
            _conversionTokenSource = null;
            SetBusy(false);
            RebuildDownloadList();
        }
    }

    private async Task<int> RunYtDlpAsync(
        string ytDlpPath,
        string ffmpegPath,
        string ffprobePath,
        string url,
        string outputPath,
        string outputFormat,
        string mp4Quality,
        bool includeSubtitles,
        CancellationToken token,
        DownloadItemView? item)
    {
        _lastRunForbidden = false;
        var needsAccount = VideoNeedsAccount(url);
        FlushLogToUi(force: true);
        var logAtStart = _logText.Text?.Length ?? 0;
        var firstAttempt = await RunYtDlpAttemptAsync(
            ytDlpPath,
            ffmpegPath,
            ffprobePath,
            url,
            outputPath,
            outputFormat,
            mp4Quality,
            includeSubtitles,
            token,
            item,
            allowAutomaticBrowserCookies: true,
            allowAutomaticForAnySite: needsAccount);

        if (firstAttempt.ExitCode == 0)
        {
            return 0;
        }

        if (!needsAccount
            && !firstAttempt.UsedAutomaticBrowserCookies
            && VideoAccess.LooksLikeAccountRequired(LogSince(logAtStart)))
        {
            needsAccount = true;
            AppendLog("偵測到會員或付費限制，改用瀏覽器 Cookies 再下載。");
            SetStatus("這是會員或付費影片，正在用登入狀態重試…");
            _lastRunForbidden = false;
            firstAttempt = await RunYtDlpAttemptAsync(
                ytDlpPath,
                ffmpegPath,
                ffprobePath,
                url,
                outputPath,
                outputFormat,
                mp4Quality,
                includeSubtitles,
                token,
                item,
                allowAutomaticBrowserCookies: true,
                allowAutomaticForAnySite: true);
            if (firstAttempt.ExitCode == 0)
            {
                return 0;
            }
        }

        if (!needsAccount
            && CookieRetryPolicy.ShouldRetryWithoutAutomaticCookies(
                firstAttempt.ExitCode,
                firstAttempt.UsedAutomaticBrowserCookies))
        {
            _automaticBrowserCookiesUnavailable = true;
            AppendLog("\u700f\u89bd\u5668 Cookies \u7121\u6cd5\u7528\u65bc\u4e0b\u8f09\uff0c\u6539\u7528\u4e0d\u767b\u5165\u6a21\u5f0f\u91cd\u8a66\u3002");
            item?.SetProgress(0, "\u6539\u7528\u516c\u958b\u6a21\u5f0f\u91cd\u8a66");
            _lastRunForbidden = false;
            firstAttempt = await RunYtDlpAttemptAsync(
                ytDlpPath,
                ffmpegPath,
                ffprobePath,
                url,
                outputPath,
                outputFormat,
                mp4Quality,
                includeSubtitles,
                token,
                item,
                allowAutomaticBrowserCookies: false);
            if (firstAttempt.ExitCode == 0)
            {
                return 0;
            }
        }

        if (needsAccount && firstAttempt.ExitCode != 0 && !_lastRunForbidden)
        {
            AppendLog(VideoAccess.AccountRequiredHint());
            SetStatus(VideoAccess.AccountRequiredHint());
        }

        if (YoutubeDownloadPolicy.IsYouTubeUrl(url) && _lastRunForbidden)
        {
            AppendLog(YoutubeDownloadPolicy.ForbiddenRetryHint());
            item?.SetProgress(0, "\u76f8\u5bb9\u6a21\u5f0f\u91cd\u8a66");
            _lastRunForbidden = false;
            var fallback = await RunYtDlpAttemptAsync(
                ytDlpPath,
                ffmpegPath,
                ffprobePath,
                url,
                outputPath,
                outputFormat,
                mp4Quality,
                includeSubtitles,
                token,
                item,
                allowAutomaticBrowserCookies: true,
                youtubeFallback: true,
                allowAutomaticForAnySite: true);
            if (fallback.ExitCode != 0 && _lastRunForbidden)
            {
                AppendLog(YoutubeDownloadPolicy.ForbiddenGiveUpHint(
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));
                SetStatus(YoutubeDownloadPolicy.ForbiddenGiveUpHint(
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));
            }

            return fallback.ExitCode;
        }

        return firstAttempt.ExitCode;
    }

    private string LogSince(int mark)
    {
        FlushLogToUi(force: true);
        var text = _logText.Text ?? "";
        return text.Length <= mark ? "" : text[mark..];
    }

    private bool VideoNeedsAccount(string url)
    {
        if (_parsedInfo is null || !VideoAccess.RequiresAccount(_parsedInfo.Access))
        {
            return false;
        }

        return UrlsLikelySameVideo(_parsedInfo.Url, url)
            || UrlsLikelySameVideo(_parsedInfo.WebpageUrl, url);
    }

    private async Task<YtDlpAttemptResult> RunYtDlpAttemptAsync(
        string ytDlpPath,
        string ffmpegPath,
        string ffprobePath,
        string url,
        string outputPath,
        string outputFormat,
        string mp4Quality,
        bool includeSubtitles,
        CancellationToken token,
        DownloadItemView? item,
        bool allowAutomaticBrowserCookies,
        bool youtubeFallback = false,
        bool allowAutomaticForAnySite = false)
    {
        var startInfo = CreateYtDlpStartInfo(ytDlpPath, ffmpegPath, ffprobePath);
        AddOutputFormatArguments(startInfo, outputFormat, mp4Quality);
        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        startInfo.ArgumentList.Add("--ffmpeg-location");
        startInfo.ArgumentList.Add(IoPath.GetDirectoryName(ffmpegPath) ?? ffmpegPath);
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--retries");
        startInfo.ArgumentList.Add("8");
        startInfo.ArgumentList.Add("--fragment-retries");
        startInfo.ArgumentList.Add("8");
        startInfo.ArgumentList.Add("--concurrent-fragments");
        startInfo.ArgumentList.Add(
            YoutubeDownloadPolicy.ConcurrentFragments(url).ToString(CultureInfo.InvariantCulture));
        // Optional full playlist download; default remains single-video only.
        startInfo.ArgumentList.Add(_downloadPlaylist ? "--yes-playlist" : "--no-playlist");

        // Subtitles are handled in a separate pass so HTTP 429 on captions cannot abort media.
        if (includeSubtitles)
        {
            AddSubtitleArguments(startInfo, forEmbedDuringDownload: false);
        }

        AddYoutubeCompatibilityArguments(startInfo, url, youtubeFallback);
        AddBilibiliBrowserHeaders(startInfo, url);
        var cookieSelection = AddCookieArguments(
            startInfo,
            url,
            allowAutomaticBrowserCookies,
            allowAutomaticForAnySite);
        if (cookieSelection is not null)
        {
            AppendLog($"Cookies: {cookieSelection.Label}");
        }

        if (outputFormat == "MP3")
        {
            // YouTube/Bilibili often provide WebP covers. Embedding WebP into MP3
            // can freeze or crash Windows Media Player / Groove / some shell handlers.
            // Convert to JPEG first and force ID3v2.3 for broader player compatibility.
            startInfo.ArgumentList.Add("--convert-thumbnails");
            startInfo.ArgumentList.Add("jpg");
            startInfo.ArgumentList.Add("--embed-thumbnail");
            startInfo.ArgumentList.Add("--postprocessor-args");
            startInfo.ArgumentList.Add("ffmpeg:-id3v2_version 3");
        }

        startInfo.ArgumentList.Add("--add-metadata");
        startInfo.ArgumentList.Add("--paths");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--output");
        // Playlist mode: number files so titles don't collide across entries.
        startInfo.ArgumentList.Add(_downloadPlaylist
            ? "%(playlist_index&{} - |)s%(title)s.%(ext)s"
            : "%(title)s.%(ext)s");
        startInfo.ArgumentList.Add(NormalizeMediaUrl(url, preservePlaylistParams: _downloadPlaylist));

        var exitCode = await RunProcessAsync(startInfo, token, item);
        return new YtDlpAttemptResult(
            exitCode,
            UsedAutomaticBrowserCookies: cookieSelection?.IsAutomaticBrowser == true);
    }

    private async Task RunSubtitleDownloadAsync(
        string ytDlpPath,
        string ffmpegPath,
        string ffprobePath,
        string url,
        string outputPath,
        string outputFormat,
        CancellationToken token,
        DownloadItemView? item)
    {
        // Prefer one language per attempt to reduce YouTube rate-limit (429) hits.
        string[] languageAttempts =
        [
            "zh-Hant,zh-TW,zh-HK",
            "zh-Hans,zh-CN",
            "zh.*",
            "en"
        ];

        foreach (var langs in languageAttempts)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            // If we already have a usable caption file for this run, stop requesting more.
            if (HasAnySubtitleNearLatestMedia(outputPath, outputFormat, _lastMediaOutputPath))
            {
                AppendLog("\u5b57\u5e55: \u5df2\u6709\u53ef\u7528\u6a94\u6848\uff0c\u505c\u6b62\u7e7c\u7e8c\u8acb\u6c42");
                break;
            }

            AppendLog($"\u5b57\u5e55\u8a9e\u8a00\u5617\u8a66: {langs}");
            var startInfo = CreateYtDlpStartInfo(ytDlpPath, ffmpegPath, ffprobePath);
            startInfo.ArgumentList.Add("--skip-download");
            startInfo.ArgumentList.Add("--encoding");
            startInfo.ArgumentList.Add("utf-8");
            startInfo.ArgumentList.Add("--ffmpeg-location");
            startInfo.ArgumentList.Add(IoPath.GetDirectoryName(ffmpegPath) ?? ffmpegPath);
            startInfo.ArgumentList.Add("--newline");
            startInfo.ArgumentList.Add(_downloadPlaylist ? "--yes-playlist" : "--no-playlist");
            startInfo.ArgumentList.Add("--ignore-errors");
            startInfo.ArgumentList.Add("--retries");
            startInfo.ArgumentList.Add("3");
            startInfo.ArgumentList.Add("--sleep-subtitles");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add("--write-subs");
            startInfo.ArgumentList.Add("--write-auto-subs");
            startInfo.ArgumentList.Add("--sub-langs");
            startInfo.ArgumentList.Add(langs);
            startInfo.ArgumentList.Add("--convert-subs");
            startInfo.ArgumentList.Add("srt");
            startInfo.ArgumentList.Add("--paths");
            startInfo.ArgumentList.Add(outputPath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(_downloadPlaylist
                ? "%(playlist_index&{} - |)s%(title)s.%(ext)s"
                : "%(title)s.%(ext)s");
            AddBilibiliBrowserHeaders(startInfo, url);
            _ = AddCookieArguments(startInfo, url);
            startInfo.ArgumentList.Add(NormalizeMediaUrl(url, preservePlaylistParams: _downloadPlaylist));

            var code = await RunProcessAsync(startInfo, token, item);
            if (code == 0 && HasAnySubtitleNearLatestMedia(outputPath, outputFormat, _lastMediaOutputPath))
            {
                break;
            }

            // Brief pause between language attempts to ease 429 pressure.
            try
            {
                await Task.Delay(1500, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static ProcessStartInfo CreateYtDlpStartInfo(string ytDlpPath, string ffmpegPath, string ffprobePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        ToolLocator.PrependToPath(startInfo.Environment, ytDlpPath, ffmpegPath, ffprobePath);
        return startInfo;
    }

    private async Task<int> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken token, DownloadItemView? item)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException("\u7121\u6cd5\u555f\u52d5 yt-dlp\u3002");
        }

        var outputTask = ReadProcessStreamAsync(process.StandardOutput.BaseStream, token, item);
        var errorTask = ReadProcessStreamAsync(process.StandardError.BaseStream, token, item);

        try
        {
            await process.WaitForExitAsync(token);
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return process.ExitCode;
    }

    private static void AddSubtitleArguments(ProcessStartInfo startInfo, bool forEmbedDuringDownload)
    {
        startInfo.ArgumentList.Add("--write-subs");
        startInfo.ArgumentList.Add("--write-auto-subs");
        startInfo.ArgumentList.Add("--sub-langs");
        // Keep the primary pass small; multi-lang is handled by the separate subtitle phase.
        startInfo.ArgumentList.Add("zh-Hant,zh-Hans,en");
        startInfo.ArgumentList.Add("--convert-subs");
        startInfo.ArgumentList.Add("srt");
        startInfo.ArgumentList.Add("--ignore-errors");
        startInfo.ArgumentList.Add("--sleep-subtitles");
        startInfo.ArgumentList.Add("2");

        if (forEmbedDuringDownload)
        {
            startInfo.ArgumentList.Add("--embed-subs");
        }
    }

    private bool HasAnySubtitleNearLatestMedia(string outputDir, string format, string? mediaPathHint)
    {
        var mediaPath = ResolveMediaPath(outputDir, mediaPathHint, format);
        if (mediaPath is null)
        {
            return Directory.Exists(outputDir)
                && Directory.EnumerateFiles(outputDir, "*.*")
                    .Any(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase));
        }

        var dir = IoPath.GetDirectoryName(mediaPath) ?? outputDir;
        var stem = IoPath.GetFileNameWithoutExtension(mediaPath);
        return FindBestSubtitleForStem(dir, stem) is not null
            || Directory.GetFiles(dir, stem + "*.vtt").Length > 0
            || Directory.GetFiles(dir, stem + "*.srt").Length > 0;
    }

    /// <summary>
    /// Align external subtitle filenames with the media basename so players auto-load them
    /// (title.srt next to title.mp4 / title.mp3). For MP3 also write a simple .lrc lyrics file.
    /// For MP4, optionally soft-embed via ffmpeg when an external .srt is available.
    /// </summary>
    private void PairSubtitlesWithMedia(string outputDir, string? mediaPathHint, string format, string? ffmpegPath = null)
    {
        try
        {
            var mediaPath = ResolveMediaPath(outputDir, mediaPathHint, format);
            if (mediaPath is null || !File.Exists(mediaPath))
            {
                AppendLog("\u5b57\u5e55\u5c0d\u9f4a: \u627e\u4e0d\u5230\u5f71\u97f3\u6a94\uff0c\u8df3\u904e");
                return;
            }

            var dir = IoPath.GetDirectoryName(mediaPath) ?? outputDir;
            var stem = IoPath.GetFileNameWithoutExtension(mediaPath);
            var pairedSrt = IoPath.Combine(dir, stem + ".srt");

            // Convert leftover .vtt files (when convert-subs was interrupted by 429).
            ConvertMatchingVttToSrt(dir, stem);

            var bestSub = FindBestSubtitleForStem(dir, stem);
            if (bestSub is null)
            {
                AppendLog("\u5b57\u5e55\u5c0d\u9f4a: \u6c92\u6709\u53ef\u7528\u5b57\u5e55\uff08\u53ef\u80fd\u88ab\u9650\u6d41 429 \u6216\u5e73\u53f0\u672a\u63d0\u4f9b\uff09");
                return;
            }

            if (!string.Equals(bestSub, pairedSrt, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(bestSub, pairedSrt, overwrite: true);
                AppendLog($"\u5b57\u5e55\u5c0d\u9f4a: {IoPath.GetFileName(bestSub)} -> {IoPath.GetFileName(pairedSrt)}");
            }
            else
            {
                AppendLog($"\u5b57\u5e55\u5c0d\u9f4a: {IoPath.GetFileName(pairedSrt)}");
            }

            if (string.Equals(format, "MP4", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(ffmpegPath)
                && File.Exists(ffmpegPath))
            {
                if (TryEmbedSubtitlesIntoMp4(ffmpegPath, mediaPath, pairedSrt))
                {
                    AppendLog("MP4: \u5df2\u5167\u5d4c\u5b57\u5e55\u8ecc\u9053\uff08\u64ad\u653e\u5668\u53ef\u958b\u555f\uff09");
                }
                else
                {
                    AppendLog("MP4: \u5167\u5d4c\u5931\u6557\uff0c\u4ecd\u4fdd\u7559\u5916\u639b .srt");
                }
            }

            if (string.Equals(format, "MP3", StringComparison.OrdinalIgnoreCase))
            {
                var lrcPath = IoPath.Combine(dir, stem + ".lrc");
                if (TryWriteLrcFromSrt(pairedSrt, lrcPath))
                {
                    AppendLog($"\u6b4c\u8a5e .lrc: {IoPath.GetFileName(lrcPath)}");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\u5b57\u5e55\u5c0d\u9f4a\u5931\u6557: {ex.Message}");
        }
    }

    private void ConvertMatchingVttToSrt(string dir, string stem)
    {
        foreach (var vtt in Directory.GetFiles(dir, stem + "*.vtt"))
        {
            var srt = IoPath.ChangeExtension(vtt, ".srt");
            if (File.Exists(srt))
            {
                continue;
            }

            if (TryConvertVttToSrt(vtt, srt))
            {
                AppendLog($"VTT -> SRT: {IoPath.GetFileName(srt)}");
            }
        }
    }

    private static bool TryConvertVttToSrt(string vttPath, string srtPath)
    {
        try
        {
            var raw = File.ReadAllText(vttPath);
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var output = new List<string>();
            var index = 1;
            var i = 0;
            while (i < lines.Length && (lines[i].StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(lines[i])
                || lines[i].StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)
                || lines[i].Contains("-->") == false && i < 5))
            {
                // Skip header until first cue; fall through carefully below.
                if (lines[i].Contains("-->", StringComparison.Ordinal))
                {
                    break;
                }

                i++;
            }

            while (i < lines.Length)
            {
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                {
                    i++;
                }

                if (i >= lines.Length)
                {
                    break;
                }

                // Optional cue identifier line.
                if (!lines[i].Contains("-->", StringComparison.Ordinal) && i + 1 < lines.Length && lines[i + 1].Contains("-->", StringComparison.Ordinal))
                {
                    i++;
                }

                if (i >= lines.Length || !lines[i].Contains("-->", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                var timing = lines[i]
                    .Replace('.', ',')
                    .Split(new[] { " --> " }, StringSplitOptions.None);
                if (timing.Length < 2)
                {
                    i++;
                    continue;
                }

                // Strip VTT positioning settings after timestamp.
                var start = timing[0].Trim();
                var end = timing[1].Split(' ')[0].Trim();
                i++;

                var textLines = new List<string>();
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !lines[i].Contains("-->", StringComparison.Ordinal))
                {
                    var cleaned = Regex.Replace(lines[i], @"</?[^>]+>", "");
                    textLines.Add(cleaned.Trim());
                    i++;
                }

                if (textLines.Count == 0)
                {
                    continue;
                }

                output.Add(index.ToString(CultureInfo.InvariantCulture));
                output.Add($"{start} --> {end}");
                output.AddRange(textLines);
                output.Add("");
                index++;
            }

            if (output.Count == 0)
            {
                return false;
            }

            File.WriteAllText(srtPath, string.Join("\n", output), new UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryEmbedSubtitlesIntoMp4(string ffmpegPath, string mediaPath, string srtPath)
    {
        try
        {
            var tempPath = mediaPath + ".sub.tmp.mp4";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            // Do NOT redirect stdout/stderr — full buffers + WaitForExit deadlocks easily.
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(mediaPath);
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(srtPath);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");
            startInfo.ArgumentList.Add("-c:s");
            startInfo.ArgumentList.Add("mov_text");
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("1:0");
            startInfo.ArgumentList.Add("-metadata:s:s:0");
            startInfo.ArgumentList.Add("language=zho");
            startInfo.ArgumentList.Add(tempPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(180_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore kill failures
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                AppendLog("ffmpeg embed: timed out");
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                return false;
            }

            // Replace original only after a successful embed write.
            var backupPath = mediaPath + ".bak";
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(mediaPath, backupPath);
                File.Move(tempPath, mediaPath);
                File.Delete(backupPath);
            }
            catch
            {
                // Roll back if replace fails.
                if (!File.Exists(mediaPath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, mediaPath);
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"ffmpeg embed: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveMediaPath(string outputDir, string? mediaPathHint, string format)
    {
        var wantMp3 = string.Equals(format, "MP3", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(mediaPathHint))
        {
            if (wantMp3)
            {
                // yt-dlp often logs the pre-convert audio path first (.m4a/.webm). Prefer sibling .mp3.
                if (mediaPathHint.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(mediaPathHint))
                {
                    return mediaPathHint;
                }

                var siblingMp3 = IoPath.ChangeExtension(mediaPathHint, ".mp3");
                if (File.Exists(siblingMp3))
                {
                    return siblingMp3;
                }
            }

            if (File.Exists(mediaPathHint))
            {
                return mediaPathHint;
            }
        }

        if (!Directory.Exists(outputDir))
        {
            return null;
        }

        var ext = wantMp3 ? ".mp3" : ".mp4";
        return Directory.GetFiles(outputDir, "*" + ext)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Rebuild a clean, shell-openable MP3. Some players refuse files with broken
    /// cover-art tags (WebP APIC) or nonstandard streams left by post-processors.
    /// </summary>
    private async Task<string?> SanitizeMp3ForPlaybackAsync(string ffmpegPath, string mp3Path)
    {
        try
        {
            if (!File.Exists(mp3Path) || !File.Exists(ffmpegPath))
            {
                return mp3Path;
            }

            var dir = IoPath.GetDirectoryName(mp3Path) ?? IoPath.GetTempPath();
            var tempPath = IoPath.Combine(dir, IoPath.GetFileNameWithoutExtension(mp3Path) + ".openable.tmp.mp3");
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            // Audio-only remux/re-encode with ID3v2.3 — widely accepted by Windows players.
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(mp3Path);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:a:0");
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("libmp3lame");
            startInfo.ArgumentList.Add("-q:a");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add("-vn");
            startInfo.ArgumentList.Add("-id3v2_version");
            startInfo.ArgumentList.Add("3");
            startInfo.ArgumentList.Add("-write_id3v1");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add(tempPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return mp3Path;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length < 1024)
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                AppendLog("MP3 \u6e05\u7406\u5931\u6557\uff0c\u4ecd\u4f7f\u7528\u539f\u6a94");
                return mp3Path;
            }

            var backupPath = mp3Path + ".bak";
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(mp3Path, backupPath);
                File.Move(tempPath, mp3Path);
                File.Delete(backupPath);
                AppendLog("MP3: \u5df2\u91cd\u5efa\u53ef\u64ad\u653e\u6a94\uff08\u76f8\u5bb9 Windows \u64ad\u653e\u5668\uff09");
                return mp3Path;
            }
            catch
            {
                if (!File.Exists(mp3Path) && File.Exists(backupPath))
                {
                    File.Move(backupPath, mp3Path);
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                return File.Exists(mp3Path) ? mp3Path : null;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"MP3 \u6e05\u7406\u7570\u5e38: {ex.Message}");
            return File.Exists(mp3Path) ? mp3Path : null;
        }
    }

    private static string? FindBestSubtitleForStem(string dir, string stem)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        // Match: Title.srt, Title.zh-Hant.srt, Title.zh-Hans-en.srt, etc.
        var candidates = Directory.GetFiles(dir, "*.srt")
            .Where(path =>
            {
                var name = IoPath.GetFileNameWithoutExtension(path);
                return name.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(path => ScoreSubtitlePath(path, stem))
            .ThenBy(path => IoPath.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int ScoreSubtitlePath(string path, string stem)
    {
        var name = IoPath.GetFileNameWithoutExtension(path);
        if (name.Equals(stem, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var suffix = name.Length > stem.Length
            ? name[(stem.Length + (name.Length > stem.Length && name[stem.Length] == '.' ? 1 : 0))..]
            : name;

        for (var i = 0; i < PreferredSubLangTokens.Length; i++)
        {
            if (suffix.Contains(PreferredSubLangTokens[i], StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 100;
    }

    private static bool TryWriteLrcFromSrt(string srtPath, string lrcPath)
    {
        try
        {
            var text = File.ReadAllText(srtPath);
            var blocks = Regex.Split(text.Replace("\r\n", "\n"), @"\n\s*\n");
            var lines = new List<string> { "[ti:]", "[ar:]", "[by:YoutubeBilibiliConverter]" };

            foreach (var block in blocks)
            {
                var parts = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                // SRT: index, then "00:00:01,000 --> 00:00:04,000", then text lines
                var timeLineIndex = parts[0].Contains("-->", StringComparison.Ordinal) ? 0 : 1;
                if (timeLineIndex >= parts.Length || !parts[timeLineIndex].Contains("-->", StringComparison.Ordinal))
                {
                    continue;
                }

                var timePart = parts[timeLineIndex].Split("-->", 2, StringSplitOptions.TrimEntries)[0];
                if (!TryParseSrtTimestamp(timePart, out var ts))
                {
                    continue;
                }

                var content = string.Join(" ", parts.Skip(timeLineIndex + 1)).Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                lines.Add($"[{ts.Minutes + (int)ts.TotalHours * 60:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}]{content}");
            }

            if (lines.Count <= 3)
            {
                return false;
            }

            File.WriteAllText(lrcPath, string.Join(Environment.NewLine, lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseSrtTimestamp(string value, out TimeSpan ts)
    {
        ts = default;
        // 00:00:01,000 or 00:00:01.000
        value = value.Trim().Replace(',', '.');
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out ts);
    }

    private static void AddOutputFormatArguments(ProcessStartInfo startInfo, string outputFormat, string mp4Quality)
    {
        if (outputFormat == "MP4")
        {
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add(YoutubeDownloadPolicy.GetMp4FormatSelector(mp4Quality));
            startInfo.ArgumentList.Add("--merge-output-format");
            startInfo.ArgumentList.Add("mp4");
            return;
        }

        startInfo.ArgumentList.Add("--extract-audio");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("0");
    }

    private void AddYoutubeCompatibilityArguments(ProcessStartInfo startInfo, string url, bool fallback)
    {
        if (!YoutubeDownloadPolicy.IsYouTubeUrl(url))
        {
            return;
        }

        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add(fallback
            ? YoutubeDownloadPolicy.FallbackExtractorArgs
            : YoutubeDownloadPolicy.StandardExtractorArgs);
        startInfo.ArgumentList.Add("--force-ipv4");
        AddJsRuntimeArguments(startInfo);
        if (fallback)
        {
            startInfo.ArgumentList.Add("--rm-cache-dir");
        }
    }

    private void AddJsRuntimeArguments(ProcessStartInfo startInfo)
    {
        foreach (var name in new[] { "deno", "node" })
        {
            var path = ToolLocator.FindExecutable(name);
            if (path is null)
            {
                continue;
            }

            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add($"{name}:{path}");
        }
    }

    private static void AddBilibiliBrowserHeaders(ProcessStartInfo startInfo, string url)
    {
        if (!IsBilibiliVideoUrl(url))
        {
            return;
        }

        startInfo.ArgumentList.Add("--add-headers");
        startInfo.ArgumentList.Add("User-Agent:Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        startInfo.ArgumentList.Add("--add-headers");
        startInfo.ArgumentList.Add("Referer:https://www.bilibili.com/");
        startInfo.ArgumentList.Add("--add-headers");
        startInfo.ArgumentList.Add("Accept-Language:zh-CN,zh-TW;q=0.9,zh;q=0.8,en;q=0.7");
    }

    private CookieSelection? AddCookieArguments(
        ProcessStartInfo startInfo,
        string url,
        bool allowAutomaticBrowserCookies = true,
        bool allowAutomaticForAnySite = false)
    {
        if (!string.IsNullOrEmpty(_cookiesFilePath) && File.Exists(_cookiesFilePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(_cookiesFilePath);
            return new CookieSelection($"\u6a94\u6848 {IoPath.GetFileName(_cookiesFilePath)}", IsAutomaticBrowser: false);
        }

        if (!CookieRetryPolicy.ShouldUseAutomaticCookies(
                allowAutomaticBrowserCookies,
                _automaticBrowserCookiesUnavailable,
                IsBilibiliVideoUrl(url),
                allowAutomaticForAnySite))
        {
            return null;
        }

        var browser = FindBrowserWithCookies();
        if (browser is null)
        {
            return null;
        }

        startInfo.ArgumentList.Add("--cookies-from-browser");
        startInfo.ArgumentList.Add(browser);
        return new CookieSelection($"\u700f\u89bd\u5668 {browser}", IsAutomaticBrowser: true);
    }

    private static string? FindBrowserWithCookies()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return BrowserCookieLocator.FindWindowsBrowser(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return BrowserCookieLocator.FindMacBrowser(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        return null;
    }

    private static string NormalizeMediaUrl(string url, bool preservePlaylistParams = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        // Pure playlist pages always keep list= so optional full-playlist download works.
        var isPurePlaylist = IsYouTubePlaylistPageUri(uri);

        // Strip tracking params. Playlist params (list/index/...) are kept only when
        // the user opts into full playlist download, or for pure /playlist URLs.
        if (IsBilibiliVideoUri(uri) || IsYouTubeWatchUri(uri) || isPurePlaylist)
        {
            var cleaned = RemoveTrackingQueryParameters(
                uri.Query,
                preservePlaylistParams: preservePlaylistParams || isPurePlaylist);
            var builder = new UriBuilder(uri)
            {
                Query = cleaned
            };
            return builder.Uri.ToString();
        }

        return url;
    }

    private static bool IsYouTubeWatchUri(Uri uri)
    {
        var host = uri.Host;
        if (!(host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
              || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
              || host.Contains("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Pure playlist pages are handled separately.
        if (IsYouTubePlaylistPageUri(uri))
        {
            return false;
        }

        return true;
    }

    private static bool IsYouTubePlaylistPageUri(Uri uri) =>
        (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
         || uri.Host.Contains("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
        && uri.AbsolutePath.StartsWith("/playlist", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePlaylistUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Contains("list=", StringComparison.OrdinalIgnoreCase)
                || url.Contains("/playlist", StringComparison.OrdinalIgnoreCase);
        }

        if (IsYouTubePlaylistPageUri(uri))
        {
            return true;
        }

        var list = GetQueryParameter(uri, "list");
        return !string.IsNullOrWhiteSpace(list);
    }

    private static bool UrlsLikelySameVideo(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Compare after stripping playlist/tracking params.
        return string.Equals(
            NormalizeMediaUrl(a, preservePlaylistParams: false),
            NormalizeMediaUrl(b, preservePlaylistParams: false),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBilibiliVideoUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsBilibiliVideoUri(uri);

    private static bool IsBilibiliVideoUri(Uri uri) =>
        uri.Host.EndsWith("bilibili.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/video/", StringComparison.OrdinalIgnoreCase);

    private static string RemoveTrackingQueryParameters(string query, bool preservePlaylistParams = false)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "";
        }

        // Always drop tracking/share noise.
        var dropKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "spm_id_from", "from_spmid", "vd_source", "share_source",
            "share_medium", "share_plat", "share_session_id", "unique_k",
            "si", "feature", "ab_channel", "t"
        };

        // When not downloading full playlists, also drop list attachment params
        // so watch?v=ID&list=PL... becomes a single-video URL.
        if (!preservePlaylistParams)
        {
            dropKeys.Add("list");
            dropKeys.Add("index");
            dropKeys.Add("start_radio");
            dropKeys.Add("pp");
            dropKeys.Add("playnext");
        }

        var kept = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
            {
                var key = parameter.Split('=', 2)[0];
                return !dropKeys.Contains(Uri.UnescapeDataString(key));
            });

        return string.Join("&", kept);
    }

    private async Task ReadProcessStreamAsync(Stream stream, CancellationToken token, DownloadItemView? item)
    {
        var buffer = new byte[8192];
        var pending = new List<byte>(1024);

        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var value = buffer[index];
                if (value == (byte)'\n')
                {
                    AppendDecodedLogLine(pending, item);
                    pending.Clear();
                    continue;
                }

                // Cap runaway line length (binary noise / missing newlines).
                if (pending.Count < 32_768)
                {
                    pending.Add(value);
                }
            }
        }

        AppendDecodedLogLine(pending, item);
    }

    private void AppendDecodedLogLine(List<byte> bytes, DownloadItemView? item)
    {
        while (bytes.Count > 0 && bytes[^1] == (byte)'\r')
        {
            bytes.RemoveAt(bytes.Count - 1);
        }

        if (bytes.Count == 0)
        {
            return;
        }

        var line = DecodeProcessText(bytes.ToArray());
        TryCaptureMediaPath(line);

        // Progress lines fire many times per second — update bar only, do not flood the log.
        if (ProgressRegex.IsMatch(line))
        {
            TryUpdateProgress(line, item);
            return;
        }

        if (line.Contains("[Merger]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
        {
            NotifyShellProcessing();
        }

        AppendLog(line);
        TryUpdateProgress(line, item);

        if (YoutubeDownloadPolicy.LooksLikeHttpForbidden(line))
        {
            _lastRunForbidden = true;
        }

        if (!_warnedOutdatedYtDlp && YoutubeDownloadPolicy.LooksLikeOutdatedYtDlp(line))
        {
            _warnedOutdatedYtDlp = true;
            AppendLog(YoutubeDownloadPolicy.OutdatedYtDlpHint(
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));
        }

        if (line.Contains("HTTP Error 412", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Precondition Failed", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Bilibili returned HTTP 412. Check region/login limits or browser cookies.");
        }

        if (line.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("\u63d0\u793a: YouTube \u5b57\u5e55\u9650\u6d41 (429)\u3002\u5f71\u97f3\u4ecd\u6703\u4e0b\u8f09\uff1b\u5b57\u5e55\u6703\u81ea\u52d5\u63db\u8a9e\u8a00\u91cd\u8a66\u6216\u7a0d\u5f8c\u518d\u8a66\u3002");
        }
    }

    private void TryCaptureMediaPath(string line)
    {
        var match = DestinationRegex.Match(line);
        if (!match.Success)
        {
            return;
        }

        var path = match.Groups["path"].Value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Prefer final media container paths.
        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            _lastMediaOutputPath = path;
        }
    }

    private void TryUpdateProgress(string line, DownloadItemView? item)
    {
        var match = ProgressRegex.Match(line);
        if (!match.Success)
        {
            return;
        }

        if (!double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return;
        }

        var speed = match.Groups["speed"].Value;
        _lastSpeed = speed;

        var now = DateTime.UtcNow;
        if ((now - _lastProgressUiUtc).TotalMilliseconds < ProgressUiIntervalMs && percent < 99.5)
        {
            return;
        }

        _lastProgressUiUtc = now;
        UpdateFooter();
        item?.SetProgress(percent, $"{percent:0.#}%  ({speed})");
        NotifyShellProgress(percent, speed);
    }

    private static string DecodeProcessText(byte[] bytes)
    {
        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SystemAnsiEncoding.GetString(bytes);
        }
    }

    private static Encoding GetSystemAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private void CheckTools()
    {
        var ytDlp = ToolLocator.FindExecutable("yt-dlp");
        var ffmpeg = ToolLocator.FindExecutable("ffmpeg");
        var ffprobe = ToolLocator.FindExecutable("ffprobe");

        if (!RefreshToolSetup())
        {
            SetStatus("\u7b2c\u4e00\u6b21\u4f7f\u7528\uff1a\u8acb\u5148\u4f9d\u4e0a\u65b9\u6307\u5f15\u5b89\u88dd\u914d\u5957\u5de5\u5177 yt-dlp \u8207 ffmpeg");
            AppendInstallHint();
            AppendLog($"yt-dlp: {ytDlp ?? "\u627e\u4e0d\u5230"}");
            AppendLog($"ffmpeg: {ffmpeg ?? "\u627e\u4e0d\u5230"}");
            AppendLog($"ffprobe: {ffprobe ?? "\u627e\u4e0d\u5230"}");
            return;
        }

        AppendLog($"yt-dlp: {ytDlp}");
        AppendLog($"ffmpeg: {ffmpeg}");
        AppendLog($"ffprobe: {ffprobe}");
        SetStatus("\u6e96\u5099\u5c31\u7dd2 \u00b7 \u5de5\u5177\u5df2\u5c31\u7dd2");
    }

    private void AppendInstallHint()
    {
        var os = ToolSetupGuide.CurrentOs;
        AppendLog($"\u5b89\u88dd\u6307\u4ee4: {ToolSetupGuide.GetInstallCommand(os, os == "osx" && ToolSetupGuide.HasHomebrew())}");
        if (!RefreshToolSetup())
        {
            _toolSetupPanel.BringIntoView();
        }
    }

    /// <summary>
    /// Re-detects yt-dlp / ffmpeg and shows or hides the install guide.
    /// Returns true when every required tool is available.
    /// </summary>
    private bool RefreshToolSetup()
    {
        var ytDlp = ToolLocator.FindExecutable("yt-dlp");
        var ffmpeg = ToolLocator.FindExecutable("ffmpeg");
        var ffprobe = ToolLocator.FindExecutable("ffprobe");
        var ready = ytDlp is not null && ffmpeg is not null && ffprobe is not null;
        var wasMissing = _toolsMissing;
        _toolsMissing = !ready;
        var changed = wasMissing != _toolsMissing;

        if (ready)
        {
            _toolSetupPanel.IsVisible = false;
            if (wasMissing)
            {
                HideParseError();
                SetStatus("\u914d\u5957\u5de5\u5177\u5b89\u88dd\u5b8c\u6210\uff0c\u53ef\u4ee5\u958b\u59cb\u8f49\u63db\u4e86");
                AppendLog($"yt-dlp: {ytDlp}");
                AppendLog($"ffmpeg: {ffmpeg}");
                AppendLog($"ffprobe: {ffprobe}");
            }

            OnShellToolStateChanged(changed);
            return true;
        }

        BuildToolSetupContent(ytDlp is not null, ffmpeg is not null && ffprobe is not null);
        _toolSetupPanel.IsVisible = true;
        OnShellToolStateChanged(changed);
        return false;
    }

    private void BuildToolSetupContent(bool hasYtDlp, bool hasFfmpeg)
    {
        var os = ToolSetupGuide.CurrentOs;
        var hasHomebrew = os == "osx" && ToolSetupGuide.HasHomebrew();
        var command = ToolSetupGuide.GetInstallCommand(os, hasHomebrew);

        _toolSetupBody.Children.Clear();
        _toolSetupBody.Children.Add(new TextBlock
        {
            Text = "\u958b\u59cb\u4f7f\u7528\u524d\uff1a\u5b89\u88dd\u914d\u5957\u5de5\u5177",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#92400E")
        });
        _toolSetupBody.Children.Add(new TextBlock
        {
            Text = "\u8f49\u63db\u9700\u8981\u5169\u500b\u514d\u8cbb\u5de5\u5177\uff1ayt-dlp\uff08\u8b80\u53d6\u5f71\u7247\uff09\u8207 ffmpeg\uff08\u8f49\u6210 MP3 / MP4\uff09\u3002\u53ea\u9700\u5b89\u88dd\u4e00\u6b21\uff0c\u4e4b\u5f8c\u5c31\u80fd\u76f4\u63a5\u4f7f\u7528\u3002",
            FontSize = 13,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(ToolStatusChip("yt-dlp", hasYtDlp));
        chips.Children.Add(ToolStatusChip("ffmpeg", hasFfmpeg));
        _toolSetupBody.Children.Add(chips);

        var steps = ToolSetupGuide.GetSteps(os, hasHomebrew);
        var stepsPanel = new StackPanel { Spacing = 4 };
        for (var i = 0; i < steps.Length; i++)
        {
            stepsPanel.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {steps[i]}",
                FontSize = 13,
                Foreground = TextPrimary,
                TextWrapping = TextWrapping.Wrap
            });
        }
        _toolSetupBody.Children.Add(stepsPanel);

        _toolSetupBody.Children.Add(new TextBlock
        {
            Text = ToolSetupGuide.SupportsOneClickInstall(os)
                ? "\u60f3\u81ea\u5df1\u64cd\u4f5c\uff1a\u8907\u88fd\u4e0b\u65b9\u6307\u4ee4\uff0c\u8cbc\u5230\u300c\u7d42\u7aef\u6a5f\u300d\u5f8c\u6309 Enter\u3002"
                : "\u5b89\u88dd\u6307\u4ee4\uff1a",
            FontSize = 12,
            Foreground = TextMuted
        });
        _toolSetupBody.Children.Add(new TextBox
        {
            Text = command,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(PlatformCopy.MonoFontFamily),
            FontSize = 12,
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#FDE68A")
        });

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        if (ToolSetupGuide.SupportsOneClickInstall(os))
        {
            var installButton = CreatePrimaryButton("\u4e00\u9375\u5b89\u88dd", 120);
            installButton.Margin = new Thickness(0, 0, 10, 0);
            installButton.Click += (_, _) => LaunchToolInstaller();
            buttons.Children.Add(installButton);
        }

        var copyButton = CreateSoftButton("\u8907\u88fd\u6307\u4ee4", 100);
        copyButton.Margin = new Thickness(0, 0, 10, 0);
        copyButton.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null)
                {
                    SetStatus("\u8907\u88fd\u5931\u6557\uff0c\u8acb\u624b\u52d5\u9078\u53d6\u6307\u4ee4");
                    return;
                }

                await clipboard.SetTextAsync(command);
                SetStatus("\u5df2\u8907\u88fd\u5b89\u88dd\u6307\u4ee4\uff0c\u8acb\u8cbc\u5230\u7d42\u7aef\u6a5f\u57f7\u884c");
            }
            catch (Exception ex)
            {
                SetStatus("\u8907\u88fd\u5931\u6557\uff0c\u8acb\u624b\u52d5\u9078\u53d6\u6307\u4ee4");
                AppendLog(ex.Message);
            }
        };
        buttons.Children.Add(copyButton);

        var recheckButton = CreateSoftButton("\u91cd\u65b0\u6aa2\u67e5", 100);
        recheckButton.Click += (_, _) =>
        {
            if (!RefreshToolSetup())
            {
                SetStatus("\u5c1a\u672a\u5075\u6e2c\u5230\u5168\u90e8\u5de5\u5177\uff1b\u8acb\u78ba\u8a8d\u5b89\u88dd\u8996\u7a97\u5df2\u986f\u793a\u5b8c\u6210\u5f8c\u518d\u6309\u4e00\u6b21");
            }
        };
        buttons.Children.Add(recheckButton);
        _toolSetupBody.Children.Add(buttons);
    }

    private static Border ToolStatusChip(string name, bool installed) => new()
    {
        Background = installed ? GreenSoft : Brush.Parse("#FEE4E2"),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(10, 4),
        Child = new TextBlock
        {
            Text = installed ? $"\u2713 {name} \u5df2\u5b89\u88dd" : $"\u2717 {name} \u672a\u5b89\u88dd",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = installed ? Brush.Parse("#15803D") : Brush.Parse("#B42318")
        }
    };

    private void LaunchToolInstaller()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script = IoPath.Combine(IoPath.GetTempPath(), "install-converter-tools.command");
                File.WriteAllText(script, ToolSetupGuide.BuildMacInstallScript());
                File.SetUnixFileMode(script,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { "-a", "Terminal", script },
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var command = ToolSetupGuide.GetInstallCommand("windows", hasHomebrew: false);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"{command}; Write-Host ''; Write-Host '\u5b8c\u6210\u5f8c\u8acb\u95dc\u9589\u6b64\u8996\u7a97\uff0c\u56de\u5230\u5f71\u97f3\u8f49\u63db\u5927\u5e2b\u6309\u300c\u91cd\u65b0\u6aa2\u67e5\u300d\u3002'\"",
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            else
            {
                return;
            }

            SetStatus("\u5df2\u958b\u555f\u5b89\u88dd\u8996\u7a97\uff0c\u8acb\u4f9d\u756b\u9762\u6307\u793a\u5b8c\u6210\u5b89\u88dd");
            AppendLog("\u5df2\u555f\u52d5\u914d\u5957\u5de5\u5177\u5b89\u88dd\u7a0b\u5e8f");
        }
        catch (System.ComponentModel.Win32Exception ex) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Declining the administrator prompt lands here.
            SetStatus("\u5b89\u88dd\u5df2\u53d6\u6d88\uff1b\u4e5f\u53ef\u4ee5\u6309\u300c\u8907\u88fd\u6307\u4ee4\u300d\u81ea\u884c\u57f7\u884c");
            AppendLog(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("\u7121\u6cd5\u81ea\u52d5\u958b\u555f\u5b89\u88dd\u8996\u7a97\uff0c\u8acb\u6309\u300c\u8907\u88fd\u6307\u4ee4\u300d\u81ea\u884c\u57f7\u884c");
            AppendLog(ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        if (busy)
        {
            _shellSawDownload = true;
        }

        _parseButton.IsEnabled = !busy;
        _pasteButton.IsEnabled = !busy;
        _browseButton.IsEnabled = !busy;
        _qualityCombo.IsEnabled = !busy && _outputFormat == "MP4";
        _subtitleCheckBox.IsEnabled = !busy;
        _playlistCheckBox.IsEnabled = !busy;
        _urlBox.IsReadOnly = busy;
        _outputBox.IsReadOnly = busy;
        _clearQueueButton.IsEnabled = !busy;
        _convertButton.Content = busy ? "\u53d6\u6d88\u8f49\u63db" : "\u958b\u59cb\u8f49\u63db";
        _convertButton.Background = busy ? Brush.Parse("#EF4444") : Green;
    }

    private void RebuildDownloadList()
    {
        RefreshQueueSections();
        _downloadListPanel.Children.Clear();
        _queueCountText.Text = $"\u4e0b\u8f09\u6e05\u55ae ({_downloadItems.Count})";

        if (_downloadItems.Count == 0)
        {
            _downloadListPanel.Children.Add(new TextBlock
            {
                Text = "\u5c1a\u7121\u4e0b\u8f09\u9805\u76ee\u3002\u89e3\u6790\u7db2\u5740\u5f8c\u6309\u300c\u958b\u59cb\u8f49\u63db\u300d\u5373\u53ef\u52a0\u5165\u6e05\u55ae\u3002",
                FontSize = 12,
                Foreground = TextMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6)
            });
            return;
        }

        foreach (var item in _downloadItems.AsEnumerable().Reverse().Take(8))
        {
            DetachFromParent(item.Root);
            _downloadListPanel.Children.Add(item.Root);
        }
    }

    private void SaveSettingsIfPossible()
    {
        var outputPath = _outputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
        {
            outputPath = AppSettings.GetDefaultOutputFolder();
        }

        AppSettings.Save(
            outputPath,
            1,
            _outputFormat,
            _mp4Quality,
            _todayDownloads,
            DateOnly.FromDateTime(DateTime.Now),
            _includeSubtitles,
            _cookiesFilePath,
            _downloadPlaylist,
            _recentSearches
                .Select(e => new RecentSearchSetting
                {
                    Query = e.Query,
                    Platform = e.Platform,
                    SearchedAtUtc = e.SearchedAtUtc
                })
                .ToList(),
            _shellTheme.ToString().ToLowerInvariant());
    }

    private static string NormalizeMp4Quality(string? quality) =>
        YoutubeDownloadPolicy.NormalizeQuality(quality);

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null || seconds < 0)
        {
            return "-";
        }

        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private string BuildFooterStats() =>
        $"\u4eca\u65e5\u4e0b\u8f09\uff1a{_todayDownloads} \u500b\u6a94\u6848    \u901f\u5ea6\uff1a{_lastSpeed}";

    private void UpdateFooter()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastFooterUiUtc).TotalMilliseconds < ProgressUiIntervalMs)
        {
            return;
        }

        _lastFooterUiUtc = now;
        var text = BuildFooterStats();
        Dispatcher.UIThread.Post(() =>
        {
            if (_footerStats.Text != text)
            {
                _footerStats.Text = text;
            }
        }, DispatcherPriority.Background);
    }

    private void SetStatus(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            _statusText.Text = text;
            if (_shellStatusLine is not null)
            {
                _shellStatusLine.Text = text;
            }
        }, DispatcherPriority.Normal);

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_logLock)
        {
            _pendingLogChunk.AppendLine(line);
            if (_logFlushScheduled)
            {
                return;
            }

            _logFlushScheduled = true;
        }

        // Batch many log lines into one UI update to keep the window responsive.
        Dispatcher.UIThread.Post(() => FlushLogToUi(force: false), DispatcherPriority.Background);
    }

    private void FlushLogToUi(bool force)
    {
        string chunk;
        lock (_logLock)
        {
            if (_pendingLogChunk.Length == 0)
            {
                if (force)
                {
                    _logFlushScheduled = false;
                }

                return;
            }

            chunk = _pendingLogChunk.ToString();
            _pendingLogChunk.Clear();
            _logFlushScheduled = false;
        }

        void Apply()
        {
            try
            {
                var current = _logText.Text ?? "";
                var combined = current + chunk;
                if (combined.Length > MaxLogCharacters)
                {
                    combined = combined[^MaxLogCharacters..];
                    // Avoid cutting mid-line when possible.
                    var firstNl = combined.IndexOf('\n');
                    if (firstNl >= 0 && firstNl < combined.Length - 1)
                    {
                        combined = combined[(firstNl + 1)..];
                    }
                }

                _logText.Text = combined;
                // Avoid CaretIndex thrash (was a major freeze source on large logs).
            }
            catch
            {
                // Never let log UI updates crash conversion.
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
        }
    }

    private async Task WarnIfDownloadedDurationIsShortAsync(
        string ffprobePath,
        string outputDir,
        string format,
        string url)
    {
        try
        {
            var mediaPath = ResolveMediaPath(outputDir, _lastMediaOutputPath, format);
            if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            {
                return;
            }

            var actual = await ProbeMediaDurationSecondsAsync(ffprobePath, mediaPath);
            if (actual is null or <= 0)
            {
                return;
            }

            AppendLog($"\u5be6\u969b\u6a94\u6848\u6642\u9577: {FormatDuration(actual)} ({IoPath.GetFileName(mediaPath)})");

            double? expected = null;
            if (_parsedInfo is not null
                && (string.Equals(_parsedInfo.Url, url, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(_parsedInfo.WebpageUrl)
                        && string.Equals(_parsedInfo.WebpageUrl, url, StringComparison.OrdinalIgnoreCase))))
            {
                expected = _parsedInfo.ExpectedDurationSeconds ?? _parsedInfo.DurationSeconds;
            }

            // Bilibili charge-exclusive free preview is often ~10 minutes of a longer video.
            if (expected is > 30 && actual + 20 < expected * 0.85)
            {
                var warning =
                    $"\u8b66\u544a\uff1a\u4e0b\u8f09\u6a94\u6642\u9577 ({FormatDuration(actual)}) \u660e\u986f\u77ed\u65bc\u5f71\u7247\u5ba3\u7a31\u6642\u9577 ({FormatDuration(expected)})\u3002"
                    + (IsBilibiliVideoUrl(url)
                        ? " \u9019\u901a\u5e38\u662f B \u7ad9\u300c\u5145\u96fb\u5c08\u5c6c\u300d\u8a66\u770b\u7247\u6bb5\uff1b\u82e5\u8981\u5b8c\u6574\u7248\uff0c\u8acb\u5148\u5c0d\u8a72 UP \u5305\u6708\u5145\u96fb\uff0c\u4e26\u78ba\u8a8d\u7528\u5df2\u767b\u5165\u7684\u700f\u89bd\u5668 cookies \u518d\u4e0b\u8f09\u3002"
                        : " \u53ef\u80fd\u70ba\u6703\u54e1/\u5340\u57df/\u6b0a\u9650\u9650\u5236\u4e0b\u7684\u9810\u89bd\u7247\u6bb5\u3002");
                AppendLog(warning);
                SetStatus(
                    $"\u5b8c\u6210\uff08\u4f46\u50c5\u8a66\u770b {FormatDuration(actual)} / \u5ba3\u7a31 {FormatDuration(expected)}\uff09");
            }
            else if (IsBilibiliVideoUrl(url)
                     && _parsedInfo?.IsPreviewOnly == true
                     && _parsedInfo.AvailableDurationSeconds is not null
                     && Math.Abs(actual.Value - _parsedInfo.AvailableDurationSeconds.Value) < 30)
            {
                AppendLog(
                    "\u63d0\u793a\uff1a\u6b64\u6b21\u4e0b\u8f09\u7684\u662f\u8a66\u770b\u53ef\u7528\u7247\u6bb5\uff08\u975e\u5b8c\u6574\u7248\uff09\u3002");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\u6642\u9577\u6aa2\u67e5\u5931\u6557: {ex.Message}");
        }
    }

    private static async Task<double?> ProbeMediaDurationSecondsAsync(string ffprobePath, string mediaPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(mediaPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var text = stdout.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
    }

    private sealed record CollapsibleSection(
        Expander Expander,
        TextBlock TitleText,
        string Title,
        Func<Control> BuildContent,
        bool RebuildOnExpand);
    private sealed record PreviewStreamInfo(string Url, string Referer);
    private sealed record CookieSelection(string Label, bool IsAutomaticBrowser);
    private sealed record YtDlpAttemptResult(int ExitCode, bool UsedAutomaticBrowserCookies);
    private sealed record ParsedVideoInfo(
        string Title,
        double? DurationSeconds,
        long? ViewCount,
        long? ChannelFollowerCount,
        string? UploadDate,
        string Url,
        string? ThumbnailUrl = null,
        string? WebpageUrl = null,
        string? VideoId = null,
        string? ExtractorKey = null,
        string? ChannelName = null,
        bool IsChannel = false,
        double? ExpectedDurationSeconds = null,
        double? AvailableDurationSeconds = null,
        bool IsPreviewOnly = false,
        string? AccessWarning = null,
        VideoAccess.Kind Access = VideoAccess.Kind.Public);

    private sealed record SearchVideoResult(
        string Platform,
        string Title,
        string Url,
        string? Uploader,
        double? DurationSeconds,
        long? ViewCount,
        string? ThumbnailUrl,
        string? VideoId,
        string? Description = null);

    private sealed record RecentSearchEntry(
        string Query,
        string Platform,
        DateTime SearchedAtUtc);

    private enum DownloadState
    {
        Queued,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    private sealed class DownloadItemView
    {
        private readonly TextBlock _titleText;
        private readonly TextBlock _metaText;
        private readonly TextBlock _progressText;
        private readonly TextBlock _stateBadge;
        private readonly Button _openBtn;
        private readonly ProgressBar _bar;
        private readonly Border _root;
        private string? _outputPath;

        public string Title { get; }
        public string Url { get; }
        public string Format { get; }
        public string Quality { get; }
        public DownloadState State { get; private set; } = DownloadState.Queued;
        public double Progress { get; private set; }
        public string? OutputPath
        {
            get => _outputPath;
            set
            {
                _outputPath = value;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _openBtn.IsVisible = State == DownloadState.Completed
                            && !string.IsNullOrWhiteSpace(_outputPath)
                            && File.Exists(_outputPath);
                    }
                    catch
                    {
                        // ignore
                    }
                }, DispatcherPriority.Background);
            }
        }
        public Border Root => _root;
        public Action? OnRemove { get; set; }
        public Action? OnCancel { get; set; }
        public Action? OnOpen { get; set; }
        public Action? OnStateChanged { get; set; }

        public DownloadItemView(string title, string url, string format, string quality)
        {
            Title = title;
            Url = url;
            Format = format;
            Quality = quality;

            _titleText = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _metaText = new TextBlock
            {
                Text = format == "MP4" ? $"{format}  {quality}" : $"{format}  high quality",
                FontSize = 11,
                Foreground = TextMuted
            };
            _progressText = new TextBlock
            {
                Text = "\u7b49\u5f85\u4e2d",
                FontSize = 11,
                Foreground = TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            _stateBadge = new TextBlock
            {
                Text = "\u6392\u968a",
                FontSize = 11,
                Foreground = Blue,
                VerticalAlignment = VerticalAlignment.Center
            };
            _bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 6,
                MinWidth = 120
            };

            _openBtn = new Button
            {
                Content = "\u958b\u555f",
                MinWidth = 44,
                Height = 28,
                FontSize = 11,
                Foreground = Blue,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(6, 0),
                IsVisible = false
            };
            _openBtn.Click += (_, _) => OnOpen?.Invoke();

            var removeBtn = new Button
            {
                Content = "Del",
                Width = 40,
                Height = 28,
                FontSize = 11,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0)
            };
            removeBtn.Click += (_, _) => OnRemove?.Invoke();

            var cancelBtn = new Button
            {
                Content = "X",
                Width = 28,
                Height = 28,
                FontSize = 11,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0)
            };
            cancelBtn.Click += (_, _) => OnCancel?.Invoke();

            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var icon = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(8),
                Background = format == "MP4" ? BlueSoft : GreenSoft,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = format,
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = format == "MP4" ? Blue : Green,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            top.Children.Add(icon);

            var mid = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(_titleText);
            mid.Children.Add(_metaText);
            Grid.SetColumn(mid, 1);
            top.Children.Add(mid);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(_stateBadge);
            actions.Children.Add(_openBtn);
            actions.Children.Add(cancelBtn);
            actions.Children.Add(removeBtn);
            Grid.SetColumn(actions, 2);
            top.Children.Add(actions);

            var progressRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 10,
                Margin = new Thickness(52, 6, 0, 0)
            };
            progressRow.Children.Add(_bar);
            Grid.SetColumn(_progressText, 1);
            progressRow.Children.Add(_progressText);

            var stack = new StackPanel { Spacing = 0 };
            stack.Children.Add(top);
            stack.Children.Add(progressRow);

            _root = new Border
            {
                Background = Brush.Parse("#F8FBFF"),
                BorderBrush = BorderSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Child = stack
            };
        }

        public void SetState(DownloadState state)
        {
            State = state;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    (_stateBadge.Text, _stateBadge.Foreground) = state switch
                    {
                        DownloadState.Running => ("\u4e0b\u8f09\u4e2d", Blue),
                        DownloadState.Completed => ("\u5b8c\u6210", Green),
                        DownloadState.Failed => ("\u5931\u6557", Brush.Parse("#EF4444")),
                        DownloadState.Cancelled => ("\u53d6\u6d88", TextMuted),
                        DownloadState.Paused => ("\u66ab\u505c", Brush.Parse("#F59E0B")),
                        _ => ("\u6392\u968a", Blue)
                    };
                    _openBtn.IsVisible = state == DownloadState.Completed
                        && !string.IsNullOrWhiteSpace(_outputPath)
                        && File.Exists(_outputPath);
                }
                catch
                {
                    // ignore UI update races after window close
                }
            }, DispatcherPriority.Background);
            OnStateChanged?.Invoke();
        }

        public void ResetForRetry()
        {
            OutputPath = null;
            SetProgress(0, "\u7b49\u5f85\u4e2d");
            SetState(DownloadState.Queued);
        }

        public void SetProgress(double percent, string label)
        {
            Progress = Math.Clamp(percent, 0, 100);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (Math.Abs(_bar.Value - Progress) >= 0.2 || _progressText.Text != label)
                    {
                        _bar.Value = Progress;
                        _progressText.Text = label;
                    }
                }
                catch
                {
                    // ignore UI update races after window close
                }
            }, DispatcherPriority.Background);
        }
    }
}
