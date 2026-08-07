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
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

public sealed class MainWindow : Window
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
    private static readonly IBrush BgSidebar = Brush.Parse("#F5F9FF");
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

    private readonly List<NavItem> _navItems = [];
    private readonly List<DownloadItemView> _downloadItems = [];
    private readonly StackPanel _downloadListPanel;
    private readonly StackPanel _mainHost;
    private readonly TextBox _urlBox;
    private readonly TextBox _outputBox;
    private readonly ComboBox _qualityCombo;
    private readonly Button _parseButton;
    private readonly Button _convertButton;
    private readonly Button _pasteButton;
    private readonly Button _browseButton;
    private readonly Button _clearQueueButton;
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

    private string _outputFormat = "MP4";
    private string _mp4Quality = "1080P";
    private bool _includeSubtitles = false;
    private bool _downloadPlaylist = false;
    private string _activeNav = "home";
    private int _todayDownloads;
    private string _lastSpeed = "-";
    private ParsedVideoInfo? _parsedInfo;
    private CancellationTokenSource? _conversionTokenSource;
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

        Title = "\u5f71\u97f3\u8f49\u63db\u5927\u5e2b v1.0";
        Width = 1180;
        Height = 780;
        MinWidth = 980;
        MinHeight = 680;
        Background = BgApp;
        FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI, Inter, sans-serif");

        _urlBox = CreateInputBox("https://www.youtube.com/watch?v=... \u6216 Bilibili \u5f71\u7247\u7db2\u5740");
        _outputBox = CreateInputBox("\u9078\u64c7\u8f38\u51fa\u8cc7\u6599\u593e");
        _outputBox.Text = settings.LastOutputFolder;
        _outputBox.LostFocus += (_, _) => SaveSettingsIfPossible();

        _qualityCombo = new ComboBox
        {
            ItemsSource = Mp4QualityOptions,
            SelectedItem = _mp4Quality,
            MinWidth = 120,
            MinHeight = 34,
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
            MinHeight = 48,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Background = Green,
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
            Margin = new Thickness(0, 2, 0, 0)
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
            Margin = new Thickness(0, 2, 0, 0)
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
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };

        _cookiesBrowseButton = CreateSoftButton("\u532f\u5165", 72);
        _cookiesBrowseButton.Click += ChooseCookiesFileAsync;

        _cookiesClearButton = CreateSoftButton("\u6e05\u9664", 72);
        _cookiesClearButton.Click += (_, _) =>
        {
            _cookiesFilePath = null;
            _cookiesPathLabel.Text = "\u672a\u8a2d\u5b9a";
            _cookiesPathLabel.Foreground = TextMuted;
            SaveSettingsIfPossible();
            SetStatus("\u5df2\u6e05\u9664 Cookies \u6a94\u6848");
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
            Height = 240,
            MinHeight = 200,
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
            FontFamily = new FontFamily("Consolas, Microsoft JhengHei UI, monospace"),
            FontSize = 11,
            Foreground = Brush.Parse("#D1D5DB"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Brush.Parse("#D1D5DB"),
            SelectionBrush = Brush.Parse("#3B82F6"),
            MinHeight = 100
        };
        _downloadListPanel = new StackPanel { Spacing = 10 };
        _mainHost = new StackPanel { Spacing = 14 };

        Content = BuildShell();
        SetOutputFormat(_outputFormat);
        Opened += (_, _) => CheckTools();
        Closing += (_, _) => StopEmbeddedPreview(clearStatus: false);
    }

    private Control BuildShell()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("200,*")
        };

        var sidebar = BuildSidebar();
        Grid.SetRowSpan(sidebar, 2);
        root.Children.Add(sidebar);

        var main = new Border
        {
            Background = BgApp,
            Padding = new Thickness(22, 18, 22, 12),
            Child = new ScrollViewer
            {
                Content = _mainHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        var footer = BuildFooter();
        Grid.SetRow(footer, 1);
        Grid.SetColumn(footer, 1);
        root.Children.Add(footer);

        ShowHomePage();
        return root;
    }

    private Control BuildSidebar()
    {
        var panel = new Border
        {
            Background = BgSidebar,
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(14, 18, 14, 16)
        };

        var stack = new StackPanel { Spacing = 8 };

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(4, 0, 0, 18)
        };
        brand.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = RedYouTube,
            Child = new TextBlock
            {
                Text = ">",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var brandText = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock
        {
            Text = "\u5f71\u97f3\u8f49\u63db\u5927\u5e2b",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = TextPrimary
        });
        brandText.Children.Add(new TextBlock
        {
            Text = "v1.0",
            FontSize = 11,
            Foreground = TextMuted
        });
        brand.Children.Add(brandText);
        stack.Children.Add(brand);

        stack.Children.Add(CreateNav("home", "\u9996\u9801"));
        stack.Children.Add(CreateNav("parse", "\u7db2\u5740\u89e3\u6790"));
        stack.Children.Add(CreateNav("downloading", "\u4e0b\u8f09\u4e2d"));
        stack.Children.Add(CreateNav("done", "\u5df2\u5b8c\u6210"));
        stack.Children.Add(CreateNav("audio", "\u97f3\u6a02\u63d0\u53d6"));
        stack.Children.Add(CreateNav("files", "\u6a94\u6848\u7ba1\u7406"));
        stack.Children.Add(CreateNav("history", "\u6b77\u53f2\u8a18\u9304"));
        stack.Children.Add(CreateNav("fav", "\u6211\u7684\u6700\u611b"));

        stack.Children.Add(new Border { Height = 1, Background = BorderSoft, Margin = new Thickness(4, 12) });

        var mascot = new Border
        {
            Background = Brush.Parse("#FFFFFF"),
            BorderBrush = BorderSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new Border
                    {
                        Width = 64,
                        Height = 64,
                        CornerRadius = new CornerRadius(32),
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                            GradientStops =
                            {
                                new GradientStop(Color.Parse("#FFD08A"), 0),
                                new GradientStop(Color.Parse("#F8B4C4"), 1)
                            }
                        },
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "YT",
                            FontSize = 18,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    new TextBlock
                    {
                        Text = "YouTube / Bilibili",
                        FontSize = 11,
                        Foreground = TextMuted,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "\u5b89\u5fc3\u8f49\u63db \u00b7 \u672c\u6a5f\u8655\u7406",
                        FontSize = 11,
                        Foreground = TextSecondary,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        stack.Children.Add(mascot);

        var dock = new DockPanel();
        var bottomHint = new TextBlock
        {
            Text = "\u5b89\u5168\u7121\u6bd2 \u00b7 \u672c\u6a5f\u8f49\u6a94",
            FontSize = 10,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(bottomHint, Dock.Bottom);
        dock.Children.Add(bottomHint);
        dock.Children.Add(stack);
        panel.Child = dock;
        return panel;
    }

    private Control CreateNav(string id, string label)
    {
        var isActive = id == _activeNav;
        var border = new Border
        {
            Background = isActive ? Blue : Brushes.Transparent,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = id
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        content.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = isActive ? Brushes.White : Blue,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = isActive ? Brushes.White : TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        border.Child = content;

        border.PointerEntered += (_, _) =>
        {
            if ((string?)border.Tag != _activeNav)
            {
                border.Background = BlueSoft;
            }
        };
        border.PointerExited += (_, _) =>
        {
            if ((string?)border.Tag != _activeNav)
            {
                border.Background = Brushes.Transparent;
            }
        };
        border.PointerPressed += (_, _) => Navigate(id);

        _navItems.Add(new NavItem(id, border));
        return border;
    }

    private void Navigate(string id)
    {
        _activeNav = id;
        foreach (var item in _navItems)
        {
            var active = item.Id == id;
            item.Border.Background = active ? Blue : Brushes.Transparent;
            if (item.Border.Child is StackPanel sp)
            {
                if (sp.Children.Count >= 1 && sp.Children[0] is Ellipse dot)
                {
                    dot.Fill = active ? Brushes.White : Blue;
                }

                if (sp.Children.Count >= 2 && sp.Children[1] is TextBlock label)
                {
                    label.Foreground = active ? Brushes.White : TextPrimary;
                    label.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
        }

        switch (id)
        {
            case "home":
            case "parse":
                ShowHomePage();
                break;
            case "downloading":
                ShowQueuePage(onlyActive: true);
                break;
            case "done":
                ShowQueuePage(onlyDone: true);
                break;
            case "audio":
                SetOutputFormat("MP3");
                ShowHomePage();
                SetStatus("\u5df2\u5207\u63db\u5230\u97f3\u6a02\u63d0\u53d6\uff08MP3\uff09");
                break;
            case "files":
                ShowFilesPage();
                break;
            case "history":
                ShowQueuePage();
                break;
            case "fav":
                ShowPlaceholder("\u6211\u7684\u6700\u611b", "\u4e4b\u5f8c\u53ef\u6536\u85cf\u5e38\u7528\u5f71\u7247\u8207\u64ad\u653e\u6e05\u55ae\u3002");
                break;
        }
    }

    private void ShowHomePage()
    {
        // Shared controls are reused across rebuilds. Avalonia forbids adding a control
        // that still has a visual parent, so detach them before clearing the host.
        // NativeWebView needs an explicit reparent scope while its host moves.
        using var reparent = _previewWebView.BeginReparenting();
        DetachSharedControls();
        _mainHost.Children.Clear();
        _mainHost.Children.Add(BuildHeader());
        _mainHost.Children.Add(BuildUrlCard());
        _mainHost.Children.Add(BuildOptionsAndPreviewRow());
        _mainHost.Children.Add(BuildQueueAndUtilsRow());
        _mainHost.Children.Add(BuildLogCard());
        RebuildDownloadList();
    }

    private void ShowQueuePage(bool onlyActive = false, bool onlyDone = false)
    {
        // Pause embedded media when leaving the home/preview surface.
        StopEmbeddedPreview(clearStatus: false);
        using var reparent = _previewWebView.BeginReparenting();
        DetachSharedControls();
        _mainHost.Children.Clear();
        var title = onlyActive
            ? "\u4e0b\u8f09\u4e2d"
            : onlyDone
                ? "\u5df2\u5b8c\u6210"
                : "\u6b77\u53f2\u8a18\u9304";
        var subtitle = onlyActive
            ? "\u76ee\u524d\u9032\u884c\u4e2d\u7684\u8f49\u63db\u4efb\u52d9"
            : onlyDone
                ? "\u5df2\u6210\u529f\u5b8c\u6210\u7684\u6a94\u6848"
                : "\u6240\u6709\u8f49\u63db\u7d00\u9304";
        _mainHost.Children.Add(SectionTitle(title, subtitle));

        var filtered = _downloadItems.Where(item =>
        {
            if (onlyActive)
            {
                return item.State is DownloadState.Queued or DownloadState.Running or DownloadState.Paused;
            }

            if (onlyDone)
            {
                return item.State == DownloadState.Completed;
            }

            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            _mainHost.Children.Add(EmptyState("\u76ee\u524d\u6c92\u6709\u9805\u76ee", "\u5f9e\u9996\u9801\u8cbc\u4e0a\u7db2\u5740\u4e26\u958b\u59cb\u8f49\u63db\u3002"));
            return;
        }

        var list = new StackPanel { Spacing = 10 };
        foreach (var item in filtered)
        {
            DetachFromParent(item.Root);
            list.Children.Add(item.Root);
        }

        _mainHost.Children.Add(Card(list));
    }

    private void ShowFilesPage()
    {
        StopEmbeddedPreview(clearStatus: false);
        using var reparent = _previewWebView.BeginReparenting();
        DetachSharedControls();
        _mainHost.Children.Clear();
        _mainHost.Children.Add(SectionTitle("\u6a94\u6848\u7ba1\u7406", "\u958b\u555f\u8f38\u51fa\u8cc7\u6599\u593e\uff0c\u7ba1\u7406\u5df2\u8f49\u63db\u7684\u6a94\u6848"));
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

        _mainHost.Children.Add(Card(panel));
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

    private void ShowPlaceholder(string title, string message)
    {
        StopEmbeddedPreview(clearStatus: false);
        using var reparent = _previewWebView.BeginReparenting();
        DetachSharedControls();
        _mainHost.Children.Clear();
        _mainHost.Children.Add(SectionTitle(title, message));
        _mainHost.Children.Add(EmptyState(title, message));
    }

    /// <summary>
    /// Removes a control from its current visual parent so it can be reparented safely.
    /// </summary>
    private static void DetachFromParent(Control? control)
    {
        if (control?.Parent is null)
        {
            return;
        }

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
                break;
            case ContentControl contentControl:
                if (ReferenceEquals(contentControl.Content, control))
                {
                    contentControl.Content = null;
                }
                break;
        }
    }

    /// <summary>
    /// Shared field-backed controls keep their intermediate parents after
    /// <c>_mainHost.Children.Clear()</c>. Detach them before rebuilding pages.
    /// </summary>
    private void DetachSharedControls()
    {
        DetachFromParent(_urlBox);
        DetachFromParent(_pasteButton);
        DetachFromParent(_parseButton);
        DetachFromParent(_mp4Card);
        DetachFromParent(_mp3Card);
        DetachFromParent(_qualityCombo);
        DetachFromParent(_subtitleCheckBox);
        DetachFromParent(_playlistCheckBox);
        DetachFromParent(_outputBox);
        DetachFromParent(_browseButton);
        DetachFromParent(_convertButton);
        DetachFromParent(_statusText);
        DetachFromParent(_previewCard);
        DetachFromParent(_queueCountText);
        DetachFromParent(_clearQueueButton);
        DetachFromParent(_downloadListPanel);
        DetachFromParent(_logText);

        foreach (var item in _downloadItems)
        {
            DetachFromParent(item.Root);
        }
    }

    private Control BuildHeader()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var left = new StackPanel { Spacing = 4 };
        var titlePanel = new WrapPanel { Orientation = Orientation.Horizontal };
        titlePanel.Children.Add(ColoredWord("YouTube", RedYouTube, 26, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord(" / ", TextPrimary, 26, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord("Bilibili", PinkBili, 26, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord(" \u5f71\u7247\u8f49 ", TextPrimary, 26, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord("MP4", Blue, 26, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord(" / ", TextPrimary, 26, FontWeight.Bold));
        titlePanel.Children.Add(ColoredWord("MP3", Green, 26, FontWeight.Bold));

        left.Children.Add(titlePanel);
        left.Children.Add(new TextBlock
        {
            Text = "\u652f\u63f4\u9ad8\u756b\u8cea\u4e0b\u8f09 \u00b7 \u5feb\u901f\u8f49\u63db \u00b7 \u6279\u91cf\u8655\u7406",
            FontSize = 13,
            Foreground = TextSecondary,
            Margin = new Thickness(0, 2, 0, 0)
        });
        row.Children.Add(left);

        var settingsBtn = CreateSoftButton("\u8a2d\u5b9a", 96);
        settingsBtn.Click += (_, _) =>
        {
            Navigate("files");
            SetStatus("\u53ef\u5728\u6b64\u7ba1\u7406\u8f38\u51fa\u8cc7\u6599\u593e");
        };
        Grid.SetColumn(settingsBtn, 1);
        settingsBtn.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(settingsBtn);
        return row;
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
        var body = new StackPanel { Spacing = 12 };

        body.Children.Add(new TextBlock
        {
            Text = "\u8cbc\u4e0a YouTube \u6216 Bilibili \u5f71\u7247\u7db2\u5740",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        });

        var urlRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        urlRow.Children.Add(_urlBox);
        Grid.SetColumn(_pasteButton, 1);
        urlRow.Children.Add(_pasteButton);
        body.Children.Add(urlRow);

        var actionRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 10
        };

        var ytChip = PlatformChip("YouTube", RedYouTube, Brush.Parse("#FFECEC"));
        var biliChip = PlatformChip("bilibili", PinkBili, PinkBiliSoft);
        actionRow.Children.Add(ytChip);
        Grid.SetColumn(biliChip, 1);
        actionRow.Children.Add(biliChip);
        Grid.SetColumn(_parseButton, 3);
        actionRow.Children.Add(_parseButton);
        body.Children.Add(actionRow);

        return Card(body);
    }

    private Control BuildOptionsAndPreviewRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,1.05*"),
            ColumnSpacing = 14
        };

        var left = new StackPanel { Spacing = 12 };

        left.Children.Add(new TextBlock
        {
            Text = "\u9078\u64c7\u8f49\u63db\u683c\u5f0f",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        });

        var formatRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10
        };
        formatRow.Children.Add(_mp4Card);
        Grid.SetColumn(_mp3Card, 1);
        formatRow.Children.Add(_mp3Card);
        left.Children.Add(formatRow);

        var qualityPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        qualityPanel.Children.Add(new TextBlock
        {
            Text = "\u756b\u8cea\u9078\u64c7 (MP4)",
            FontSize = 13,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        });
        qualityPanel.Children.Add(new TextBlock
        {
            Text = "1080P (\u63a8\u85a6)",
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });
        qualityPanel.Children.Add(_qualityCombo);
        left.Children.Add(qualityPanel);
        left.Children.Add(_subtitleCheckBox);
        left.Children.Add(_playlistCheckBox);

        // Cookies file import row
        left.Children.Add(new TextBlock
        {
            Text = "Cookies \u6a94\u6848\uff08\u6703\u54e1\u9650\u5b9a\u5f71\u7247\uff09",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var cookiesRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        cookiesRow.Children.Add(_cookiesPathLabel);
        cookiesRow.Children.Add(_cookiesBrowseButton);
        cookiesRow.Children.Add(_cookiesClearButton);
        left.Children.Add(cookiesRow);

        left.Children.Add(new TextBlock
        {
            Text = "\u5132\u5b58\u4f4d\u7f6e",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var pathRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        pathRow.Children.Add(_outputBox);
        Grid.SetColumn(_browseButton, 1);
        pathRow.Children.Add(_browseButton);
        left.Children.Add(pathRow);

        left.Children.Add(_convertButton);
        left.Children.Add(_statusText);

        var leftCard = Card(left);
        row.Children.Add(leftCard);
        Grid.SetColumn(_previewCard, 1);
        row.Children.Add(_previewCard);
        return row;
    }

    private Border BuildPreviewCard()
    {
        var body = new StackPanel { Spacing = 10 };

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
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };
        actionRow.Children.Add(_previewPlayButton);
        Grid.SetColumn(_previewStopButton, 1);
        actionRow.Children.Add(_previewStopButton);
        Grid.SetColumn(_previewBrowserButton, 2);
        actionRow.Children.Add(_previewBrowserButton);
        body.Children.Add(actionRow);

        body.Children.Add(_previewTitle);

        var meta = new StackPanel { Spacing = 4 };
        meta.Children.Add(_previewDuration);
        meta.Children.Add(_previewViews);
        meta.Children.Add(_previewChannelFollowers);
        meta.Children.Add(_previewDate);
        body.Children.Add(meta);

        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 0)
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
            ColumnDefinitions = new ColumnDefinitions("*,220"),
            ColumnSpacing = 14
        };

        var queueBody = new StackPanel { Spacing = 10 };
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
        queueBody.Children.Add(_downloadListPanel);

        var queueCard = Card(queueBody);
        row.Children.Add(queueCard);

        var utils = new StackPanel { Spacing = 10 };
        utils.Children.Add(new TextBlock
        {
            Text = "\u5be6\u7528\u529f\u80fd",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimary
        });

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 10,
            ColumnSpacing = 10
        };
        grid.Children.Add(UtilButton("\u6279\u91cf", "\u6279\u91cf\u4e0b\u8f09", () =>
        {
            SetStatus("\u53ef\u5728\u7db2\u5740\u6b04\u8cbc\u591a\u884c\u7db2\u5740\uff08\u6bcf\u884c\u4e00\u500b\uff09\u5f8c\u958b\u59cb\u8f49\u63db");
            _urlBox.Focus();
        }));
        var sub = UtilButton("CC", "\u5b57\u5e55\u642d\u914d", () =>
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
        var audio = UtilButton("MP3", "\u97f3\u6a02\u63d0\u53d6", () =>
        {
            SetOutputFormat("MP3");
            SetStatus("\u5df2\u5207\u63db MP3 \u97f3\u6a02\u63d0\u53d6");
        });
        Grid.SetRow(audio, 1);
        grid.Children.Add(audio);
        var fmt = UtilButton("FMT", "\u683c\u5f0f\u8f49\u63db", () =>
        {
            SetOutputFormat(_outputFormat == "MP4" ? "MP3" : "MP4");
            SetStatus($"\u5df2\u5207\u63db\u70ba {_outputFormat}");
        });
        Grid.SetRow(fmt, 1);
        Grid.SetColumn(fmt, 1);
        grid.Children.Add(fmt);
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
            MinHeight = 120,
            MaxHeight = 200,
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
            Text = "\u7248\u672c\uff1a1.0.0  |  \u652f\u63f4 Windows 10/11",
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
            Padding = new Thickness(14, 16),
            Cursor = new Cursor(StandardCursorType.Hand),
            MinHeight = 72
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = softBg,
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = label == "MP4" ? "VID" : "AUD",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        });
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

    private Border UtilButton(string icon, string label, Action onClick)
    {
        var border = new Border
        {
            Background = BlueSoft,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 14),
            Cursor = new Cursor(StandardCursorType.Hand),
            MinHeight = 72
        };
        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Blue,
            HorizontalAlignment = HorizontalAlignment.Center
        });
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

    private static Border PlatformChip(string text, IBrush fg, IBrush bg) => new()
    {
        Background = bg,
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(12, 6),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
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
        Padding = new Thickness(16),
        Child = child
    };

    private static Control SectionTitle(string title, string subtitle)
    {
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = TextPrimary
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 13,
            Foreground = TextSecondary
        });
        return stack;
    }

    private static Control EmptyState(string title, string message) => Card(new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        }
    });

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
        MinHeight = 40,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.White,
        Background = Blue,
        CornerRadius = new CornerRadius(10),
        Cursor = new Cursor(StandardCursorType.Hand),
        Padding = new Thickness(14, 8)
    };

    private static Button CreateSoftButton(string text, double minWidth) => new()
    {
        Content = text,
        MinWidth = minWidth,
        MinHeight = 40,
        FontSize = 13,
        Foreground = TextPrimary,
        Background = BlueSoft,
        BorderBrush = BorderSoft,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Cursor = new Cursor(StandardCursorType.Hand),
        Padding = new Thickness(12, 8)
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
        }
    }

    private async void ParseUrlAsync(object? sender, RoutedEventArgs e) => await ParseUrlCoreAsync();

    private async Task ParseUrlCoreAsync()
    {
        var urls = GetInputUrls();
        if (urls.Length == 0)
        {
            SetStatus("\u8acb\u5148\u8cbc\u4e0a YouTube \u6216 Bilibili \u7db2\u5740");
            return;
        }

        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        if (ytDlpPath is null)
        {
            SetStatus("\u627e\u4e0d\u5230 yt-dlp\uff0c\u8acb\u5148\u5b89\u88dd");
            AppendInstallHint();
            return;
        }

        _parseButton.IsEnabled = false;
        _previewPlayButton.IsEnabled = false;
        _previewStopButton.IsEnabled = false;
        _previewBrowserButton.IsEnabled = false;
        StopEmbeddedPreview(clearStatus: false);
        SetStatus("\u6b63\u5728\u89e3\u6790\u7db2\u5740...");
        AppendLog($"\u89e3\u6790: {urls[0]}");

        try
        {
            var info = IsYouTubeChannelUrl(urls[0])
                ? await DumpChannelInfoAsync(ytDlpPath, urls[0])
                : await DumpVideoInfoAsync(ytDlpPath, urls[0]);
            if (info is null)
            {
                SetStatus("\u89e3\u6790\u5931\u6557\uff0c\u8acb\u6aa2\u67e5\u7db2\u5740\u6216\u7a0d\u5f8c\u518d\u8a66");
                _parsedInfo = null;
                _previewTitle.Text = "\u89e3\u6790\u5931\u6557";
                _previewStatus.Text = "\u89e3\u6790\u5931\u6557";
                _previewStatus.Foreground = Brush.Parse("#EF4444");
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
            if (!info.IsChannel)
            {
                // Auto-load a local HTML5 stream preview (avoids YouTube embed 152/153 blocks).
                _ = StartEmbeddedPreviewAsync(autoplay: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus("\u89e3\u6790\u6642\u767c\u751f\u932f\u8aa4");
            AppendLog(ex.Message);
        }
        finally
        {
            _parseButton.IsEnabled = true;
        }
    }

    private async Task<ParsedVideoInfo?> DumpVideoInfoAsync(string ytDlpPath, string url)
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
        AddBilibiliBrowserHeaders(startInfo, url);
        var cookieBrowser = AddBilibiliBrowserCookies(startInfo, url);
        if (cookieBrowser is not null)
        {
            AppendLog($"Bilibili cookies: {cookieBrowser}");
        }

        AddCookiesFileArgument(startInfo, url);

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
            var (isPreviewOnly, accessWarning) = DetectLimitedAccess(
                title,
                description,
                availability,
                duration,
                availableDuration,
                IsBilibiliVideoUrl(url));

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
                AccessWarning: accessWarning);
        }
        catch (Exception ex)
        {
            AppendLog($"JSON \u89e3\u6790\u5931\u6557: {ex.Message}");
            return null;
        }
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

    private static (bool IsPreviewOnly, string? AccessWarning) DetectLimitedAccess(
        string title,
        string? description,
        string? availability,
        double? metadataDuration,
        double? availableDuration,
        bool isBilibili)
    {
        var text = $"{title}\n{description ?? ""}";
        var looksChargeExclusive =
            text.Contains("\u5145\u7535\u4e13\u5c5e", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\u5145\u96fb\u5c08\u5c6c", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\u8a66\u770b", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\u8bd5\u770b", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\u4f1a\u5458\u4e13\u5c5e", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\u6703\u54e1\u5c08\u5c6c", StringComparison.OrdinalIgnoreCase)
            || text.Contains("members-only", StringComparison.OrdinalIgnoreCase)
            || text.Contains("member only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(availability, "subscriber_only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(availability, "premium_only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(availability, "needs_auth", StringComparison.OrdinalIgnoreCase);

        // Stream shorter than claimed length => free preview / locked full video.
        var durationMismatch = metadataDuration is > 30
            && availableDuration is > 0
            && availableDuration + 15 < metadataDuration * 0.85;

        if (!durationMismatch && !looksChargeExclusive)
        {
            return (false, null);
        }

        if (isBilibili || looksChargeExclusive || durationMismatch)
        {
            var claimed = FormatDuration(metadataDuration);
            var available = FormatDuration(availableDuration ?? metadataDuration);
            var warning = durationMismatch
                ? $"\u6b64\u5f71\u7247\u53ef\u80fd\u70ba\u300c\u5145\u96fb\u5c08\u5c6c\u300d\u6216\u6703\u54e1\u9650\u5236\uff1a\u5b8c\u6574\u7d04 {claimed}\uff0c\u76ee\u524d\u53ea\u80fd\u4e0b\u8f09\u8a66\u770b\u7d04 {available}\u3002\u8acb\u5148\u5c0d\u8a72 UP \u5305\u6708\u5145\u96fb\uff0c\u4e26\u7528\u5df2\u767b\u5165 B \u7ad9\u7684\u700f\u89bd\u5668 cookies \u518d\u4e0b\u8f09\u3002"
                : "\u6b64\u5f71\u7247\u53ef\u80fd\u70ba\u300c\u5145\u96fb\u5c08\u5c6c\u300d/\u6703\u54e1\u9650\u5236\u5167\u5bb9\u3002\u672a\u89e3\u9396\u6642\u53ea\u80fd\u4e0b\u8f09\u8a66\u770b\u7247\u6bb5\uff1b\u8acb\u5148\u5c0d UP \u5305\u6708\u5145\u96fb\u4e26\u4f7f\u7528\u5df2\u767b\u5165\u7684\u700f\u89bd\u5668 cookies\u3002";
            return (durationMismatch || looksChargeExclusive, warning);
        }

        return (false, null);
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
            _embeddedPreviewActive = false;
            _previewWebView.IsVisible = false;
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
            _previewWebView.IsVisible = true;
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
            _previewWebView.IsVisible = false;
            if (autoplay && _parsedInfo is not null)
            {
                OpenOriginalPage(_parsedInfo.WebpageUrl ?? _parsedInfo.Url);
            }
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
        _ = AddBilibiliBrowserCookies(startInfo, pageUrl);
        AddCookiesFileArgument(startInfo, pageUrl);
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

        _embeddedPreviewActive = false;
        _previewWebView.IsVisible = false;
        _previewOverlay.IsVisible = true;
        _previewStopButton.IsEnabled = false;

        if (_parsedInfo is not null)
        {
            _previewPlayButton.IsEnabled = true;
            _previewStatus.Text = "\u89e3\u6790\u6210\u529f \u00b7 \u53ef\u5167\u5d4c\u64ad\u653e";
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            if (_parsedInfo is not null && IsBilibiliVideoUrl(_parsedInfo.Url))
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            }

            var bytes = await http.GetByteArrayAsync(thumbnailUrl);
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
        // Optional full playlist download; default remains single-video only.
        startInfo.ArgumentList.Add(_downloadPlaylist ? "--yes-playlist" : "--no-playlist");

        // Subtitles are handled in a separate pass so HTTP 429 on captions cannot abort media.
        if (includeSubtitles)
        {
            AddSubtitleArguments(startInfo, forEmbedDuringDownload: false);
        }

        AddBilibiliBrowserHeaders(startInfo, url);
        var cookieBrowser = AddBilibiliBrowserCookies(startInfo, url);
        if (cookieBrowser is not null)
        {
            AppendLog($"Bilibili cookies: {cookieBrowser}");
        }

        AddCookiesFileArgument(startInfo, url);

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

        return await RunProcessAsync(startInfo, token, item);
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
            AddBilibiliBrowserCookies(startInfo, url);
            AddCookiesFileArgument(startInfo, url);
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
            startInfo.ArgumentList.Add(GetMp4FormatSelector(mp4Quality));
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

    private static string GetMp4FormatSelector(string mp4Quality)
    {
        var maxHeight = mp4Quality.ToUpperInvariant() switch
        {
            "4K" => 2160,
            "720P" => 720,
            "480P" => 480,
            _ => 1080
        };
        return $"bestvideo*[height<={maxHeight}]+bestaudio/best[height<={maxHeight}]/best";
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

    private static string? AddBilibiliBrowserCookies(ProcessStartInfo startInfo, string url)
    {
        if (!IsBilibiliVideoUrl(url))
        {
            return null;
        }

        var browser = FindBrowserForCookies();
        if (browser is null)
        {
            return null;
        }

        startInfo.ArgumentList.Add("--cookies-from-browser");
        startInfo.ArgumentList.Add(browser);
        return browser;
    }

    private void AddCookiesFileArgument(ProcessStartInfo startInfo, string url)
    {
        if (string.IsNullOrEmpty(_cookiesFilePath) || !File.Exists(_cookiesFilePath))
        {
            return;
        }

        // If Bilibili browser cookies were already added, skip the file-based cookies.
        if (IsBilibiliVideoUrl(url))
        {
            return;
        }

        startInfo.ArgumentList.Add("--cookies");
        startInfo.ArgumentList.Add(_cookiesFilePath);
    }

    private static string? FindBrowserForCookies()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (Directory.Exists("/Applications/Firefox.app"))
            {
                return "firefox";
            }

            if (Directory.Exists("/Applications/Google Chrome.app"))
            {
                return "chrome";
            }

            if (Directory.Exists("/Applications/Safari.app"))
            {
                return "safari";
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // Prefer Chromium browsers first: most Bilibili sessions (incl. 包月充电) live there.
            if (Directory.Exists(IoPath.Combine(localAppData, "Google", "Chrome"))
                || Directory.Exists(IoPath.Combine(programFiles, "Google", "Chrome"))
                || Directory.Exists(IoPath.Combine(programFilesX86, "Google", "Chrome")))
            {
                return "chrome";
            }

            if (Directory.Exists(IoPath.Combine(localAppData, "Microsoft", "Edge"))
                || Directory.Exists(IoPath.Combine(programFiles, "Microsoft", "Edge"))
                || Directory.Exists(IoPath.Combine(programFilesX86, "Microsoft", "Edge")))
            {
                return "edge";
            }

            if (Directory.Exists(IoPath.Combine(localAppData, "Mozilla", "Firefox"))
                || Directory.Exists(IoPath.Combine(programFiles, "Mozilla Firefox"))
                || Directory.Exists(IoPath.Combine(programFilesX86, "Mozilla Firefox")))
            {
                return "firefox";
            }
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

        AppendLog(line);
        TryUpdateProgress(line, item);

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

        if (ytDlp is null || ffmpeg is null || ffprobe is null)
        {
            SetStatus("\u9700\u8981 yt-dlp \u548c ffmpeg \u624d\u80fd\u8f49\u63db MP3 / MP4");
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AppendLog("Windows: winget install yt-dlp.yt-dlp Gyan.FFmpeg");
            AppendLog("\u8acb\u4ee5\u7ba1\u7406\u54e1\u8eab\u5206\u57f7\u884c\u7d42\u7aef\u6a5f\u5f8c\u5b89\u88dd\uff0c\u518d\u91cd\u555f\u7a0b\u5f0f\u3002");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            AppendLog("macOS: brew install yt-dlp ffmpeg");
            return;
        }

        AppendLog("Please install yt-dlp and ffmpeg via your package manager.");
    }

    private void SetBusy(bool busy)
    {
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
            _downloadPlaylist);
    }

    private static string NormalizeMp4Quality(string? quality)
    {
        var q = (quality ?? "1080P").ToUpperInvariant();
        return q switch
        {
            "4K" => "4K",
            "480P" or "480" => "480P",
            "720P" or "720" => "720P",
            "1080P" or "1080" => "1080P",
            _ => "1080P"
        };
    }

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
        Dispatcher.UIThread.Post(() => _statusText.Text = text, DispatcherPriority.Normal);

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

    private sealed record NavItem(string Id, Border Border);
    private sealed record PreviewStreamInfo(string Url, string Referer);
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
        string? AccessWarning = null);

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
                    return new AppSettings
                    {
                        LastOutputFolder = Directory.Exists(settings.LastOutputFolder)
                            ? settings.LastOutputFolder
                            : GetDefaultOutputFolder(),
                        UrlInputCount = settings.UrlInputCount is 1 or 3 or 7 ? settings.UrlInputCount : 1,
                        OutputFormat = string.Equals(settings.OutputFormat, "MP3", StringComparison.OrdinalIgnoreCase) ? "MP3" : "MP4",
                        Mp4Quality = NormalizeQuality(settings.Mp4Quality),
                        // Missing property in older settings.json => default off.
                        IncludeSubtitles = settings.IncludeSubtitles ?? false,
                        DownloadPlaylist = settings.DownloadPlaylist ?? false,
                        TodayDownloadCount = settings.TodayDate == today ? settings.TodayDownloadCount : 0,
                        TodayDate = today,
                        CookieFilePath = !string.IsNullOrEmpty(settings.CookieFilePath) && File.Exists(settings.CookieFilePath)
                            ? settings.CookieFilePath
                            : null
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
        bool downloadPlaylist = false)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var settings = new AppSettings
            {
                LastOutputFolder = outputFolder,
                UrlInputCount = urlInputCount,
                OutputFormat = string.Equals(outputFormat, "MP3", StringComparison.OrdinalIgnoreCase) ? "MP3" : "MP4",
                Mp4Quality = NormalizeQuality(mp4Quality),
                IncludeSubtitles = includeSubtitles,
                DownloadPlaylist = downloadPlaylist,
                TodayDownloadCount = todayDownloadCount,
                TodayDate = todayDate ?? DateOnly.FromDateTime(DateTime.Now),
                CookieFilePath = cookieFilePath
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
        var videos = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Videos",
            "Converted");
        try
        {
            Directory.CreateDirectory(videos);
            return videos;
        }
        catch
        {
            var downloads = IoPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            return Directory.Exists(downloads)
                ? downloads
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private static string NormalizeQuality(string? quality)
    {
        var q = (quality ?? "1080P").ToUpperInvariant();
        return q switch
        {
            "4K" => "4K",
            "480P" or "480" => "480P",
            "720P" or "720" => "720P",
            "1080P" or "1080" => "1080P",
            _ => "1080P"
        };
    }
}

internal static class ToolLocator
{
    private static readonly string[] UnixSearchPaths =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/usr/bin",
        "/bin"
    ];

    public static string? FindExecutable(string name)
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
