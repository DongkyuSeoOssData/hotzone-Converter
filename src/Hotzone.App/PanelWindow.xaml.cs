using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hotzone.Core;
using Hotzone.Interop;
using Forms = System.Windows.Forms;

namespace Hotzone.App;

/// <summary>
/// 원형으로 펼쳐지는 형식 패널. 버블마다 드롭 타깃이고, 열릴 때 가운데에서 부채꼴로 튀어나온다.
/// 커서 모드: 커서 위치에 전체 원. 핫존 모드: 핫존 기준점에서 가장자리면 반원, 구석이면 1/4원.
/// 버블이 한 바퀴에 다 안 들어가면 바깥쪽 고리를 더한다.
/// </summary>
public partial class PanelWindow : Window
{
    private const double Bubble = 66, Gap = 10, MinRadius = 112, RingStep = 80, Pad = 18, Hub = 24, HoverScale = 1.12, ShadowMargin = 24;

    private readonly PanelController _controller;
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(300) };
    /// <summary>끌고 있는 파일. 커서 모드는 패널이 첫 DragEnter를 받을 때까지 null.</summary>
    private IReadOnlyList<string>? _files;
    /// <summary>화면 안쪽 방향. (0, 0)이면 커서 모드의 전체 원.</summary>
    private (int X, int Y) _inward;
    /// <summary>원 중심이 와야 할 화면 위치와 그 모니터 (물리 픽셀).</summary>
    private System.Drawing.Point _screenAnchor;
    private System.Drawing.Rectangle _bounds;
    /// <summary>실제로 놓인 원 중심 (커서 모드는 화면 밖으로 안 나가게 밀려날 수 있다).</summary>
    private System.Drawing.Point _discCenter;
    /// <summary>창 안에서의 원 중심 (DIP).</summary>
    private Point _anchor;
    private double _outer;
    private PanelTheme _theme = PanelTheme.Get(PanelThemeKind.GlassLight);

    public AppSettings Settings { get; set; } = new();

    public event Action<string, IReadOnlyList<string>>? ConvertRequested;

    public PanelWindow(PanelController controller)
    {
        _controller = controller;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle, clickThrough: false);
        DpiChanged += (_, _) => { if (IsVisible) PlaceAtAnchor(moveToMonitorFirst: false); }; // DPI 변경이 늦게 오면 다시 정렬
        _collapse.Tick += (_, _) => Dismiss(log: false);
        // 버블이 DragEnter/DragOver를 Handled로 끝내므로 handledEventsToo로 받는다.
        // 버블 사이를 옮길 때도 DragLeave가 창까지 올라오니, 커서가 실제로 원판 밖일 때만 접는다.
        AddHandler(DragEnterEvent, new DragEventHandler((_, e) => { _collapse.Stop(); FillFromDrag(e); }), handledEventsToo: true);
        AddHandler(DragOverEvent, new DragEventHandler((_, e) => { _collapse.Stop(); FillFromDrag(e); }), handledEventsToo: true);
        AddHandler(DragLeaveEvent, new DragEventHandler((_, _) => { if (!CursorInside()) ScheduleCollapse(); }), handledEventsToo: true);
        // 버블 밖(원판·가운데)에서는 놓아도 아무 일 없음을 커서로 보여준다
        DragOver += (_, e) => { e.Effects = DragDropEffects.None; e.Handled = true; };
    }

    /// <summary>
    /// 첫 무장이 드래그 도중 창 생성·JIT·첫 렌더링 비용을 떠안지 않게 앱 시작 때 한 번 그려 둔다.
    /// 투명도 0인 층 창은 히트 테스트도 안 되므로 사용자에게 보이거나 클릭을 막지 않는다.
    /// </summary>
    public void Prewarm()
    {
        _inward = (0, 0);
        _bounds = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).Bounds;
        _screenAnchor = new(_bounds.Left + _bounds.Width / 2, _bounds.Top + _bounds.Height / 2);
        Build([new PanelController.Tile("png", "PNG", "준비")], ["warmup.png"]);
        Opacity = 0;
        Show();
        PlaceAtAnchor(moveToMonitorFirst: true);
        Hide();
    }

    /// <summary>핫존 모드: 핫존 기준점에 파일 목록으로 바로 연다.</summary>
    public void OpenFor(IReadOnlyList<string> files)
    {
        _collapse.Stop();
        if (IsVisible && ReferenceEquals(files, _files)) return;

        var tiles = _controller.TilesFor(files);
        _files = files;
        _inward = Edge.Inward(Settings.Position);
        _bounds = Edge.Screen(Settings.Monitor).Bounds;
        _screenAnchor = Edge.Anchor(_bounds, Settings.Position);
        Build(tiles, files);
        ShowAnimated();
        Log.Info($"패널 열림(핫존): 버블 {tiles.Count}개, 파일 {files.Count}개");
    }

    /// <summary>커서 모드: 커서 위치에 가운데 원부터 띄우고, 패널이 드래그를 받는 순간 버블을 채운다.</summary>
    public void OpenAtCursor()
    {
        _collapse.Stop();
        var (x, y) = WindowStyles.CursorPosition();
        _files = null;
        _inward = (0, 0);
        _screenAnchor = new(x, y);
        _bounds = Forms.Screen.FromPoint(_screenAnchor).Bounds;
        Build([], null);
        ShowAnimated();
        Log.Info($"패널 열림(커서): ({x}, {y})");
    }

    public void ScheduleCollapse() => _collapse.Start();

    /// <summary>바로 닫는다 (커서 모드에서 스페이스를 한 번 더 눌렀을 때).</summary>
    public void Dismiss(bool log = true)
    {
        _collapse.Stop();
        Hide();
        _files = null;
        if (log) Log.Info("패널 닫힘");
    }

    private void FillFromDrag(DragEventArgs e)
    {
        if (_files is not null || !IsVisible) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        var tiles = _controller.TilesFor(paths);
        _files = paths;
        Build(tiles, paths);
        PlaceAtAnchor(moveToMonitorFirst: false);
        Animate(fadeIn: false);
        Log.Info($"버블 채움(커서): 버블 {tiles.Count}개, 파일 {paths.Length}개");
    }

    private void ShowAnimated()
    {
        // 지난번 열림 애니메이션이 투명도 1을 붙잡고 있으면 자리 잡기 전 한 프레임이 엉뚱한 곳에 보인다
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        PlaceAtAnchor(moveToMonitorFirst: true);
        Animate(fadeIn: true);
    }

    private void Build(IReadOnlyList<PanelController.Tile> tiles, IReadOnlyList<string>? files)
    {
        var (ix, iy) = _inward;
        var full = ix == 0 && iy == 0;
        var span = full ? 2 * Math.PI : ix != 0 && iy != 0 ? Math.PI / 2 : Math.PI;
        var facing = full ? -Math.PI / 2 : Math.Atan2(iy, ix);

        // 안쪽 고리부터 들어가는 만큼 채우고 남으면 바깥 고리로
        var rings = new List<(double Radius, int Count)>();
        for (double r = MinRadius, placed = 0; placed < tiles.Count; r += RingStep)
        {
            var capacity = Math.Max(1, (int)Math.Floor(r * span / (Bubble + Gap)));
            var take = (int)Math.Min(capacity, tiles.Count - placed);
            rings.Add((r, take));
            placed += take;
        }
        var outer = _outer = rings.Count == 0 ? Hub + 34 : rings[^1].Radius + Bubble / 2 + Pad;
        _theme = PanelTheme.Get(Settings.Theme);

        // 기준점이 중심인 지름 2R 원에서 화면 안쪽 절반(구석이면 1/4, 커서 모드면 전체)만 창으로 쓴다.
        // 화면 안쪽 방향으로는 그림자가 잘리지 않게 여백을 더 둔다.
        Width = ix == 0 ? 2 * (outer + ShadowMargin) : outer + ShadowMargin;
        Height = iy == 0 ? 2 * (outer + ShadowMargin) : outer + ShadowMargin;
        _anchor = new Point(ix > 0 ? 0 : outer + ShadowMargin, iy > 0 ? 0 : outer + ShadowMargin);

        Root.Children.Clear();
        Root.Width = Width;
        Root.Height = Height;

        var disc = PanelArt.Disc(_theme, outer);
        Place(disc, _anchor);
        Root.Children.Add(disc);
        foreach (var (radius, _) in rings)
        {
            var guide = new Ellipse
            {
                Width = 2 * radius, Height = 2 * radius,
                Stroke = _theme.Guide, StrokeThickness = 1, StrokeDashArray = [2, 4],
                IsHitTestVisible = false,
            };
            Place(guide, _anchor);
            Root.Children.Add(guide);
        }
        var hub = PanelArt.Hub(_theme, Hub);
        Place(hub, _anchor);
        Root.Children.Add(hub);

        var summary = new TextBlock
        {
            Text = files is null ? "파일 확인 중" : Summary(files),
            Foreground = _theme.Caption,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            IsHitTestVisible = false,
        };
        Root.Children.Add(summary);
        // 커서 모드는 가운데 원 바로 아래, 핫존 모드는 화면 안쪽으로 조금 들어간 곳
        Place(summary, full ? new Point(_anchor.X, _anchor.Y + Hub + 14) : Along(facing, Hub + 36));

        var index = 0;
        foreach (var (radius, count) in rings)
        {
            for (var i = 0; i < count; i++, index++)
            {
                var angle = full ? facing + 2 * Math.PI * i / count : facing - span / 2 + span * (i + 0.5) / count;
                var bubble = CreateBubble(tiles[index]);
                Root.Children.Add(bubble);
                Place(bubble, Along(angle, radius));
            }
        }
    }

    private Point Along(double angle, double distance) =>
        new(_anchor.X + Math.Cos(angle) * distance, _anchor.Y + Math.Sin(angle) * distance);

    /// <summary>요소의 가운데가 <paramref name="center"/>에 오게 캔버스에 놓는다.</summary>
    private static void Place(FrameworkElement element, Point center)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = element.DesiredSize;
        Canvas.SetLeft(element, center.X - size.Width / 2);
        Canvas.SetTop(element, center.Y - size.Height / 2);
    }

    private static string Summary(IReadOnlyList<string> files)
    {
        if (files.Any(System.IO.Directory.Exists)) return files.Count == 1 ? "폴더" : $"항목 {files.Count}개";
        var formats = files.Take(5).Select(Formats.Detect).Distinct().ToList();
        var kind = formats.Count == 1 && formats[0] != "" ? Formats.FileExtension(formats[0]).ToUpperInvariant() : "파일";
        return files.Count == 1 ? kind : $"{kind} {files.Count}개";
    }

    private Grid CreateBubble(PanelController.Tile tile)
    {
        var parts = PanelArt.Bubble(_theme, tile, Bubble);
        var bubble = parts.Root;
        bubble.AllowDrop = true;
        bubble.Cursor = Cursors.Hand;

        void Hover(bool on)
        {
            parts.Highlight(_theme, on);
            var anim = new DoubleAnimation(on ? HoverScale : 1, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() };
            parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        bubble.DragEnter += (_, e) =>
        {
            Hover(true);
            e.Effects = DragDropEffects.Copy; // 원본 이동 방지
            e.Handled = true;
        };
        bubble.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        bubble.DragLeave += (_, _) => Hover(false);
        bubble.Drop += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            Hover(false);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            {
                Log.Info($"드롭: {tile.Target} ← {string.Join(", ", paths)}");
                Dismiss(log: false);
                ConvertRequested?.Invoke(tile.Target, paths);
            }
        };
        return bubble;
    }

    /// <summary>원판은 가운데에서 커지고, 버블은 가운데에서 차례로 튀어나온다.</summary>
    private void Animate(bool fadeIn)
    {
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var index = 0;
        foreach (var child in Root.Children.OfType<FrameworkElement>())
        {
            if (child.RenderTransform is ScaleTransform discScale)
            {
                var grow = new DoubleAnimation(fadeIn ? 0.82 : 0.94, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() };
                discScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                discScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            else if (child.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform move] })
            {
                var center = new Point(Canvas.GetLeft(child) + child.Width / 2, Canvas.GetTop(child) + child.Height / 2);
                var delay = TimeSpan.FromMilliseconds(index++ * 22);
                var duration = TimeSpan.FromMilliseconds(280);
                var ease = new BackEase { Amplitude = 0.35 };
                // BeginTime 동안 최종 위치가 먼저 보이지 않게 시작값을 깔아 둔다
                move.X = _anchor.X - center.X;
                move.Y = _anchor.Y - center.Y;
                scale.ScaleX = scale.ScaleY = 0.3;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = ease });
                var pop = new DoubleAnimation(1, duration) { BeginTime = delay, EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }
    }

    /// <summary>
    /// 창 안의 원 중심이 화면 기준점에 오도록 놓는다 (물리 픽셀).
    /// 커서 모드는 원이 화면 밖으로 잘리지 않게 모니터 안으로 민다.
    /// </summary>
    private void PlaceAtAnchor(bool moveToMonitorFirst)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _bounds;
        // 대상 모니터 중앙으로 먼저 옮겨 그 DPI로 크기가 다시 잡히게 한다 (이미 열린 채 다시 그릴 때는 건너뜀: 깜빡임 방지)
        if (moveToMonitorFirst) WindowStyles.Move(hwnd, b.X + b.Width / 2, b.Y + b.Height / 2);
        var dpi = VisualTreeHelper.GetDpi(this);
        int ax = (int)Math.Round(_anchor.X * dpi.DpiScaleX), ay = (int)Math.Round(_anchor.Y * dpi.DpiScaleY);
        int w = (int)Math.Round(Width * dpi.DpiScaleX), h = (int)Math.Round(Height * dpi.DpiScaleY);
        int left = _screenAnchor.X - ax, top = _screenAnchor.Y - ay;
        if (_inward == (0, 0))
        {
            left = Math.Clamp(left, b.Left, Math.Max(b.Left, b.Right - w));
            top = Math.Clamp(top, b.Top, Math.Max(b.Top, b.Bottom - h));
        }
        WindowStyles.Move(hwnd, left, top);
        _discCenter = new(left + ax, top + ay);
    }

    /// <summary>커서가 원판 안인지 (창 사각형의 투명한 모서리는 밖으로 친다).</summary>
    private bool CursorInside()
    {
        var (x, y) = WindowStyles.CursorPosition();
        var outer = _outer * VisualTreeHelper.GetDpi(this).DpiScaleX;
        return Math.Pow(x - _discCenter.X, 2) + Math.Pow(y - _discCenter.Y, 2) < outer * outer;
    }
}
