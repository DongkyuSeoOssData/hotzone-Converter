using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hotzone.Interop;

namespace Hotzone.App;

/// <summary>커서 옆 작은 주황 점 (설계 3.4). 최상단, 클릭 통과, 커서를 따라 이동.</summary>
public sealed class ArmIndicator : Window
{
    private const double Size = 12;
    private const double Offset = 18;
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public ArmIndicator()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Width = Height = Size;
        Content = new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x24)) };
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle, clickThrough: true);
        _follow.Tick += (_, _) => Follow();
    }

    /// <summary>첫 무장 때 창을 처음 만드느라 늦어지지 않게 투명하게 한 번 띄웠다 숨긴다.</summary>
    public void Prewarm()
    {
        Opacity = 0;
        Show();
        Hide();
        Opacity = 1;
    }

    public void SetArmed(bool armed)
    {
        if (armed)
        {
            Follow();
            Show();
            _follow.Start();
        }
        else
        {
            _follow.Stop();
            Hide();
        }
    }

    private void Follow()
    {
        // GetCursorPos는 물리 픽셀 → WPF DIP로 변환
        var (x, y) = WindowStyles.CursorPosition();
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var p = scale.Transform(new Point(x, y));
        Left = p.X + Offset;
        Top = p.Y + Offset;
    }
}
