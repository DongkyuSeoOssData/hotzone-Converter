using System.Drawing;
using Forms = System.Windows.Forms;

namespace Hotzone.App;

/// <summary>
/// 핫존·패널을 붙일 화면 위치 계산 (물리 픽셀).
/// 커서가 실제로 멈추는 곳이어야 하므로 작업 영역이 아니라 모니터 전체 경계를 쓴다.
/// 모니터마다 DPI가 다르면 WPF DIP 좌표가 어긋나므로 위치는 전부 물리 픽셀로 잡는다.
/// </summary>
internal static class Edge
{
    /// <summary>장치 이름(<c>\\.\DISPLAY1</c>)의 모니터. 없거나 연결이 끊겼으면 주 모니터.</summary>
    public static Forms.Screen Screen(string? device) =>
        Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device)
        ?? Forms.Screen.PrimaryScreen
        ?? Forms.Screen.AllScreens[0];

    public static bool IsCorner(HotzonePosition p) => p is HotzonePosition.TopLeft or HotzonePosition.TopRight
        or HotzonePosition.BottomLeft or HotzonePosition.BottomRight;

    /// <summary>핫존이 붙는 기준점: 가장자리 가운데 또는 구석.</summary>
    public static Point Anchor(Rectangle b, HotzonePosition p)
    {
        int cx = b.Left + b.Width / 2, cy = b.Top + b.Height / 2;
        return p switch
        {
            HotzonePosition.Top => new(cx, b.Top),
            HotzonePosition.Bottom => new(cx, b.Bottom),
            HotzonePosition.Left => new(b.Left, cy),
            HotzonePosition.Right => new(b.Right, cy),
            HotzonePosition.TopLeft => new(b.Left, b.Top),
            HotzonePosition.TopRight => new(b.Right, b.Top),
            HotzonePosition.BottomLeft => new(b.Left, b.Bottom),
            _ => new(b.Right, b.Bottom),
        };
    }

    /// <summary>화면 안쪽 방향 (각 성분 -1/0/1, y는 아래가 +).</summary>
    public static (int X, int Y) Inward(HotzonePosition p) => p switch
    {
        HotzonePosition.Top => (0, 1),
        HotzonePosition.Bottom => (0, -1),
        HotzonePosition.Left => (1, 0),
        HotzonePosition.Right => (-1, 0),
        HotzonePosition.TopLeft => (1, 1),
        HotzonePosition.TopRight => (-1, 1),
        HotzonePosition.BottomLeft => (1, -1),
        _ => (-1, -1),
    };

    /// <summary>기준점을 중심으로 놓되 모니터 밖으로 나가지 않게 민 창의 왼쪽 위.</summary>
    public static Point Clamp(Rectangle b, Point anchor, int width, int height) => new(
        Math.Clamp(anchor.X - width / 2, b.Left, b.Right - width),
        Math.Clamp(anchor.Y - height / 2, b.Top, b.Bottom - height));
}
