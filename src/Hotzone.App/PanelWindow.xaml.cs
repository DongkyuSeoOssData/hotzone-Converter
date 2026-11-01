using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hotzone.Core;
using Hotzone.Interop;
using Forms = System.Windows.Forms;

namespace Hotzone.App;

/// <summary>
/// 원형으로 펼쳐지는 형식 패널. 원판을 피자 칸으로 나누고 칸 전체가 드롭 타깃이다. 가운데 파란 원에 파일 요약을 보여준다.
/// 커서 모드: 커서 위치에 전체 원. 핫존 모드: 핫존 기준점에서 가장자리면 반원, 구석이면 1/4원.
/// 반원·1/4원에서 칸이 너무 좁아지면 바깥쪽 고리를 더한다.
/// </summary>
public partial class PanelWindow : Window
{
    private const double Bubble = 66, Gap = 10, MinRadius = 112, RingStep = 80, Pad = 18, Hub = 40, SectorGap = 6, HoverScale = 1.03, ShadowMargin = 24,
        WedgeLength = 230, WedgeTaper = 66, RowHeight = 38, RowInset = 8;

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
    private double _extent;
    private const string GhostTag = "ghost";
    private const int MorphMillis = 340;
    private PanelTheme _theme = PanelTheme.Get(PanelThemeKind.GlassLight);
    /// <summary>처음 펼친 버블들. 묶음에서 돌아올 때 쓴다.</summary>
    private IReadOnlyList<PanelController.Tile> _mainTiles = [];
    /// <summary>묶음 버블이 펼쳐진 상태인지.</summary>
    private bool _expanded;
    private readonly DispatcherTimer _expand = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private PanelController.Tile? _hoveredGroup;

    public AppSettings Settings { get; set; } = new();

    public event Action<string, IReadOnlyList<string>>? ConvertRequested;

    public PanelWindow(PanelController controller)
    {
        _controller = controller;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle, clickThrough: false);
        DpiChanged += (_, _) => { if (IsVisible) PlaceAtAnchor(moveToMonitorFirst: false); }; // DPI 변경이 늦게 오면 다시 정렬
        _collapse.Tick += (_, _) => Dismiss(log: false);
        _expand.Tick += (_, _) =>
        {
            _expand.Stop();
            if (IsVisible && _hoveredGroup is { Children: { } children }) ShowTiles(children, expanded: true);
        };
        // 묶음을 펼친 뒤 커서를 가운데로 가져가면 원래 버블로 돌아간다
        AddHandler(DragOverEvent, new DragEventHandler((_, _) => { if (_expanded && CursorOnHub()) ShowTiles(_mainTiles, expanded: false); }), handledEventsToo: true);
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
        ResetExpansion(tiles);
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
        ResetExpansion([]);
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
        ResetExpansion([]);
        if (log) Log.Info("패널 닫힘");
    }

    private void ResetExpansion(IReadOnlyList<PanelController.Tile> mainTiles)
    {
        _expand.Stop();
        _hoveredGroup = null;
        _expanded = false;
        _mainTiles = mainTiles;
    }

    /// <summary>드래그 중에 묶음을 펼치거나(조각 + 목록) 원래 원판으로 돌아간다.</summary>
    private void ShowTiles(IReadOnlyList<PanelController.Tile> tiles, bool expanded)
    {
        _expand.Stop();
        _hoveredGroup = null;
        _expanded = expanded;
        var old = Root.Children.OfType<UIElement>().Where(e => !ReferenceEquals((e as FrameworkElement)?.Tag, GhostTag)).ToList();
        if (expanded) BuildWedge(tiles);
        else Build(tiles, _files);
        // 창은 처음부터 조각이 들어갈 크기라 전환 중에 움직이지 않는다
        Morph(old);
    }

    /// <summary>이전 그림은 파란 점 쪽으로 접히며 사라지고, 새 그림은 파란 점에서 천천히 자라난다.</summary>
    private void Morph(IReadOnlyList<UIElement> old)
    {
        var duration = TimeSpan.FromMilliseconds(MorphMillis);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var shrink = new ScaleTransform(1, 1, _anchor.X, _anchor.Y);
        var ghost = new Canvas
        {
            Tag = GhostTag,
            Width = Width, Height = Height,
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            RenderTransform = shrink,
        };
        foreach (var element in old) ghost.Children.Add(element);
        Root.Children.Insert(0, ghost);
        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => Root.Children.Remove(ghost);
        ghost.BeginAnimation(OpacityProperty, fade);
        var fold = new DoubleAnimation(0.7, duration) { EasingFunction = ease };
        shrink.BeginAnimation(ScaleTransform.ScaleXProperty, fold);
        shrink.BeginAnimation(ScaleTransform.ScaleYProperty, fold);

        var index = 0;
        foreach (var child in Root.Children.OfType<FrameworkElement>())
        {
            if (ReferenceEquals(child, ghost)) continue;
            if (child.RenderTransform is ScaleTransform shape)
            {
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
                var grow = new DoubleAnimation(0.65, 1, duration) { EasingFunction = ease };
                shape.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                shape.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            else if (child.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform move] })
            {
                var center = new Point(Canvas.GetLeft(child) + child.Width / 2, Canvas.GetTop(child) + child.Height / 2);
                var delay = TimeSpan.FromMilliseconds(60 + index++ * 25);
                // BeginTime 동안 최종 모습이 먼저 보이지 않게 시작값을 깔아 둔다
                child.Opacity = 0;
                move.X = (_anchor.X - center.X) * 0.35;
                move.Y = (_anchor.Y - center.Y) * 0.35;
                scale.ScaleX = scale.ScaleY = 0.8;
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { BeginTime = delay, EasingFunction = ease });
                var settle = new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = ease };
                move.BeginAnimation(TranslateTransform.XProperty, settle);
                move.BeginAnimation(TranslateTransform.YProperty, settle);
                var pop = new DoubleAnimation(1, duration) { BeginTime = delay, EasingFunction = ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }
    }

    /// <summary>원판 대신 파란 점을 꼭짓점으로 뻗어 나온 조각 하나를 그리고, 그 안에 세로 목록을 넣는다.</summary>
    private void BuildWedge(IReadOnlyList<PanelController.Tile> tiles)
    {
        var (ix, iy) = _inward;
        // 가장자리에서는 화면 안쪽으로, 위아래·커서 모드에서는 커서가 있는 쪽으로 펼친다
        var side = ix != 0 ? ix : WindowStyles.CursorPosition().X < _discCenter.X ? -1 : 1;
        var bodyHeight = WedgeHeight(tiles.Count);

        Root.Children.Clear();
        Root.Width = Width;
        Root.Height = Height;

        // 위아래 가장자리·구석은 화면 안쪽으로만 내려온다
        var top = iy > 0 ? _anchor.Y : iy < 0 ? _anchor.Y - bodyHeight : _anchor.Y - bodyHeight / 2;
        var near = _anchor.X + side * WedgeTaper;
        var far = _anchor.X + side * WedgeLength;
        var wedge = new Polygon
        {
            Width = Width, Height = Height,
            Points = [_anchor, new(near, top), new(far, top), new(far, top + bodyHeight), new(near, top + bodyHeight)],
            Fill = _theme.Disc, Stroke = _theme.DiscRim, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 6, Direction = 270, Opacity = _theme.ShadowOpacity },
            CacheMode = new BitmapCache(), // 그림자를 매 프레임 다시 계산하지 않게
            RenderTransformOrigin = new Point(_anchor.X / Width, _anchor.Y / Height),
            RenderTransform = new ScaleTransform(1, 1),
        };
        Root.Children.Add(wedge);

        var rowWidth = WedgeLength - WedgeTaper - 2 * RowInset;
        var rowLeft = Math.Min(near, far) + RowInset;
        for (var i = 0; i < tiles.Count; i++)
        {
            var row = CreateRow(tiles[i], rowWidth);
            Canvas.SetLeft(row, rowLeft);
            Canvas.SetTop(row, top + RowInset + i * RowHeight);
            Root.Children.Add(row);
        }
        AddHub("뒤로", ix == 0 && iy == 0 ? _anchor : new Point(_anchor.X + ix * Hub * 0.55, _anchor.Y + iy * Hub * 0.55));
    }

    /// <summary>미리보기 렌더링용: 왼쪽으로 펼친 조각 그림만 꺼낸다.</summary>
    internal FrameworkElement PreviewWedge()
    {
        _theme = PanelTheme.Get(Settings.Theme);
        _inward = (0, 0);
        Resize(163, Extent(163, 5));
        _discCenter = new(int.MaxValue, 0);
        BuildWedge(_controller.TilesFor(["a.png"]).First(t => t.Children is not null).Children!);
        Content = null;
        return Root;
    }

    /// <summary>조각 안의 목록 한 줄: 아이콘 + 이름. 줄 전체가 드롭 대상이다.</summary>
    private Border CreateRow(PanelController.Tile tile, double width)
    {
        var icon = new TextBlock { Text = PanelArt.Glyph(tile.Target), FontFamily = PanelArt.IconFont, FontSize = 15, Foreground = _theme.Glyph, Width = 24, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = tile.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _theme.Label, VerticalAlignment = VerticalAlignment.Center };
        var row = new Border
        {
            Width = width, Height = RowHeight - 2,
            Padding = new Thickness(10, 0, 8, 0),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent, // 투명해도 칠해 둬야 줄 전체가 드래그를 받는다
            AllowDrop = true,
            Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false, Children = { icon, label } },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
        };
        AttachDrop(row, tile, on =>
        {
            row.Background = on ? _theme.Hover : Brushes.Transparent;
            icon.Foreground = on ? _theme.HoverText : _theme.Glyph;
            label.Foreground = on ? _theme.HoverText : _theme.Label;
        });
        return row;
    }

    private bool CursorOnHub()
    {
        var (x, y) = WindowStyles.CursorPosition();
        var reach = (Hub + SectorGap) * VisualTreeHelper.GetDpi(this).DpiScaleX;
        return Math.Pow(x - _discCenter.X, 2) + Math.Pow(y - _discCenter.Y, 2) < reach * reach;
    }

    private void FillFromDrag(DragEventArgs e)
    {
        if (_files is not null || !IsVisible) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        var tiles = _controller.TilesFor(paths);
        _files = paths;
        ResetExpansion(tiles);
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

    /// <summary>
    /// 기준점이 중심인 지름 2R 원에서 화면 안쪽 절반(구석이면 1/4, 커서 모드면 전체)만 창으로 쓴다.
    /// 화면 안쪽 방향으로는 그림자가 잘리지 않게 여백을 더 둔다.
    /// </summary>
    /// <param name="outer">원판 반지름.</param>
    /// <param name="extent">창이 품을 반지름. 펼쳐질 조각까지 미리 잡아 두어 전환 중에 창이 움직이지 않게 한다.</param>
    private void Resize(double outer, double extent)
    {
        var (ix, iy) = _inward;
        _outer = outer;
        _extent = extent;
        Width = ix == 0 ? 2 * (extent + ShadowMargin) : extent + ShadowMargin;
        Height = iy == 0 ? 2 * (extent + ShadowMargin) : extent + ShadowMargin;
        _anchor = new Point(ix > 0 ? 0 : extent + ShadowMargin, iy > 0 ? 0 : extent + ShadowMargin);
    }

    private static double WedgeHeight(int rows) => rows * RowHeight + 2 * RowInset;

    private static double Extent(double outer, int? wedgeRows) =>
        wedgeRows is { } rows ? Math.Max(outer, Math.Max(WedgeLength, WedgeHeight(rows)) + Pad) : outer;

    private void Build(IReadOnlyList<PanelController.Tile> tiles, IReadOnlyList<string>? files)
    {
        var (ix, iy) = _inward;
        var full = ix == 0 && iy == 0;
        var span = full ? 2 * Math.PI : ix != 0 && iy != 0 ? Math.PI / 2 : Math.PI;
        var facing = full ? -Math.PI / 2 : Math.Atan2(iy, ix);

        // 안쪽 고리부터 들어가는 만큼 채우고 남으면 바깥 고리로
        // 전체 원은 고리 하나로 다 담고, 칸이 좁아지면 원을 키운다
        var rings = new List<(double Radius, int Count)>();
        if (full && tiles.Count > 0) rings.Add((Math.Max(MinRadius, tiles.Count * (Bubble + Gap) / span), tiles.Count));
        for (double r = MinRadius, placed = full ? tiles.Count : 0; placed < tiles.Count; r += RingStep)
        {
            var capacity = Math.Max(1, (int)Math.Floor(r * span / (Bubble + Gap)));
            var take = (int)Math.Min(capacity, tiles.Count - placed);
            rings.Add((r, take));
            placed += take;
        }
        var outer = rings.Count == 0 ? Hub + 34 : rings[^1].Radius + Bubble / 2 + Pad;
        _theme = PanelTheme.Get(Settings.Theme);
        Resize(outer, Extent(outer, tiles.FirstOrDefault(t => t.Children is not null)?.Children?.Count));

        Root.Children.Clear();
        Root.Width = Width;
        Root.Height = Height;

        var disc = PanelArt.Disc(_theme, outer);
        Place(disc, _anchor);
        Root.Children.Add(disc);
        // 칸은 가운데 원 바깥부터 원판 끝까지 (고리가 여럿이면 고리 사이가 경계)
        var index = 0;
        for (var ring = 0; ring < rings.Count; ring++)
        {
            var (radius, count) = rings[ring];
            var inner = ring == 0 ? Hub + SectorGap : radius - RingStep / 2;
            var ringOuter = ring == rings.Count - 1 ? outer : radius + RingStep / 2;
            var first = full ? facing - Math.PI / count : facing - span / 2; // 전체 원은 첫 칸이 위쪽 가운데에 오게
            for (var i = 0; i < count; i++, index++)
            {
                var sector = CreateSector(tiles[index], outer, inner, ringOuter, first + span * i / count, first + span * (i + 1) / count);
                Root.Children.Add(sector);
                Place(sector, _anchor);
            }
        }
        AddHub(files is null ? "파일 확인 중" : Summary(files), full ? _anchor : Along(facing, Hub * 0.55));
    }

    /// <summary>가운데 파란 원과 그 안의 글자.</summary>
    private void AddHub(string text, Point textAt)
    {
        var hub = PanelArt.Hub(_theme, Hub);
        Place(hub, _anchor);
        Root.Children.Add(hub);
        var label = new TextBlock
        {
            Text = text,
            Foreground = _theme.HoverText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = 2 * Hub - 8,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        Root.Children.Add(label);
        Place(label, textAt);
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

    private Grid CreateSector(PanelController.Tile tile, double outer, double inner, double ringOuter, double start, double end)
    {
        var parts = PanelArt.Sector(_theme, tile, outer, inner, ringOuter, start, end);
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

        AttachDrop(bubble, tile, Hover);
        return bubble;
    }

    /// <summary>버블·목록 줄 공통: 드래그가 올라오면 강조하고, 놓으면 변환을 요청한다.</summary>
    private void AttachDrop(UIElement target, PanelController.Tile tile, Action<bool> hover)
    {
        // 묶음 버블은 놓는 곳이 아니라 머물면 펼쳐지는 곳이다
        var isGroup = tile.Children is not null;
        var effect = isGroup ? DragDropEffects.None : DragDropEffects.Copy; // Copy: 원본 이동 방지

        target.DragEnter += (_, e) =>
        {
            hover(true);
            if (isGroup)
            {
                _hoveredGroup = tile;
                _expand.Start();
            }
            e.Effects = effect;
            e.Handled = true;
        };
        target.DragOver += (_, e) => { e.Effects = effect; e.Handled = true; };
        target.DragLeave += (_, _) =>
        {
            hover(false);
            if (isGroup)
            {
                _expand.Stop();
                _hoveredGroup = null;
            }
        };
        target.Drop += (_, e) =>
        {
            e.Effects = effect;
            e.Handled = true;
            hover(false);
            if (isGroup) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            {
                Log.Info($"드롭: {tile.Target} ← {string.Join(", ", paths)}");
                Dismiss(log: false);
                ConvertRequested?.Invoke(tile.Target, paths);
            }
        };
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
        var outer = (_expanded ? _extent : _outer) * VisualTreeHelper.GetDpi(this).DpiScaleX;
        return Math.Pow(x - _discCenter.X, 2) + Math.Pow(y - _discCenter.Y, 2) < outer * outer;
    }
}
