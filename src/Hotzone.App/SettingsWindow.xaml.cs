using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace Hotzone.App;

/// <summary>
/// 환경설정: 왼쪽 메뉴(일반·모양·핫존·알림·작업 기록) + 오른쪽 페이지.
/// 바꾸는 즉시 저장하고 <see cref="Changed"/>로 알린다. 작업 기록은 진행 중인 작업까지 실시간으로 보여준다.
/// </summary>
public partial class SettingsWindow : Window
{
    private const double Inset = 4, DotSize = 16, DotInset = 14;
    private static readonly int[] DismissChoices = [1, 3, 5, 10, 0];

    private static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF); // iOS 시스템 블루
    private static readonly Brush AccentBrush = new SolidColorBrush(Accent);
    private static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x4E));
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly JobHistory _history;
    private readonly (string Key, string Glyph, string Label, FrameworkElement Page)[] _pages;
    private AppSettings _settings;
    private string _page = "general";

    public event Action<AppSettings>? Changed;

    public SettingsWindow(AppSettings settings, JobHistory history)
    {
        _settings = settings;
        _history = history;
        InitializeComponent();
        _pages =
        [
            ("general", "", "일반", GeneralPage),
            ("look", "", "모양", LookPage),
            ("hotzone", "", "핫존", HotzonePage),
            ("toast", "", "알림", ToastPage),
            ("history", "", "작업 기록", HistoryPage),
        ];
        AppIcon.Source = TrayIcon.ImageSource;
        VersionText.Text = $"버전 {AppInfo.Version}";
        StartupRow.MouseLeftButtonUp += (_, _) => Update(_settings with { LaunchAtStartup = !_settings.LaunchAtStartup });
        ToastRow.MouseLeftButtonUp += (_, _) => Update(_settings with { ShowToasts = !_settings.ShowToasts });
        ClearHistory.MouseLeftButtonUp += (_, _) => _history.ClearFinished();
        // 작업 기록은 새 작업이 생기거나 지워질 때 다시 그린다 (진행률은 줄마다 스스로 갱신)
        _history.ListChanged += RenderHistory;
        Closed += (_, _) => _history.ListChanged -= RenderHistory;
        Loaded += (_, _) => { Render(); ShowPage(_page); };
    }

    /// <summary>다른 곳에서 특정 페이지로 열 때 (예: 트레이의 "작업 기록").</summary>
    public void ShowPage(string key)
    {
        _page = key;
        foreach (var (k, _, _, page) in _pages) page.Visibility = k == key ? Visibility.Visible : Visibility.Collapsed;
        RenderNav();
        Scroller.ScrollToTop();
    }

    private void Update(AppSettings settings)
    {
        _settings = settings;
        try { settings.Save(); }
        catch (Exception ex) { Log.Error("설정 저장 실패", ex); }
        Changed?.Invoke(settings);
        Render();
    }

    private void Render()
    {
        RenderModes();
        RenderThemes();
        SetToggle(StartupToggle, StartupKnob, _settings.LaunchAtStartup);
        SetToggle(ToastToggle, ToastKnob, _settings.ShowToasts);
        ToastOptions.IsEnabled = _settings.ShowToasts;
        ToastOptions.Opacity = _settings.ShowToasts ? 1 : 0.4;
        RenderDismissOptions();

        // 커서 모드에서는 핫존을 쓰지 않으니 위치 설정을 흐리게 막아 둔다
        var hotzone = _settings.Mode == OpenMode.Hotzone;
        PositionSection.IsEnabled = hotzone;
        PositionSection.Opacity = hotzone ? 1 : 0.4;
        PositionHint.Text = hotzone
            ? "모니터를 고른 뒤 점 8개 중 하나를 누르세요. 스페이스를 누른 채 그 위치로 가져가면 패널이 열립니다."
            : "핫존 모드에서만 씁니다. '일반 → 열기 방식'에서 '핫존으로 가져가서'를 고르세요.";

        var number = RenderMap(Layout, _settings.Monitor, _settings.Position, Enum.GetValues<HotzonePosition>(),
            d => _settings with { Monitor = d }, p => _settings with { Position = p }, AddHotzonePreview);
        Selection.Text = $"{number}번 모니터 · {PositionName(_settings.Position)}";
        Warning.Visibility = TouchesOtherMonitor(Edge.Screen(_settings.Monitor), _settings.Position) ? Visibility.Visible : Visibility.Collapsed;

        HotzonePosition[] corners = [HotzonePosition.TopLeft, HotzonePosition.TopRight, HotzonePosition.BottomLeft, HotzonePosition.BottomRight];
        var toastNumber = RenderMap(ToastLayout, _settings.ToastMonitor, _settings.ToastPosition, corners,
            d => _settings with { ToastMonitor = d }, p => _settings with { ToastPosition = p }, AddToastPreview);
        ToastSelection.Text = $"{toastNumber}번 모니터 · {PositionName(_settings.ToastPosition)}";

        RenderHistory();
    }

    private void RenderNav()
    {
        Nav.Children.Clear();
        foreach (var (key, glyph, label, _) in _pages)
        {
            var current = key == _page;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 15, Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = current ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)) });
            row.Children.Add(new TextBlock { Text = label, FontSize = 14, Margin = new Thickness(8, 0, 0, 0), FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            var item = new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(12, 9, 12, 9),
                CornerRadius = new CornerRadius(10),
                Background = current ? AccentBrush : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = row,
            };
            if (!current)
            {
                item.MouseEnter += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            }
            item.MouseLeftButtonUp += (_, _) => ShowPage(key);
            Nav.Children.Add(item);
        }
    }

    private static void SetToggle(Border track, Ellipse knob, bool on)
    {
        track.Background = on ? AccentBrush : OffBrush;
        knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    /// <summary>1·3·5·10초 / 안 사라짐 중 하나를 고르는 알약 버튼들.</summary>
    private void RenderDismissOptions()
    {
        DismissOptions.Children.Clear();
        foreach (var seconds in DismissChoices)
        {
            var current = _settings.ToastDismissSeconds == seconds;
            var pill = new Border
            {
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(16, 7, 16, 7),
                CornerRadius = new CornerRadius(16),
                Background = current ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(current ? 0 : 1),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = seconds == 0 ? "안 사라짐 (닫기로만)" : $"{seconds}초", FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal },
            };
            pill.MouseLeftButtonUp += (_, _) => { if (!current) Update(_settings with { ToastDismissSeconds = seconds }); };
            DismissOptions.Children.Add(pill);
        }
    }

    private void RenderHistory()
    {
        HistoryList.Children.Clear();
        var theme = PanelTheme.Get(PanelThemeKind.GlassDark);
        var line = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < _history.Items.Count; i++)
        {
            var row = new JobRowView(_history.Items[i], theme, showTime: true)
            {
                BorderBrush = line,
                BorderThickness = new Thickness(0, 0, 0, i < _history.Items.Count - 1 ? 1 : 0),
            };
            HistoryList.Children.Add(row);
        }
        HistoryEmpty.Visibility = _history.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 모니터 배치도: 모니터를 누르면 그 모니터를, 선택한 모니터 위의 점을 누르면 위치를 고른다.
    /// 고른 모니터의 번호(1부터)를 돌려준다.
    /// </summary>
    private int RenderMap(Canvas canvas, string? monitorName, HotzonePosition current, IEnumerable<HotzonePosition> choices,
        Func<string, AppSettings> pickMonitor, Func<HotzonePosition, AppSettings> pickPosition, Action<Canvas, Rect, HotzonePosition> drawPreview)
    {
        canvas.Children.Clear();
        var screens = Forms.Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
        var selected = Edge.Screen(monitorName);

        // 전체 배치를 캔버스에 맞춰 축소
        var union = screens.Select(s => s.Bounds).Aggregate(System.Drawing.Rectangle.Union);
        double width = canvas.Width, height = canvas.Height; // 숨은 페이지에서도 그려지도록 고정 크기
        var scale = Math.Min(width / union.Width, height / union.Height);
        var ox = (width - union.Width * scale) / 2;
        var oy = (height - union.Height * scale) / 2;

        Rect selectedRect = default;
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var b = screen.Bounds;
            var rect = new Rect(ox + (b.X - union.X) * scale + Inset, oy + (b.Y - union.Y) * scale + Inset,
                b.Width * scale - 2 * Inset, b.Height * scale - 2 * Inset);
            var isSelected = screen.DeviceName == selected.DeviceName;
            if (isSelected) selectedRect = rect;

            var caption = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            caption.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 28, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            caption.Children.Add(new TextBlock
            {
                Text = $"{b.Width}×{b.Height}" + (screen.Primary ? " · 주 모니터" : ""),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            var monitor = new Border
            {
                Width = rect.Width, Height = rect.Height,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(isSelected ? Color.FromArgb(0x2E, Accent.R, Accent.G, Accent.B) : Color.FromRgb(0x33, 0x33, 0x39)),
                BorderBrush = isSelected ? AccentBrush : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Cursor = Cursors.Hand,
                Child = caption,
                ToolTip = isSelected ? null : "이 모니터 선택",
            };
            var device = screen.DeviceName;
            monitor.MouseLeftButtonUp += (_, _) => { if (device != selected.DeviceName) Update(pickMonitor(device)); };
            Canvas.SetLeft(monitor, rect.X);
            Canvas.SetTop(monitor, rect.Y);
            canvas.Children.Add(monitor);
        }

        drawPreview(canvas, selectedRect, current);
        foreach (var position in choices)
            AddDot(canvas, selectedRect, position, position == current, pickPosition);

        return Array.FindIndex(screens, s => s.DeviceName == selected.DeviceName) + 1;
    }

    private void RenderModes()
    {
        ModeCards.Children.Clear();
        ModeCards.Children.Add(ModeCard(OpenMode.Cursor, "커서 위치에 바로", "스페이스를 누르는 순간 커서 주변에 원형 메뉴가 펼쳐져요. 한 번 더 누르면 닫혀요.", new Thickness(0, 0, 6, 0)));
        ModeCards.Children.Add(ModeCard(OpenMode.Hotzone, "핫존으로 가져가서", "스페이스를 누른 뒤 정해 둔 가장자리나 구석으로 가져가면 열려요.", new Thickness(6, 0, 0, 0)));
    }

    private void RenderThemes()
    {
        ThemeCards.Children.Clear();
        for (var i = 0; i < PanelTheme.All.Count; i++)
        {
            var kind = PanelTheme.All[i];
            var margin = new Thickness(i == 0 ? 0 : 5, 0, i == PanelTheme.All.Count - 1 ? 0 : 5, 0);
            ThemeCards.Children.Add(ThemeCard(kind, margin));
        }
    }

    /// <summary>테마 카드: 배경화면 위에 실제 패널 그림을 작게 올린 미리보기.</summary>
    private Border ThemeCard(PanelThemeKind kind, Thickness margin)
    {
        var theme = PanelTheme.Get(kind);
        var current = _settings.Theme == kind;
        const double w = 232, h = 150, cx = w / 2, cy = h / 2, outer = 66, ring = 41, bubble = 36;

        var preview = new Canvas
        {
            Width = w, Height = h,
            ClipToBounds = true,
            // 유리 질감이 보이도록 알록달록한 배경화면 느낌
            Background = new LinearGradientBrush(
                [new GradientStop(Color.FromRgb(0x5B, 0x8C, 0xFF), 0), new GradientStop(Color.FromRgb(0xB0, 0x6A, 0xF5), 0.55), new GradientStop(Color.FromRgb(0xFF, 0x9E, 0x6B), 1)],
                new Point(0, 0), new Point(1, 1)),
        };
        void Put(FrameworkElement e, double x, double y)
        {
            Canvas.SetLeft(e, x - e.Width / 2);
            Canvas.SetTop(e, y - e.Height / 2);
            preview.Children.Add(e);
        }
        Put(PanelArt.Disc(theme, outer), cx, cy);
        Put(PanelArt.Hub(theme, 8), cx, cy);
        PanelController.Tile[] tiles = [new("jpeg", "JPG"), new("png", "PNG"), new("pdf", "PDF"), new(PanelController.ZipTile, "ZIP")];
        for (var t = 0; t < tiles.Length; t++)
        {
            var parts = PanelArt.Bubble(theme, tiles[t], bubble);
            if (t == 0) parts.Highlight(theme, true); // 강조색도 보이게
            var angle = -Math.PI / 2 + 2 * Math.PI * t / tiles.Length;
            Put(parts.Root, cx + Math.Cos(angle) * ring, cy + Math.Sin(angle) * ring);
        }

        var content = new StackPanel();
        content.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = preview, Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10) });
        content.Children.Add(new TextBlock { Text = theme.Name, Margin = new Thickness(2, 10, 0, 0), FontSize = 14, FontWeight = FontWeights.SemiBold });

        var card = new Border
        {
            Margin = margin,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(current ? Color.FromArgb(0x2E, Accent.R, Accent.G, Accent.B) : Color.FromRgb(0x26, 0x26, 0x2B)),
            BorderBrush = current ? AccentBrush : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(current ? 2 : 1),
            Cursor = Cursors.Hand,
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        card.MouseLeftButtonUp += (_, _) => { if (!current) Update(_settings with { Theme = kind }); };
        return card;
    }

    private Border ModeCard(OpenMode mode, string title, string description, Thickness margin)
    {
        var current = _settings.Mode == mode;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
        });
        var card = new Border
        {
            Margin = margin,
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(current ? Color.FromArgb(0x2E, Accent.R, Accent.G, Accent.B) : Color.FromRgb(0x26, 0x26, 0x2B)),
            BorderBrush = current ? AccentBrush : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(current ? 2 : 1),
            Cursor = Cursors.Hand,
            Child = content,
        };
        card.MouseLeftButtonUp += (_, _) => { if (!current) Update(_settings with { Mode = mode }); };
        return card;
    }

    /// <summary>점의 위치: 기준점에서 안쪽으로 조금 들인 곳.</summary>
    private static Point DotCenter(Rect r, HotzonePosition position)
    {
        var (ix, iy) = Edge.Inward(position);
        var x = ix < 0 ? r.Right : ix > 0 ? r.Left : r.Left + r.Width / 2;
        var y = iy < 0 ? r.Bottom : iy > 0 ? r.Top : r.Top + r.Height / 2;
        return new Point(x + ix * DotInset, y + iy * DotInset);
    }

    private void AddDot(Canvas canvas, Rect r, HotzonePosition position, bool current, Func<HotzonePosition, AppSettings> pick)
    {
        var dot = new Ellipse
        {
            Width = DotSize, Height = DotSize,
            Fill = current ? AccentBrush : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Stroke = current ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = current ? 2 : 1.5,
            Effect = current ? new DropShadowEffect { Color = Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 } : null,
            Cursor = Cursors.Hand,
            ToolTip = PositionName(position),
        };
        dot.MouseEnter += (_, _) => { if (!current) dot.Fill = new SolidColorBrush(Color.FromArgb(0x99, Accent.R, Accent.G, Accent.B)); };
        dot.MouseLeave += (_, _) => { if (!current) dot.Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); };
        dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!current) Update(pick(position)); };
        var c = DotCenter(r, position);
        Canvas.SetLeft(dot, c.X - DotSize / 2);
        Canvas.SetTop(dot, c.Y - DotSize / 2);
        canvas.Children.Add(dot);
    }

    /// <summary>알림 카드 미리보기: 고른 구석에 작은 카드 모양.</summary>
    private static void AddToastPreview(Canvas canvas, Rect r, HotzonePosition position)
    {
        var (ix, iy) = Edge.Inward(position);
        double w = Math.Min(70, r.Width * 0.32), h = Math.Max(12, w * 0.3);
        var x = ix < 0 ? r.Right - w - 6 : r.Left + 6;
        var y = iy < 0 ? r.Bottom - h - 6 : r.Top + 6;
        var card = new Rectangle { Width = w, Height = h, RadiusX = 4, RadiusY = 4, Fill = AccentBrush, Opacity = 0.85, IsHitTestVisible = false };
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
        canvas.Children.Add(card);
    }

    /// <summary>선택한 위치의 핫존 모양 미리보기: 가장자리는 40% 띠, 구석은 작은 사각형.</summary>
    private static void AddHotzonePreview(Canvas canvas, Rect r, HotzonePosition position)
    {
        var (ix, iy) = Edge.Inward(position);
        const double t = 4;
        double w = Edge.IsCorner(position) ? 10 : ix != 0 ? t : r.Width * 0.4;
        double h = Edge.IsCorner(position) ? 10 : iy != 0 ? t : r.Height * 0.4;
        var x = ix < 0 ? r.Right - w : ix > 0 ? r.Left : r.Left + (r.Width - w) / 2;
        var y = iy < 0 ? r.Bottom - h : iy > 0 ? r.Top : r.Top + (r.Height - h) / 2;
        var bar = new Rectangle { Width = w, Height = h, RadiusX = 2, RadiusY = 2, Fill = AccentBrush, IsHitTestVisible = false };
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);
        canvas.Children.Add(bar);
    }

    /// <summary>바깥쪽 바로 옆이 다른 모니터면 커서가 멈추지 않고 넘어간다.</summary>
    private static bool TouchesOtherMonitor(Forms.Screen screen, HotzonePosition position)
    {
        var b = screen.Bounds;
        var a = Edge.Anchor(b, position);
        var (ix, iy) = Edge.Inward(position);
        // Right/Bottom 경계는 배타적이라 기준점 자체가 이미 바깥 픽셀이다
        int outX = ix < 0 ? a.X : a.X - 1, outY = iy < 0 ? a.Y : a.Y - 1;
        int inX = Math.Clamp(a.X, b.Left, b.Right - 1), inY = Math.Clamp(a.Y, b.Top, b.Bottom - 1);
        var probes = new List<System.Drawing.Point>();
        if (ix != 0) probes.Add(new(outX, inY));
        if (iy != 0) probes.Add(new(inX, outY));
        return Forms.Screen.AllScreens.Any(s => s.DeviceName != screen.DeviceName && probes.Any(s.Bounds.Contains));
    }

    private static string PositionName(HotzonePosition position) => position switch
    {
        HotzonePosition.Top => "위쪽 가장자리",
        HotzonePosition.Bottom => "아래쪽 가장자리",
        HotzonePosition.Left => "왼쪽 가장자리",
        HotzonePosition.Right => "오른쪽 가장자리",
        HotzonePosition.TopLeft => "왼쪽 위 구석",
        HotzonePosition.TopRight => "오른쪽 위 구석",
        HotzonePosition.BottomLeft => "왼쪽 아래 구석",
        _ => "오른쪽 아래 구석",
    };
}
