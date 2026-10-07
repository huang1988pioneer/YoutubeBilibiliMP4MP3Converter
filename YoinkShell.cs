using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using IoPath = System.IO.Path;

namespace YoutubeOrBilibiliMP3Converter;

public sealed partial class MainWindow
{
    private enum ShellPhase
    {
        Input,
        Probing,
        Picking,
        Downloading,
        Done,
        Error,
        History,
        Settings
    }

    private enum ShellTheme
    {
        Dark,
        Light,
        Auto
    }

    private sealed class ShellPalette
    {
        public required IBrush Background { get; init; }
        public required IBrush Surface { get; init; }
        public required IBrush Primary { get; init; }
        public required IBrush Gray { get; init; }
        public required IBrush Line { get; init; }
        public required IBrush ButtonFill { get; init; }
        public required IBrush ButtonText { get; init; }
        public required IBrush Track { get; init; }
        public required IBrush Warn { get; init; }
        public required IBrush Accent { get; init; }
        public required IBrush Soft { get; init; }
        public required IBrush Selection { get; init; }
    }

    private sealed class FormatCard
    {
        public required Border Root { get; init; }
        public required Border Badge { get; init; }
        public required TextBlock BadgeText { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Detail { get; init; }
    }

    private const double StageWidth = 640;

    private static readonly string[] SpinFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private ShellPhase _shellPhase = ShellPhase.Input;
    private ShellTheme _shellTheme = ShellTheme.Dark;
    private Border? _shellCenter;
    private ScrollViewer? _shellScroll;
    private Border? _shellPreviewFrame;
    private bool _fittingWindow;
    private TextBox? _shellUrlBox;
    private TextBox? _shellSearchBox;
    private TextBlock? _shellHintText;
    private TextBlock? _shellStatusLine;
    private TextBlock? _shellSpinner;
    private ProgressBar? _shellProgressBar;
    private TextBlock? _shellProgressMeta;
    private readonly List<FormatCard> _formatCards = [];
    private List<FormatChoiceBuilder.Choice> _formatChoices = [];
    private readonly List<string> _shellUrlHistory = [];
    private int _formatIndex;
    private int _shellRecallIndex = -1;
    private int _shellSpinFrame;
    private int _probeGeneration;
    private bool _shellRendering;
    private bool _shellRecallSilent;
    private bool _shellSawDownload;
    private bool _shellCancelRequested;
    private bool _shellProcessing;
    private bool _shellSearching;
    private double _shellPercent;
    private string _shellDraft = "";
    private string _shellSearchDraft = "";
    private string? _shellWarning;
    private string? _shellError;
    private string? _shellDonePath;
    private string? _shellClipboardUrl;
    private string _shellSpeed = "";
    private string _shellDownloadLabel = "";
    private DispatcherTimer? _shellSpinTimer;
    private Bitmap? _shellPreviewBitmap;
    private int _shellPreviewVersion;
    private string? _shellPreviewUrl;

    private Control CreateShellRoot()
    {
        _shellCenter = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = Height > 0 ? Height : 700
        };
        _shellScroll = new ScrollViewer
        {
            Content = _shellCenter,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent
        };
        var scroll = _shellScroll;

        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += (_, e) =>
        {
            if (_shellCenter is not null)
            {
                _shellCenter.MinHeight = Math.Max(0, e.NewSize.Height);
            }
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            if (_shellTheme == ShellTheme.Auto && !_shellRendering)
            {
                RenderShell();
            }
        };
        Activated += async (_, _) => await RefreshClipboardHintAsync();
        _shellSpinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _shellSpinTimer.Tick += (_, _) =>
        {
            if (_shellSpinner is null)
            {
                return;
            }

            _shellSpinFrame = (_shellSpinFrame + 1) % SpinFrames.Length;
            _shellSpinner.Text = SpinFrames[_shellSpinFrame];
        };
        _shellSpinTimer.Start();
        Closing += (_, _) => _shellSpinTimer.Stop();

        RenderShell();
        return scroll;
    }

    private void RenderShell()
    {
        if (_shellCenter is null || _shellRendering)
        {
            return;
        }

        _shellRendering = true;
        try
        {
            if (_shellUrlBox is not null)
            {
                _shellDraft = _shellUrlBox.Text ?? "";
            }

            if (_shellSearchBox is not null)
            {
                _shellSearchDraft = _shellSearchBox.Text ?? "";
            }

            _shellUrlBox = null;
            _shellSearchBox = null;
            _shellHintText = null;
            _shellStatusLine = null;
            _shellSpinner = null;
            _shellProgressBar = null;
            _shellProgressMeta = null;
            _formatCards.Clear();
            _shellPreviewFrame = null;

            ApplyShellTheme();
            var palette = CurrentPalette();
            var root = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Spacing = 0
            };
            root.Children.Add(BuildAppBar(palette));

            var page = new StackPanel
            {
                Width = StageWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 0
            };

            switch (_shellPhase)
            {
                case ShellPhase.Input:
                    BuildInput(page, palette);
                    break;
                case ShellPhase.Probing:
                    BuildProbing(page, palette);
                    break;
                case ShellPhase.Picking:
                    BuildPicking(page, palette);
                    break;
                case ShellPhase.Downloading:
                    BuildDownloading(page, palette);
                    break;
                case ShellPhase.Done:
                    BuildDone(page, palette);
                    break;
                case ShellPhase.Error:
                    BuildError(page, palette);
                    break;
                case ShellPhase.History:
                    BuildHistory(page, palette);
                    break;
                case ShellPhase.Settings:
                    BuildSettings(page, palette);
                    break;
            }

            if (_shellPhase is ShellPhase.Picking or ShellPhase.History or ShellPhase.Settings)
            {
                page.VerticalAlignment = VerticalAlignment.Top;
                page.Margin = new Thickness(0, 8, 0, 0);
            }

            var stage = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 0,
                Padding = new Thickness(32, 8, 32, 20),
                Child = page
            };
            root.Children.Add(stage);
            _shellCenter!.Child = root;
            FitWindowToContent(root, stage);

            Dispatcher.UIThread.Post(() =>
            {
                if (_shellPhase == ShellPhase.Input)
                {
                    _shellUrlBox?.Focus();
                    _ = RefreshClipboardHintAsync();
                    return;
                }

                Focus();
            }, DispatcherPriority.Background);
        }
        finally
        {
            _shellRendering = false;
        }
    }

    private void ApplyShellTheme()
    {
        RequestedThemeVariant = _shellTheme switch
        {
            ShellTheme.Light => ThemeVariant.Light,
            ShellTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        Background = CurrentPalette().Background;
    }

    private bool ShellIsDark() => _shellTheme switch
    {
        ShellTheme.Dark => true,
        ShellTheme.Light => false,
        _ => ActualThemeVariant == ThemeVariant.Dark
    };

    private ShellPalette CurrentPalette() => ShellIsDark()
        ? new ShellPalette
        {
            Background = Brush.Parse("#101418"),
            Surface = Brush.Parse("#1A2228"),
            Primary = Brush.Parse("#F3F6F4"),
            Gray = Brush.Parse("#9AABA8"),
            Line = Brush.Parse("#2C3A40"),
            ButtonFill = Brush.Parse("#1FA7A0"),
            ButtonText = Brush.Parse("#06201E"),
            Track = Brush.Parse("#243036"),
            Warn = Brush.Parse("#E2B15A"),
            Accent = Brush.Parse("#3DCFC6"),
            Soft = Brush.Parse("#163430"),
            Selection = Brush.Parse("#663DCFC6")
        }
        : new ShellPalette
        {
            Background = Brush.Parse("#F3F6F5"),
            Surface = Brush.Parse("#FFFFFF"),
            Primary = Brush.Parse("#14211F"),
            Gray = Brush.Parse("#5E726F"),
            Line = Brush.Parse("#D5E2DF"),
            ButtonFill = Brush.Parse("#0E8F88"),
            ButtonText = Brush.Parse("#FFFFFF"),
            Track = Brush.Parse("#E5F3F1"),
            Warn = Brush.Parse("#8A5A00"),
            Accent = Brush.Parse("#0E8F88"),
            Soft = Brush.Parse("#E7F7F5"),
            Selection = Brush.Parse("#6614B8A6")
        };

    private ShellTheme ParseShellTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" => ShellTheme.Light,
        "auto" => ShellTheme.Auto,
        _ => ShellTheme.Dark
    };

    private string ThemeLabel() => _shellTheme switch
    {
        ShellTheme.Light => "淺色",
        ShellTheme.Auto => "自動",
        _ => "深色"
    };

    private void CycleShellTheme()
    {
        _shellTheme = _shellTheme switch
        {
            ShellTheme.Dark => ShellTheme.Light,
            ShellTheme.Light => ShellTheme.Auto,
            _ => ShellTheme.Dark
        };
        SaveSettingsIfPossible();
        RenderShell();
    }

    private void BuildInput(StackPanel page, ShellPalette palette)
    {
        page.Children.Add(Eyebrow("本機轉檔", palette));
        page.Children.Add(Gap(10));
        page.Children.Add(new TextBlock
        {
            Text = "留下這支影片",
            Foreground = palette.Primary,
            FontSize = 34,
            FontWeight = FontWeight.SemiBold
        });
        page.Children.Add(Gap(8));
        page.Children.Add(new TextBlock
        {
            Text = "貼上 YouTube 或 Bilibili 的網址。下一步會列出這支影片實際有的畫質，也可以只留聲音。",
            Foreground = palette.Gray,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22
        });
        page.Children.Add(Gap(16));
        page.Children.Add(PlatformPills(palette));
        page.Children.Add(Gap(22));

        var box = new TextBox
        {
            Text = _shellDraft,
            PlaceholderText = "貼上影片網址",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = palette.Primary,
            CaretBrush = palette.Accent,
            SelectionBrush = palette.Selection,
            FontSize = 15,
            Padding = new Thickness(0, 2),
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            _ = SubmitShellUrlAsync();
        };
        box.TextChanged += (_, _) =>
        {
            if (_shellRecallSilent)
            {
                return;
            }

            _shellDraft = box.Text ?? "";
            _shellRecallIndex = -1;
        };
        _shellUrlBox = box;
        page.Children.Add(UrlField(palette, box));
        page.Children.Add(Gap(14));
        page.Children.Add(PrimaryButton(palette, "查看格式", () => _ = SubmitShellUrlAsync()));

        _shellHintText = new TextBlock
        {
            Text = _shellWarning ?? "",
            Foreground = string.IsNullOrEmpty(_shellWarning) ? palette.Gray : palette.Warn,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 20,
            Margin = new Thickness(4, 12, 0, 0),
            Opacity = string.IsNullOrEmpty(_shellWarning) ? 0 : 1
        };
        page.Children.Add(_shellHintText);

        if (_toolsMissing)
        {
            page.Children.Add(Gap(16));
            page.Children.Add(BuildToolBanner(palette));
        }
    }

    private void BuildProbing(StackPanel page, ShellPalette palette)
    {
        var url = GetInputUrls().FirstOrDefault() ?? _shellDraft;
        _shellSpinner = new TextBlock
        {
            Text = SpinFrames[0],
            Foreground = palette.Accent,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        _shellStatusLine = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(_statusText.Text) ? "正在讀取影片資訊…" : _statusText.Text,
            Foreground = palette.Primary,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(_shellStatusLine);
        copy.Children.Add(new TextBlock
        {
            Text = TruncateMiddle(url, 64),
            Foreground = palette.Gray,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(_shellSpinner);
        Grid.SetColumn(copy, 1);
        copy.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(copy);
        page.Children.Add(SurfaceCard(palette, row));
    }

    private void BuildPicking(StackPanel page, ShellPalette palette)
    {
        if (_formatChoices.Count == 0)
        {
            _formatChoices = FormatChoiceBuilder.Fallback();
        }

        var info = _parsedInfo;
        var preview = BuildPreviewImage(palette);
        if (preview is not null)
        {
            page.Children.Add(preview);
        }

        page.Children.Add(Eyebrow("選擇格式", palette));
        page.Children.Add(Gap(8));
        var badge = VideoAccess.Badge(info?.Access ?? VideoAccess.Kind.Public);
        if (badge is not null)
        {
            page.Children.Add(AccessBadge(palette, badge));
            page.Children.Add(Gap(8));
        }

        page.Children.Add(new TextBlock
        {
            Text = info?.Title ?? "影片",
            Foreground = palette.Primary,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 32
        });
        page.Children.Add(Gap(6));
        page.Children.Add(new TextBlock
        {
            Text = BuildVideoMeta(info),
            Foreground = palette.Gray,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(info?.AccessWarning))
        {
            page.Children.Add(Gap(8));
            page.Children.Add(new TextBlock
            {
                Text = info.AccessWarning,
                Foreground = palette.Warn,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var extra = Math.Max(0, GetInputUrls().Length - 1);
        if (extra > 0)
        {
            page.Children.Add(Gap(8));
            page.Children.Add(new TextBlock
            {
                Text = $"另外 {extra} 個連結會套用同一個格式",
                Foreground = palette.Gray,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
        }

        page.Children.Add(Gap(12));
        var rows = new StackPanel { Spacing = 6 };
        for (var index = 0; index < _formatChoices.Count; index++)
        {
            rows.Children.Add(BuildFormatRow(palette, index));
        }

        page.Children.Add(rows);
        page.Children.Add(Gap(16));
        var downloadLabel = VideoAccess.RequiresAccount(info?.Access ?? VideoAccess.Kind.Public)
            ? "下載這支影片"
            : "下載所選格式";
        page.Children.Add(PrimaryButton(palette, downloadLabel, () => _ = DownloadCurrentChoiceAsync()));
        PaintFormatRows();
    }

    private Control BuildFormatRow(ShellPalette palette, int index)
    {
        var badgeText = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Border
        {
            Width = 52,
            Height = 36,
            CornerRadius = new CornerRadius(12),
            Child = badgeText
        };
        var title = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.Primary,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        var detail = new TextBlock
        {
            FontSize = 13,
            Foreground = palette.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        var copy = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(14, 0, 12, 0)
        };
        copy.Children.Add(title);
        copy.Children.Add(detail);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(badge);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var row = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(8, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid
        };
        _formatCards.Add(new FormatCard
        {
            Root = row,
            Badge = badge,
            BadgeText = badgeText,
            Title = title,
            Detail = detail
        });
        var captured = index;
        row.PointerEntered += (_, _) =>
        {
            if (_formatIndex == captured)
            {
                return;
            }

            _formatIndex = captured;
            PaintFormatRows();
        };
        row.PointerPressed += (_, e) =>
        {
            _formatIndex = captured;
            PaintFormatRows();
            e.Handled = true;
            _ = DownloadCurrentChoiceAsync();
        };
        return row;
    }

    private void PaintFormatRows()
    {
        var palette = CurrentPalette();
        for (var index = 0; index < _formatCards.Count && index < _formatChoices.Count; index++)
        {
            var selected = index == _formatIndex;
            var choice = _formatChoices[index];
            var card = _formatCards[index];
            var (badge, title, detail) = DescribeChoice(choice);
            card.BadgeText.Text = badge;
            card.Title.Text = title;
            card.Detail.Text = detail;
            card.Root.Background = selected ? palette.Soft : palette.Surface;
            card.Root.BorderBrush = selected ? palette.Accent : palette.Line;
            card.Root.BorderThickness = new Thickness(selected ? 2 : 1);
            card.Badge.Background = selected ? palette.Accent : palette.Track;
            card.BadgeText.Foreground = selected ? palette.ButtonText : palette.Primary;
        }
    }

    private static (string Badge, string Title, string Detail) DescribeChoice(FormatChoiceBuilder.Choice choice)
    {
        var size = SizeFromLabel(choice.Label);
        if (choice.Kind == "audio")
        {
            return ("MP3", "只要聲音", string.IsNullOrEmpty(size) ? "存成 MP3" : size);
        }

        var badge = choice.Quality.Equals("4K", StringComparison.OrdinalIgnoreCase)
            ? "4K"
            : choice.Quality.TrimEnd('P', 'p');
        return (badge, "MP4 影片", string.IsNullOrEmpty(size) ? "含畫面與聲音" : size);
    }

    private static string SizeFromLabel(string label)
    {
        var mark = label.LastIndexOf('~');
        return mark < 0 ? "" : "約 " + label[(mark + 1)..].Trim();
    }

    private void BuildDownloading(StackPanel page, ShellPalette palette)
    {
        var title = string.IsNullOrWhiteSpace(_parsedInfo?.Title) ? "正在儲存" : _parsedInfo!.Title;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Eyebrow("下載中", palette));
        body.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = palette.Primary,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(_shellDownloadLabel) ? "準備檔案" : FriendlyChoiceLabel(_shellDownloadLabel),
            Foreground = palette.Gray,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        _shellProgressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = _shellProcessing ? 100 : _shellPercent,
            Height = 12,
            Foreground = palette.Accent,
            Background = palette.Track,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        body.Children.Add(_shellProgressBar);
        var metaRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };
        if (_shellProcessing)
        {
            _shellSpinner = new TextBlock
            {
                Text = SpinFrames[0],
                Foreground = palette.Accent,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            metaRow.Children.Add(_shellSpinner);
        }

        _shellProgressMeta = new TextBlock
        {
            Text = _shellProcessing ? "正在合併或轉檔…" : _shellPercent <= 0 ? "開始寫入檔案" : $"{_shellPercent:0}%   {_shellSpeed}",
            Foreground = palette.Gray,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        metaRow.Children.Add(_shellProgressMeta);
        body.Children.Add(metaRow);
        page.Children.Add(SurfaceCard(palette, body));
    }

    private void BuildDone(StackPanel page, ShellPalette palette)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = palette.Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "✓",
                Foreground = palette.ButtonText,
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        body.Children.Add(new TextBlock
        {
            Text = "檔案已經存好",
            Foreground = palette.Primary,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold
        });
        var path = string.IsNullOrWhiteSpace(_shellDonePath) ? "輸出資料夾" : ShortenHome(_shellDonePath);
        var pathText = new TextBlock
        {
            Text = path,
            Foreground = palette.Gray,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        pathText.PointerPressed += (_, e) =>
        {
            OpenFinishedFile();
            e.Handled = true;
        };
        body.Children.Add(pathText);

        var summary = _statusText.Text?.Trim();
        if (!string.IsNullOrEmpty(summary))
        {
            body.Children.Add(new TextBlock
            {
                Text = summary,
                Foreground = palette.Gray,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };
        actions.Children.Add(PrimaryButton(palette, "開啟檔案", OpenFinishedFile));
        actions.Children.Add(GhostButton(palette, "再轉一支", () => ShowInput(clear: true)));
        body.Children.Add(actions);
        page.Children.Add(SurfaceCard(palette, body));
    }

    private void BuildError(StackPanel page, ShellPalette palette)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Eyebrow("沒有完成", palette));
        body.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(_shellError) ? "這個連結沒有轉成檔案。" : _shellError,
            Foreground = palette.Primary,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(PrimaryButton(palette, "返回修改", () => ShowInput(clear: false)));
        page.Children.Add(SurfaceCard(palette, body));
    }

    private void BuildHistory(StackPanel page, ShellPalette palette)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Eyebrow("紀錄", palette));
        body.Children.Add(new TextBlock
        {
            Text = "之前轉過的檔案",
            Foreground = palette.Primary,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, 8)
        });
        var files = _downloadItems.Where(item => item.State == DownloadState.Completed).Reverse().ToList();
        if (files.Count == 0 && _shellUrlHistory.Count == 0)
        {
            body.Children.Add(CenterText("還沒有紀錄。", palette.Gray, 14, FontWeight.Normal));
        }

        if (files.Count > 0)
        {
            body.Children.Add(SectionLabel("檔案", palette));
            foreach (var item in files.Take(12))
            {
                var captured = item;
                body.Children.Add(ClickLine(
                    palette,
                    item.Title,
                    $"{item.Format}  ·  {ShortenHome(item.OutputPath ?? "")}",
                    () => OpenMediaFile(captured.OutputPath, captured.Format)));
            }
        }

        if (_shellUrlHistory.Count > 0)
        {
            body.Children.Add(SectionLabel("連結", palette));
            foreach (var url in _shellUrlHistory.Take(12))
            {
                var captured = url;
                body.Children.Add(ClickLine(palette, captured, "再抓一次", () =>
                {
                    _shellDraft = captured;
                    _ = SubmitShellUrlAsync();
                }));
            }
        }

        _shellStatusLine = new TextBlock
        {
            Foreground = palette.Gray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        body.Children.Add(_shellStatusLine);
        page.Children.Add(body);
    }

    private void BuildSettings(StackPanel page, ShellPalette palette)
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(Eyebrow("設定", palette));
        body.Children.Add(new TextBlock
        {
            Text = "儲存與搜尋",
            Foreground = palette.Primary,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        });
        body.Children.Add(SectionLabel("輸出資料夾", palette));
        var folder = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        folder.Children.Add(new TextBlock
        {
            Text = ShortenHome(_outputBox.Text ?? ""),
            Foreground = palette.Primary,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var browse = GhostButton(palette, "瀏覽", () => ChooseFolderAsync(this, new RoutedEventArgs()));
        Grid.SetColumn(browse, 1);
        folder.Children.Add(browse);
        body.Children.Add(folder);

        body.Children.Add(BindCheck(palette, "搭配字幕（外掛 .srt，MP4 內嵌，MP3 產生 .lrc）", _includeSubtitles, value =>
        {
            if (_subtitleCheckBox.IsChecked != value)
            {
                _subtitleCheckBox.IsChecked = value;
            }
        }));
        body.Children.Add(BindCheck(palette, "下載整份播放清單", _downloadPlaylist, value =>
        {
            if (_playlistCheckBox.IsChecked != value)
            {
                _playlistCheckBox.IsChecked = value;
            }
        }));

        body.Children.Add(SectionLabel("Cookies", palette));
        var cookies = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        cookies.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(_cookiesFilePath) ? "未設定" : IoPath.GetFileName(_cookiesFilePath),
            Foreground = string.IsNullOrEmpty(_cookiesFilePath) ? palette.Gray : palette.Primary,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var importCookies = GhostButton(palette, "匯入", () => ChooseCookiesFileAsync(this, new RoutedEventArgs()));
        Grid.SetColumn(importCookies, 1);
        cookies.Children.Add(importCookies);
        var clearCookies = GhostButton(palette, "清除", () => _cookiesClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Grid.SetColumn(clearCookies, 2);
        cookies.Children.Add(clearCookies);
        body.Children.Add(cookies);

        body.Children.Add(SectionLabel("搜尋 YouTube / Bilibili", palette));
        var search = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _shellSearchBox = new TextBox
        {
            Text = _shellSearchDraft,
            PlaceholderText = "搜尋影片標題",
            Background = palette.Background,
            BorderBrush = palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Foreground = palette.Primary,
            CaretBrush = palette.Accent,
            Padding = new Thickness(12, 8),
            FontSize = 14,
            MinHeight = 40
        };
        _shellSearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            await RunShellSearchAsync();
        };
        search.Children.Add(_shellSearchBox);
        var searchButton = GhostButton(palette, "搜尋", () => _ = RunShellSearchAsync());
        searchButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(searchButton, 1);
        search.Children.Add(searchButton);
        body.Children.Add(search);

        if (_recentSearches.Count > 0)
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var entry in _recentSearches.Take(8))
            {
                var captured = entry;
                var chip = GhostButton(palette, captured.Query, () =>
                {
                    _shellSearchDraft = captured.Query;
                    if (_shellSearchBox is not null)
                    {
                        _shellSearchBox.Text = captured.Query;
                    }

                    _ = RunShellSearchAsync();
                });
                chip.Margin = new Thickness(0, 0, 6, 6);
                chips.Children.Add(chip);
            }

            body.Children.Add(chips);
        }

        var status = _shellSearching ? "搜尋中…" : _searchStatusText.Text;
        if (!string.IsNullOrWhiteSpace(status))
        {
            body.Children.Add(new TextBlock
            {
                Text = status,
                Foreground = palette.Gray,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (_searchResults.Count > 0)
        {
            var results = new StackPanel { Spacing = 6 };
            foreach (var item in _searchResults.Take(8))
            {
                var captured = item;
                var meta = $"{captured.Platform}  ·  {FormatDuration(captured.DurationSeconds)}";
                results.Children.Add(ClickLine(palette, captured.Title, meta, () => _ = UseSearchResultAsync(captured)));
            }

            body.Children.Add(new ScrollViewer
            {
                Content = results,
                MaxHeight = 220,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
        }

        _shellStatusLine = new TextBlock
        {
            Foreground = palette.Gray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(_shellStatusLine);
        page.Children.Add(body);
    }

    private Control BuildToolBanner(ShellPalette palette)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = "還差轉檔工具",
            Foreground = palette.Primary,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "這台電腦需要 yt-dlp 和 ffmpeg，裝好之後就能開始。",
            Foreground = palette.Gray,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        var os = ToolSetupGuide.CurrentOs;
        var command = ToolSetupGuide.GetInstallCommand(os, os == "osx" && ToolSetupGuide.HasHomebrew());
        stack.Children.Add(new TextBlock
        {
            Text = command,
            Foreground = palette.Gray,
            FontFamily = MonoFont(),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 8
        };
        if (ToolSetupGuide.SupportsOneClickInstall(os))
        {
            buttons.Children.Add(OutlineButton(palette, "一鍵安裝", LaunchToolInstaller));
        }

        buttons.Children.Add(OutlineButton(palette, "複製指令", () => _ = CopyInstallCommandAsync()));
        buttons.Children.Add(OutlineButton(palette, "重新檢查", () => RefreshToolSetup()));
        stack.Children.Add(buttons);
        return SurfaceCard(palette, stack);
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CycleShellTheme();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemComma
            && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            && _shellPhase is not (ShellPhase.Probing or ShellPhase.Downloading))
        {
            ShowSettings();
            e.Handled = true;
            return;
        }

        switch (_shellPhase)
        {
            case ShellPhase.Input when e.Key == Key.Tab
                && !string.IsNullOrEmpty(_shellClipboardUrl)
                && string.IsNullOrWhiteSpace(_shellUrlBox?.Text):
                AcceptClipboardUrl();
                e.Handled = true;
                break;
            case ShellPhase.Input when e.Key == Key.Up && CanRecallUrl():
                RecallUrl(1);
                e.Handled = true;
                break;
            case ShellPhase.Input when e.Key == Key.Up
                && string.IsNullOrWhiteSpace(_shellUrlBox?.Text)
                && HasCompletedDownloads():
                ShowHistory();
                e.Handled = true;
                break;
            case ShellPhase.Input when e.Key == Key.Down && _shellRecallIndex >= 0:
                RecallUrl(-1);
                e.Handled = true;
                break;
            case ShellPhase.Picking:
                HandlePickingKey(e);
                break;
            case ShellPhase.Probing or ShellPhase.Downloading when e.Key == Key.Escape:
                CancelShellActivity();
                e.Handled = true;
                break;
            case ShellPhase.Done when e.Key == Key.Enter:
                ShowInput(clear: true);
                e.Handled = true;
                break;
            case ShellPhase.Error when e.Key == Key.Enter:
                ShowInput(clear: false);
                e.Handled = true;
                break;
            case ShellPhase.Done or ShellPhase.Error or ShellPhase.History or ShellPhase.Settings
                when e.Key == Key.Escape:
                ShowInput(clear: false);
                e.Handled = true;
                break;
        }
    }

    private void HandlePickingKey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key is Key.Up or Key.K)
        {
            MoveFormat(-1);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.J)
        {
            MoveFormat(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = DownloadCurrentChoiceAsync();
            return;
        }

        if (e.Key == Key.Escape)
        {
            ShowInput(clear: false);
            e.Handled = true;
            return;
        }

        var index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => -1
        };
        if (index >= 0 && index < _formatChoices.Count)
        {
            _formatIndex = index;
            PaintFormatRows();
            e.Handled = true;
        }
    }

    private async Task SubmitShellUrlAsync()
    {
        if (_shellUrlBox is not null)
        {
            _shellDraft = _shellUrlBox.Text ?? "";
        }

        var urls = ExtractHttpUrls(_shellDraft);
        if (urls.Count == 0)
        {
            _shellWarning = "這看起來不是連結 — 請貼上完整網址";
            _shellPhase = ShellPhase.Input;
            RenderShell();
            return;
        }

        _shellWarning = null;
        _urlBox.Text = string.Join(Environment.NewLine, urls);
        _formatChoices = [];
        var generation = ++_probeGeneration;
        _shellPhase = ShellPhase.Probing;
        RenderShell();
        try
        {
            await ParseUrlCoreAsync();
        }
        catch (Exception ex)
        {
            if (generation != _probeGeneration)
            {
                return;
            }

            _shellError = ex.Message;
            _shellPhase = ShellPhase.Error;
            RenderShell();
            return;
        }

        if (generation != _probeGeneration)
        {
            return;
        }

        if (_parsedInfo is null)
        {
            _shellError = FirstNonEmpty(_parseErrorText.Text, _statusText.Text, "無法讀取這個連結。");
            _shellPhase = ShellPhase.Error;
        }
        else
        {
            RememberShellUrl(urls[0]);
            if (_formatChoices.Count == 0)
            {
                _formatChoices = FormatChoiceBuilder.Fallback();
            }

            _formatIndex = 0;
            _shellPhase = ShellPhase.Picking;
        }

        RenderShell();
    }

    private async Task DownloadCurrentChoiceAsync()
    {
        if (_shellPhase == ShellPhase.Downloading || _conversionTokenSource is not null)
        {
            return;
        }

        if (_formatIndex < 0 || _formatIndex >= _formatChoices.Count)
        {
            return;
        }

        var choice = _formatChoices[_formatIndex];
        _shellDownloadLabel = choice.Label;
        _shellPercent = 0;
        _shellSpeed = "";
        _shellProcessing = false;
        _shellCancelRequested = false;
        _shellSawDownload = false;
        if (choice.Kind == "audio")
        {
            SetOutputFormat("MP3");
        }
        else
        {
            _mp4Quality = string.IsNullOrEmpty(choice.Quality) ? "1080P" : choice.Quality;
            SetOutputFormat("MP4");
        }

        _shellPhase = ShellPhase.Downloading;
        RenderShell();
        await ConvertOrCancelCoreAsync();
        if (_shellPhase != ShellPhase.Downloading)
        {
            return;
        }

        if (_shellCancelRequested)
        {
            _shellCancelRequested = false;
            ShowInput(clear: false);
            return;
        }

        if (!_shellSawDownload)
        {
            _shellError = FirstNonEmpty(_statusText.Text, "無法開始下載。");
            _shellPhase = ShellPhase.Error;
            RenderShell();
            return;
        }

        var urls = GetInputUrls();
        var item = urls.Length == 0
            ? _downloadItems.LastOrDefault()
            : _downloadItems.LastOrDefault(entry => UrlsLikelySameVideo(entry.Url, urls[0]));
        if (item?.State == DownloadState.Completed)
        {
            _shellDonePath = string.IsNullOrWhiteSpace(item.OutputPath) ? _outputBox.Text : item.OutputPath;
            _shellPhase = ShellPhase.Done;
        }
        else if (item?.State == DownloadState.Cancelled)
        {
            ShowInput(clear: false);
            return;
        }
        else
        {
            _shellError = DownloadFailureText.Summarize(
                _statusText.Text,
                RecentLogTail(),
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
            _shellPhase = ShellPhase.Error;
        }

        RenderShell();
    }

    private async Task RunShellSearchAsync()
    {
        if (_shellSearching)
        {
            return;
        }

        var query = (_shellSearchBox?.Text ?? _shellSearchDraft).Trim();
        _shellSearchDraft = query;
        _searchBox.Text = query;
        _searchPlatform = "both";
        _shellSearching = true;
        if (_shellStatusLine is not null)
        {
            _shellStatusLine.Text = "搜尋中…";
        }

        try
        {
            await SearchVideosCoreAsync();
        }
        finally
        {
            _shellSearching = false;
        }

        if (_shellPhase == ShellPhase.Settings)
        {
            RenderShell();
        }
    }

    private async Task UseSearchResultAsync(SearchVideoResult item)
    {
        _shellDraft = item.Url;
        _shellWarning = null;
        await SubmitShellUrlAsync();
    }

    private async Task RefreshClipboardHintAsync()
    {
        if (_shellPhase != ShellPhase.Input || _shellHintText is null || !string.IsNullOrEmpty(_shellWarning))
        {
            return;
        }

        string? url = null;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                var text = await clipboard.TryGetTextAsync();
                url = string.IsNullOrWhiteSpace(text) ? null : ExtractHttpUrls(text).FirstOrDefault();
            }
        }
        catch
        {
            url = null;
        }

        if (_shellPhase != ShellPhase.Input || _shellHintText is null || !string.IsNullOrEmpty(_shellWarning))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_shellUrlBox?.Text))
        {
            _shellClipboardUrl = null;
            return;
        }

        _shellClipboardUrl = url;
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        _shellHintText.Text = "剪貼簿裡有網址，可以按 Tab 貼上";
        _shellHintText.Foreground = CurrentPalette().Gray;
        _shellHintText.Opacity = 1;
    }

    private async Task CopyInstallCommandAsync()
    {
        var os = ToolSetupGuide.CurrentOs;
        var command = ToolSetupGuide.GetInstallCommand(os, os == "osx" && ToolSetupGuide.HasHomebrew());
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetStatus("複製失敗，請手動選取指令");
                return;
            }

            await clipboard.SetTextAsync(command);
            SetStatus("已複製安裝指令，請貼到終端機執行");
        }
        catch (Exception ex)
        {
            SetStatus("複製失敗，請手動選取指令");
            AppendLog(ex.Message);
        }
    }

    private void AcceptClipboardUrl()
    {
        if (_shellUrlBox is null || string.IsNullOrEmpty(_shellClipboardUrl))
        {
            return;
        }

        _shellRecallSilent = true;
        _shellUrlBox.Text = _shellClipboardUrl;
        _shellUrlBox.CaretIndex = _shellUrlBox.Text?.Length ?? 0;
        _shellRecallSilent = false;
        _shellDraft = _shellUrlBox.Text ?? "";
        _shellClipboardUrl = null;
        if (_shellHintText is not null)
        {
            _shellHintText.Text = "已貼上剪貼簿的網址";
            _shellHintText.Foreground = CurrentPalette().Gray;
            _shellHintText.Opacity = 1;
        }
    }

    private void ShowInput(bool clear)
    {
        if (clear)
        {
            _shellDraft = "";
            _shellWarning = null;
            _shellRecallIndex = -1;
        }

        _shellError = null;
        _shellPhase = ShellPhase.Input;
        RenderShell();
    }

    private void ShowHistory()
    {
        _shellPhase = ShellPhase.History;
        RenderShell();
    }

    private void ShowSettings()
    {
        _shellPhase = ShellPhase.Settings;
        RenderShell();
    }

    private void CancelShellActivity()
    {
        if (_shellPhase == ShellPhase.Probing)
        {
            _probeGeneration++;
            ShowInput(clear: false);
            return;
        }

        if (_shellPhase == ShellPhase.Downloading)
        {
            _shellCancelRequested = true;
            _conversionTokenSource?.Cancel();
        }
    }

    private void MoveFormat(int delta)
    {
        if (_formatChoices.Count == 0)
        {
            return;
        }

        _formatIndex = (_formatIndex + delta + _formatChoices.Count) % _formatChoices.Count;
        PaintFormatRows();
    }

    private bool CanRecallUrl()
    {
        if (_shellUrlHistory.Count == 0 || _shellUrlBox is null)
        {
            return false;
        }

        var text = _shellUrlBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return _shellRecallIndex >= 0
            && _shellRecallIndex < _shellUrlHistory.Count
            && string.Equals(text, _shellUrlHistory[_shellRecallIndex], StringComparison.Ordinal);
    }

    private void RecallUrl(int delta)
    {
        if (_shellUrlBox is null || _shellUrlHistory.Count == 0)
        {
            return;
        }

        if (delta < 0 && _shellRecallIndex <= 0)
        {
            _shellRecallIndex = -1;
            _shellRecallSilent = true;
            _shellUrlBox.Text = "";
            _shellRecallSilent = false;
            _shellDraft = "";
            return;
        }

        if (_shellRecallIndex < 0)
        {
            _shellRecallIndex = 0;
        }
        else
        {
            _shellRecallIndex = Math.Clamp(_shellRecallIndex + delta, 0, _shellUrlHistory.Count - 1);
        }

        _shellRecallSilent = true;
        _shellUrlBox.Text = _shellUrlHistory[_shellRecallIndex];
        _shellUrlBox.CaretIndex = _shellUrlBox.Text?.Length ?? 0;
        _shellRecallSilent = false;
        _shellDraft = _shellUrlBox.Text ?? "";
    }

    private void RememberShellUrl(string url)
    {
        _shellUrlHistory.RemoveAll(entry => string.Equals(entry, url, StringComparison.OrdinalIgnoreCase));
        _shellUrlHistory.Insert(0, url);
        if (_shellUrlHistory.Count > 20)
        {
            _shellUrlHistory.RemoveAt(_shellUrlHistory.Count - 1);
        }

        _shellRecallIndex = -1;
    }

    private bool HasCompletedDownloads() =>
        _downloadItems.Any(item => item.State == DownloadState.Completed);

    private List<string> ExtractHttpUrls(string text) =>
        text.Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(url => NormalizeMediaUrl(url, preservePlaylistParams: _downloadPlaylist))
            .Where(url => url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string BuildVideoMeta(ParsedVideoInfo? info)
    {
        if (info is null)
        {
            return "▸ 影片";
        }

        var parts = new List<string> { DescribePlatform(info) };
        var duration = FormatDuration(info.DurationSeconds);
        if (duration != "-")
        {
            parts.Add(duration);
        }

        if (!string.IsNullOrWhiteSpace(info.ChannelName))
        {
            parts.Add(info.ChannelName);
        }

        return "▸  " + string.Join("  ·  ", parts);
    }

    private string DescribePlatform(ParsedVideoInfo info)
    {
        var url = string.IsNullOrWhiteSpace(info.WebpageUrl) ? info.Url : info.WebpageUrl;
        if (IsBilibiliVideoUrl(url) || (info.ExtractorKey?.Contains("bili", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return "Bilibili";
        }

        if (YoutubeDownloadPolicy.IsYouTubeUrl(url)
            || (info.ExtractorKey?.Contains("youtube", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return "YouTube";
        }

        return string.IsNullOrWhiteSpace(info.ExtractorKey) ? "影片" : info.ExtractorKey;
    }

    private void OpenFinishedFile()
    {
        if (string.IsNullOrWhiteSpace(_shellDonePath))
        {
            return;
        }

        if (File.Exists(_shellDonePath))
        {
            OpenMediaFile(_shellDonePath, _outputFormat);
            return;
        }

        if (Directory.Exists(_shellDonePath))
        {
            OpenOutputFolder(_shellDonePath);
        }
    }

    private Control? BuildPreviewImage(ShellPalette palette)
    {
        var url = _parsedInfo?.ThumbnailUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Height = 150,
            Source = string.Equals(_shellPreviewUrl, url, StringComparison.Ordinal) ? _shellPreviewBitmap : null
        };
        if (image.Source is null)
        {
            _ = LoadShellPreviewAsync(url, image);
        }

        _shellPreviewFrame = new Border
        {
            Height = 150,
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Background = palette.Track,
            Child = image
        };
        return _shellPreviewFrame;
    }

    private double MaxClientHeight()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen is null)
        {
            return 860;
        }

        // Height is the client area. The macOS title bar sits outside it.
        return Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 64);
    }

    private void FitWindowToContent(Control root, Border stage)
    {
        if (_fittingWindow)
        {
            return;
        }

        var limit = MaxClientHeight();
        var width = Math.Max(Width, MinWidth);
        double Measure()
        {
            root.Measure(new Size(width, double.PositiveInfinity));
            return Math.Ceiling(root.DesiredSize.Height);
        }

        var desired = Measure();
        if (_shellPreviewFrame is not null && desired > limit)
        {
            var next = _shellPreviewFrame.Height - (desired - limit) - 4;
            if (_shellPreviewFrame.Child is Image image)
            {
                image.Height = Math.Max(0, next);
            }

            if (next < 72)
            {
                _shellPreviewFrame.IsVisible = false;
                _shellPreviewFrame.Height = 0;
                _shellPreviewFrame.Margin = new Thickness(0);
            }
            else
            {
                _shellPreviewFrame.Height = next;
            }

            desired = Measure();
        }

        var target = Math.Clamp(Math.Max(desired + 2, 700), MinHeight, limit);
        if (target > desired)
        {
            stage.MinHeight = stage.DesiredSize.Height + (target - desired);
        }

        _fittingWindow = true;
        try
        {
            if (Math.Abs(Height - target) > 1)
            {
                Height = target;
            }

            var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
            if (screen is not null)
            {
                var area = screen.WorkingArea;
                var scale = screen.Scaling;
                var bottom = Position.Y + (Height + 58) * scale;
                var overflow = bottom - (area.Y + area.Height);
                if (overflow > 0)
                {
                    Position = new PixelPoint(
                        Position.X,
                        Math.Max(area.Y, Position.Y - (int)Math.Ceiling(overflow)));
                }
            }
        }
        finally
        {
            _fittingWindow = false;
        }
    }

    private async Task LoadShellPreviewAsync(string url, Image image)
    {
        var version = Interlocked.Increment(ref _shellPreviewVersion);
        try
        {
            var referer = _parsedInfo is not null && IsBilibiliVideoUrl(_parsedInfo.Url)
                ? "https://www.bilibili.com/"
                : null;
            var bytes = await AppHttp.GetBytesAsync(url, referer);
            if (version != _shellPreviewVersion)
            {
                return;
            }

            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _shellPreviewVersion)
                {
                    bitmap.Dispose();
                    return;
                }

                _shellPreviewBitmap?.Dispose();
                _shellPreviewBitmap = bitmap;
                _shellPreviewUrl = url;
                image.Source = bitmap;
            });
        }
        catch (Exception ex)
        {
            if (version == _shellPreviewVersion)
            {
                AppendLog($"縮圖載入失敗: {ex.Message}");
            }
        }
    }

    private static Border AccessBadge(ShellPalette palette, string label) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = palette.Soft,
        BorderBrush = palette.Warn,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(10, 4),
        Child = new TextBlock
        {
            Text = label,
            Foreground = palette.Warn,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        }
    };

    private LinearGradientBrush AccentGradient() => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(ShellIsDark() ? "#0E8F88" : "#0B7C76"), 0),
            new GradientStop(Color.Parse(ShellIsDark() ? "#3DCFC6" : "#14B8A6"), 0.55),
            new GradientStop(Color.Parse(ShellIsDark() ? "#7AA2FF" : "#3B6FE0"), 1)
        }
    };

    private string? RecentLogTail()
    {
        var text = _logText.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var start = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("[", StringComparison.Ordinal) && lines[index].Contains("] http", StringComparison.Ordinal))
            {
                start = index;
            }
        }

        var slice = lines[start..];
        var take = slice.Length <= 40 ? slice : slice[^40..];
        return string.Join('\n', take);
    }

    private void NotifyShellProgress(double percent, string speed)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shellProcessing || _shellPhase != ShellPhase.Downloading)
            {
                return;
            }

            _shellPercent = percent;
            _shellSpeed = speed;
            try
            {
                if (_shellProgressBar is not null)
                {
                    _shellProgressBar.Value = percent;
                }

                if (_shellProgressMeta is not null)
                {
                    _shellProgressMeta.Text = $"{percent:0}%   {speed}";
                }
            }
            catch
            {
                // The window can close while yt-dlp is still writing progress.
            }
        });
    }

    private void NotifyShellProcessing()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shellPhase != ShellPhase.Downloading)
            {
                return;
            }

            _shellProcessing = true;
            try
            {
                if (_shellProgressBar is not null)
                {
                    _shellProgressBar.Value = 100;
                }

                if (_shellProgressMeta is not null)
                {
                    _shellProgressMeta.Text = "處理中…";
                }
            }
            catch
            {
                // Ignore UI races after the window closes.
            }
        });
    }

    private void OnShellToolStateChanged(bool changed)
    {
        if (!changed || _shellCenter is null || _shellRendering || _shellPhase != ShellPhase.Input)
        {
            return;
        }

        RenderShell();
    }

    private void RefreshShellChrome()
    {
        if (_shellCenter is null)
        {
            return;
        }

        if (_shellPhase is ShellPhase.Settings or ShellPhase.Input or ShellPhase.History)
        {
            RenderShell();
        }
    }

    private void LogoClicked()
    {
        if (_shellPhase is ShellPhase.Probing or ShellPhase.Downloading)
        {
            CancelShellActivity();
            return;
        }

        if (_shellPhase != ShellPhase.Input)
        {
            ShowInput(clear: false);
        }
    }

    private Control BuildAppBar(ShellPalette palette)
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 18, 28, 0)
        };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        brand.Children.Add(BuildMark(palette));
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        titles.Children.Add(new TextBlock
        {
            Text = "影音轉換大師",
            Foreground = palette.Primary,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });
        titles.Children.Add(new TextBlock
        {
            Text = "YouTube 與 Bilibili",
            Foreground = palette.Gray,
            FontSize = 12
        });
        brand.Children.Add(titles);
        brand.PointerPressed += (_, e) =>
        {
            LogoClicked();
            e.Handled = true;
        };
        bar.Children.Add(brand);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_shellPhase is ShellPhase.Probing or ShellPhase.Downloading)
        {
            actions.Children.Add(GhostButton(palette, "取消", CancelShellActivity));
        }
        else if (_shellPhase is ShellPhase.History or ShellPhase.Settings or ShellPhase.Picking or ShellPhase.Done or ShellPhase.Error)
        {
            actions.Children.Add(GhostButton(palette, "回首頁", () => ShowInput(clear: false)));
            if (_shellPhase != ShellPhase.Settings)
            {
                actions.Children.Add(GhostButton(palette, "設定", ShowSettings));
            }
        }
        else
        {
            actions.Children.Add(GhostButton(palette, "紀錄", ShowHistory));
            actions.Children.Add(GhostButton(palette, "設定", ShowSettings));
        }

        actions.Children.Add(GhostButton(palette, ThemeLabel(), CycleShellTheme));
        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);

        var wrap = new StackPanel { Spacing = 14 };
        wrap.Children.Add(bar);
        wrap.Children.Add(new Border
        {
            Height = 3,
            Background = AccentGradient()
        });
        return wrap;
    }

    private Control BuildMark(ShellPalette palette)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://YoutubeOrBilibiliMP3Converter/Assets/app-icon.png"));
            var bitmap = new Bitmap(stream);
            return new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Child = new Image
                {
                    Source = bitmap,
                    Width = 36,
                    Height = 36,
                    Stretch = Stretch.UniformToFill
                }
            };
        }
        catch
        {
            return new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = palette.Accent,
                Child = new TextBlock
                {
                    Text = "影",
                    Foreground = palette.ButtonText,
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }

    private Control UrlField(ShellPalette palette, TextBox box)
    {
        var paste = GhostButton(palette, "貼上", () => _ = PasteClipboardIntoUrlAsync());
        paste.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(box);
        Grid.SetColumn(paste, 1);
        grid.Children.Add(paste);
        return new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 8, 8, 8),
            MinHeight = 58,
            Child = grid
        };
    }

    private async Task PasteClipboardIntoUrlAsync()
    {
        string? url = null;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                var text = await clipboard.TryGetTextAsync();
                url = string.IsNullOrWhiteSpace(text) ? null : ExtractHttpUrls(text).FirstOrDefault();
            }
        }
        catch
        {
            url = null;
        }

        if (string.IsNullOrEmpty(url))
        {
            _shellWarning = "剪貼簿裡沒有可用的網址";
            _shellPhase = ShellPhase.Input;
            RenderShell();
            return;
        }

        _shellClipboardUrl = url;
        AcceptClipboardUrl();
    }

    private Button PrimaryButton(ShellPalette palette, string text, Action onClick, bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.45,
            Foreground = palette.ButtonText,
            Background = palette.ButtonFill,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(22, 12),
            MinHeight = 46,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control SurfaceCard(ShellPalette palette, Control child) => new Border
    {
        Background = palette.Surface,
        BorderBrush = palette.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(22),
        Padding = new Thickness(22, 20),
        Child = child
    };

    private Control PlatformPills(ShellPalette palette)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        row.Children.Add(Pill(palette, "YouTube"));
        row.Children.Add(Pill(palette, "Bilibili"));
        return row;
    }

    private static Border Pill(ShellPalette palette, string text) => new()
    {
        Background = palette.Surface,
        BorderBrush = palette.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(12, 5),
        Child = new TextBlock
        {
            Text = text,
            Foreground = palette.Primary,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        }
    };

    private static TextBlock Eyebrow(string text, ShellPalette palette) => new()
    {
        Text = text,
        Foreground = palette.Accent,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold
    };

    private static string FriendlyChoiceLabel(string label)
    {
        if (label.Contains("僅音訊", StringComparison.Ordinal) || label.Contains("mp3", StringComparison.OrdinalIgnoreCase))
        {
            var size = SizeFromLabel(label);
            return string.IsNullOrEmpty(size) ? "只要聲音 · MP3" : $"只要聲音 · {size}";
        }

        var height = label.Split('·', StringSplitOptions.TrimEntries).FirstOrDefault() ?? label;
        var bytes = SizeFromLabel(label);
        return string.IsNullOrEmpty(bytes) ? $"MP4 · {height}" : $"MP4 · {height} · {bytes}";
    }

    private static Control Gap(double height) => new Border { Height = height, Width = 1 };

    private static TextBlock CenterText(string text, IBrush foreground, double size, FontWeight weight, double maxWidth = 640) =>
        new()
        {
            Text = text,
            Foreground = foreground,
            FontSize = size,
            FontWeight = weight,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth
        };

    private static TextBlock SectionLabel(string text, ShellPalette palette) => new()
    {
        Text = text,
        Foreground = palette.Gray,
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 0)
    };

    private static FontFamily MonoFont() => new(PlatformCopy.MonoFontFamily);

    private Button GhostButton(ShellPalette palette, string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Foreground = palette.Primary,
            Background = palette.Surface,
            BorderBrush = palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(14, 8),
            FontSize = 13,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private Control OutlineButton(ShellPalette palette, string text, Action onClick)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = palette.Primary,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var border = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label
        };
        border.PointerPressed += (_, e) =>
        {
            onClick();
            e.Handled = true;
        };
        return border;
    }

    private CheckBox BindCheck(ShellPalette palette, string label, bool value, Action<bool> onChanged)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = value,
            Foreground = palette.Primary,
            FontSize = 14
        };
        box.IsCheckedChanged += (_, _) => onChanged(box.IsChecked == true);
        return box;
    }

    private Control ClickLine(ShellPalette palette, string title, string meta, Action onClick)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = palette.Primary,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(meta))
        {
            stack.Children.Add(new TextBlock
            {
                Text = meta,
                Foreground = palette.Gray,
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var row = new Border
        {
            Background = palette.Background,
            BorderBrush = palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack
        };
        row.PointerPressed += (_, e) =>
        {
            onClick();
            e.Handled = true;
        };
        return row;
    }

    private static string ShortenHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..];
        }

        return path;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string TruncateMiddle(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var head = Math.Max(1, (max - 1) / 2);
        var tail = Math.Max(1, max - 1 - head);
        return text[..head] + "…" + text[^tail..];
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
