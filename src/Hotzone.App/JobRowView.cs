using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Hotzone.App;

/// <summary>
/// 작업 한 줄: 아이콘 · 제목 · 파일 이름 · (진행 막대와 % | 열기 버튼 | 오류와 로그 보기).
/// 알림 창과 환경설정의 작업 기록이 같이 쓴다. 작업이 바뀌면 스스로 다시 그린다.
/// </summary>
public sealed class JobRowView : Border
{
    private const double TrackWidth = 170;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Brush ErrorBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xFF, 0x3B, 0x30), 90));
    private static readonly Brush DoneBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x4C, 0xD9, 0x64), Color.FromRgb(0x2F, 0xB8, 0x4A), 90));

    private readonly PanelTheme _theme;
    private readonly bool _showTime;
    private readonly Ellipse _iconCircle = new();
    private readonly TextBlock _icon = new() { FontFamily = IconFont, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _time = new() { FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _detail = new() { FontSize = 12, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid _progressRow = new() { Margin = new Thickness(0, 8, 0, 2) };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TranslateTransform _fillMove = new();
    private readonly TextBlock _percent = new() { FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 32 };
    private readonly WrapPanel _buttons = new() { Margin = new Thickness(0, 8, 0, 0) };
    private bool _flowing;

    public JobRecord Job { get; }

    /// <summary>열기 버튼을 누른 뒤 (알림 창은 이때 닫는다).</summary>
    public event Action? Opened;

    public JobRowView(JobRecord job, PanelTheme theme, bool showTime)
    {
        Job = job;
        _theme = theme;
        _showTime = showTime;
        Padding = new Thickness(4, 10, 4, 10);

        _title.Foreground = theme.Label;
        _detail.Foreground = theme.Caption;
        _time.Foreground = theme.Caption;
        _percent.Foreground = theme.Caption;
        _fill.Background = theme.Hover;
        _fill.RenderTransform = _fillMove;

        var icon = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top, Children = { _iconCircle, _icon } };

        var track = new Border
        {
            Width = TrackWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = theme.Guide,
            ClipToBounds = true, Child = _fill, VerticalAlignment = VerticalAlignment.Center,
        };
        _progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_percent, 1);
        _progressRow.Children.Add(track);
        _progressRow.Children.Add(_percent);

        var titleRow = new DockPanel();
        DockPanel.SetDock(_time, Dock.Right);
        titleRow.Children.Add(_time);
        titleRow.Children.Add(_title);

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { titleRow, _detail, _progressRow, _buttons } };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        layout.Children.Add(icon);
        layout.Children.Add(text);
        Child = layout;

        Render(job);
        // 화면에 붙어 있는 동안만 작업 변화를 듣는다
        Loaded += (_, _) => { Job.Changed += Render; Render(Job); };
        Unloaded += (_, _) => Job.Changed -= Render;
    }

    private void Render(JobRecord job)
    {
        _title.Text = job.Title;
        _time.Text = _showTime ? Ago(job.Started) : "";
        _time.Visibility = _showTime ? Visibility.Visible : Visibility.Collapsed;
        _detail.Text = job.Status == JobStatus.Done && job.Message is null && job.Results.Count > 0
            ? (job.Results.Count == 1 ? System.IO.Path.GetFileName(job.Results[0].TrimEnd('\\')) : $"결과 {job.Results.Count}개")
            : job.Message ?? job.Detail;
        _detail.Foreground = job.Message is not null ? ErrorBrush : _theme.Caption;
        _detail.TextWrapping = job.Status == JobStatus.Failed ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _detail.MaxHeight = 34;

        switch (job.Status)
        {
            case JobStatus.Running:
                _iconCircle.Fill = _theme.Hub;
                _icon.Text = job.Glyph;
                _progressRow.Visibility = Visibility.Visible;
                _buttons.Visibility = Visibility.Collapsed;
                if (job.Progress is { } p) ShowProgress(p);
                else Flow();
                break;
            case JobStatus.Done:
                _iconCircle.Fill = DoneBrush;
                _icon.Text = ""; // CheckMark
                Finish();
                if (job.Results.Count > 0)
                {
                    var first = job.Results[0];
                    if (job.Results.Count == 1 && File.Exists(first)) AddButton("파일 열기", () => Shell(first));
                    AddButton("폴더 열기", () => RevealInExplorer(first));
                }
                break;
            default:
                _iconCircle.Fill = ErrorBrush;
                _icon.Text = ""; // Error
                Finish();
                AddButton("로그 보기", () => Shell(Log.FilePath));
                break;
        }
    }

    private void Flow()
    {
        if (_flowing) return;
        _flowing = true;
        _fill.Width = TrackWidth * 0.3;
        _percent.Text = "";
        _fillMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-TrackWidth * 0.3, TrackWidth, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void ShowProgress(double value)
    {
        if (_flowing)
        {
            _flowing = false;
            _fillMove.BeginAnimation(TranslateTransform.XProperty, null);
            _fillMove.X = 0;
        }
        _fill.Width = Math.Max(6, TrackWidth * value);
        _percent.Text = $"{value * 100:0}%";
    }

    private void Finish()
    {
        _flowing = false;
        _fillMove.BeginAnimation(TranslateTransform.XProperty, null);
        _progressRow.Visibility = Visibility.Collapsed;
        _buttons.Children.Clear();
        _buttons.Visibility = Visibility.Visible;
    }

    private void AddButton(string label, Action onClick)
    {
        var button = new Border
        {
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(13),
            Background = _theme.Bubble,
            BorderBrush = _theme.BubbleRim,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _theme.Glyph },
        };
        button.MouseEnter += (_, _) => button.Opacity = 0.75;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            try { onClick(); }
            catch (Exception ex) { Log.Error($"열기 실패: {label}", ex); }
            Opened?.Invoke();
        };
        _buttons.Children.Add(button);
    }

    private static string Ago(DateTime at)
    {
        var span = DateTime.Now - at;
        if (span.TotalMinutes < 1) return "방금";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}분 전";
        if (at.Date == DateTime.Today) return at.ToString("HH:mm");
        return at.ToString("M/d HH:mm");
    }

    private static void Shell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>파일이면 탐색기에서 그 파일을 선택한 채로, 폴더면 그 폴더를 연다.</summary>
    private static void RevealInExplorer(string path)
    {
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else Process.Start("explorer.exe", $"\"{path}\"");
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
