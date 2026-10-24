using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Hotzone.Interop;

namespace Hotzone.App;

/// <summary>
/// 진행 알림: 카드 하나에 작업들을 줄로 쌓는다 (최신이 위). 3줄이 넘으면 스크롤.
/// 설정한 모니터의 구석(작업 표시줄을 뺀 작업 영역)에 붙고, 패널 테마를 따른다.
/// 모든 작업이 끝나면 설정한 초 뒤 사라지고(0이면 닫기를 눌러야), 마우스를 올리고 있으면 기다린다.
/// Windows 토스트는 위치를 정할 수 없고 진행률을 실시간으로 바꾸기 어려워 직접 그린다.
/// </summary>
public sealed class ToastHost : Window
{
    private const double CardWidth = 360, ShadowPad = 22, RowsVisible = 3, RowHeight = 84;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly Border _card;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _summary = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _header = new() { Text = "변환 작업", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _close = new() { Text = "", FontFamily = IconFont, FontSize = 11, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "닫기" };
    private readonly DispatcherTimer _dismiss = new();
    private string? _placedMonitor;
    private PanelThemeKind? _themeKind;

    public AppSettings Settings { get; set; } = new();

    public ToastHost()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");

        var header = new DockPanel { Margin = new Thickness(4, 0, 2, 6) };
        DockPanel.SetDock(_close, Dock.Right);
        header.Children.Add(_close);
        header.Children.Add(_header);
        header.Children.Add(_summary);

        var scroller = new ScrollViewer
        {
            Content = _rows,
            MaxHeight = RowsVisible * RowHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
        };
        _card = new Border
        {
            Width = CardWidth,
            Margin = new Thickness(ShadowPad),
            Padding = new Thickness(14, 12, 10, 10),
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { header, scroller } },
            RenderTransform = new TranslateTransform(),
        };
        Content = _card;

        _close.MouseLeftButtonUp += (_, _) => Dismiss(animated: true);
        _dismiss.Tick += (_, _) =>
        {
            if (_card.IsMouseOver) return; // 보고 있는 동안은 기다린다
            Dismiss(animated: true);
        };
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle, clickThrough: false);
        // 줄이 늘거나 줄면 구석에 다시 맞춘다
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => { _placedMonitor = null; Place(); };
    }

    /// <summary>새 작업 줄을 맨 위에 넣고 창을 띄운다. 알림을 끈 설정이면 아무것도 하지 않는다.</summary>
    public void Add(JobRecord job)
    {
        if (!Settings.ShowToasts) return;
        ApplyTheme();
        var row = new JobRowView(job, PanelTheme.Get(Settings.Theme), showTime: false);
        row.Opened += () => Dismiss(animated: true);
        job.Changed += _ => Update();
        _rows.Children.Insert(0, row);
        SeparateRows();

        _dismiss.Stop();
        if (!IsVisible)
        {
            _placedMonitor = null;
            Show();
            SlideIn();
        }
        Update();
        Place();
    }

    /// <summary>위치·테마 설정이 바뀌면 열린 창도 바로 따른다.</summary>
    public void Reposition()
    {
        _placedMonitor = null;
        if (!IsVisible) return;
        if (_themeKind != Settings.Theme)
        {
            // 줄마다 테마 색을 쥐고 있으므로 다시 만든다
            var jobs = _rows.Children.OfType<JobRowView>().Select(r => r.Job).Reverse().ToList();
            _rows.Children.Clear();
            ApplyTheme();
            foreach (var job in jobs)
            {
                var row = new JobRowView(job, PanelTheme.Get(Settings.Theme), showTime: false);
                row.Opened += () => Dismiss(animated: true);
                _rows.Children.Insert(0, row);
            }
            SeparateRows();
        }
        Place();
    }

    private void ApplyTheme()
    {
        if (_themeKind == Settings.Theme) return;
        _themeKind = Settings.Theme;
        var theme = PanelTheme.Get(Settings.Theme);
        _card.Background = theme.Disc;
        _card.BorderBrush = theme.DiscRim;
        _card.Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Direction = 270, Opacity = theme.ShadowOpacity * 0.8 };
        _header.Foreground = theme.Label;
        _summary.Foreground = theme.Caption;
        _close.Foreground = theme.Caption;
    }

    /// <summary>줄 사이 얇은 구분선 (맨 아래 줄은 없음).</summary>
    private void SeparateRows()
    {
        var line = PanelTheme.Get(Settings.Theme).Guide;
        for (var i = 0; i < _rows.Children.Count; i++)
        {
            var row = (JobRowView)_rows.Children[i];
            row.BorderBrush = line;
            row.BorderThickness = new Thickness(0, 0, 0, i < _rows.Children.Count - 1 ? 1 : 0);
        }
    }

    /// <summary>머리글 요약을 고치고, 모두 끝났으면 사라지기 타이머를 건다.</summary>
    private void Update()
    {
        var jobs = _rows.Children.OfType<JobRowView>().Select(r => r.Job).ToList();
        int running = jobs.Count(j => j.Status == JobStatus.Running), failed = jobs.Count(j => j.Status == JobStatus.Failed);
        var done = jobs.Count - running - failed;
        _summary.Text = string.Join(" · ", new[]
        {
            running > 0 ? $"진행 중 {running}" : null,
            done > 0 ? $"완료 {done}" : null,
            failed > 0 ? $"실패 {failed}" : null,
        }.Where(s => s is not null));

        if (running == 0 && jobs.Count > 0 && Settings.ToastDismissSeconds > 0)
        {
            _dismiss.Interval = TimeSpan.FromSeconds(Settings.ToastDismissSeconds);
            _dismiss.Stop();
            _dismiss.Start();
        }
        else
        {
            _dismiss.Stop();
        }
    }

    /// <summary>창을 닫고 끝난 줄은 비운다 (기록에는 남아 있다). 진행 중인 줄은 다음에 다시 열 때 그대로 보인다.</summary>
    private void Dismiss(bool animated)
    {
        _dismiss.Stop();
        void Finish()
        {
            foreach (var row in _rows.Children.OfType<JobRowView>().Where(r => r.Job.Status != JobStatus.Running).ToList())
                _rows.Children.Remove(row);
            SeparateRows();
            Hide();
            _card.BeginAnimation(OpacityProperty, null);
            _card.Opacity = 1;
        }
        if (!animated) { Finish(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Finish();
        _card.BeginAnimation(OpacityProperty, fade);
    }

    private void SlideIn()
    {
        var move = (TranslateTransform)_card.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(IsRight ? 40 : -40, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private bool IsBottom => Settings.ToastPosition is HotzonePosition.BottomLeft or HotzonePosition.BottomRight or HotzonePosition.Bottom;
    private bool IsRight => Settings.ToastPosition is HotzonePosition.TopRight or HotzonePosition.BottomRight or HotzonePosition.Right;

    private void Place()
    {
        if (!IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var screen = Edge.Screen(Settings.ToastMonitor);
        var area = screen.WorkingArea;
        // 다른 모니터로 처음 옮길 때만 중앙을 거쳐 그 DPI를 받는다 (매번 하면 줄이 늘 때 깜빡인다)
        if (_placedMonitor != screen.DeviceName)
        {
            WindowStyles.Move(hwnd, area.X + area.Width / 2, area.Y + area.Height / 2);
            _placedMonitor = screen.DeviceName;
        }
        var (l, t, r, b) = WindowStyles.Bounds(hwnd);
        int w = r - l, h = b - t;
        WindowStyles.Move(hwnd, IsRight ? area.Right - w : area.Left, IsBottom ? area.Bottom - h : area.Top);
    }

    /// <summary>미리보기 렌더링용: 창 없이 카드만 꺼낸다.</summary>
    internal FrameworkElement DetachCard()
    {
        _dismiss.Stop();
        Content = null;
        _card.BeginAnimation(OpacityProperty, null);
        _card.Opacity = 1;
        ((TranslateTransform)_card.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
        ((TranslateTransform)_card.RenderTransform).X = 0;
        return _card;
    }
}
