using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hotzone.Converters;

namespace Hotzone.App;

/// <summary>
/// 앱·트레이 아이콘: iOS 앱 아이콘 같은 둥근 사각형에 파란 그라데이션 + 직접 그린 흰 변환 화살표(⇄).
/// 16px 트레이에서도 선명하도록 크기마다 따로 그린다. exe 아이콘(app.ico)도 같은 그림으로 만든다.
/// </summary>
internal static class TrayIcon
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>환경설정 머리 등에 쓰는 큰 그림.</summary>
    public static ImageSource ImageSource { get; } = Render(256);

    /// <summary>트레이용 아이콘 (작은 크기를 따로 그린 ICO).</summary>
    public static System.Drawing.Icon Create()
    {
        var stream = new MemoryStream();
        WriteIco(stream);
        stream.Position = 0;
        return new System.Drawing.Icon(stream, 16, 16);
    }

    /// <summary>개발용: exe 아이콘 파일을 만든다 (<c>--make-icon 경로</c>).</summary>
    public static void SaveIco(string path)
    {
        using var file = File.Create(path);
        WriteIco(file);
    }

    private static void WriteIco(Stream stream) =>
        IcoConverter.Write(stream, Sizes.Select(s => (s, Png(Render(s)))).ToList());

    private static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, size, size);
            var radius = size * 0.24;
            dc.DrawRoundedRectangle(
                new LinearGradientBrush(Color.FromRgb(0x5A, 0xB2, 0xFF), Color.FromRgb(0x0A, 0x66, 0xF0), 90),
                null, rect, radius, radius);
            // 위쪽 유리 광택
            dc.DrawRoundedRectangle(
                new LinearGradientBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90),
                null, new Rect(0, 0, size, size * 0.55), radius, radius);
            // 변환 화살표 ⇄ 를 직접 그린다 (글꼴 글리프를 로고에 쓰지 않기 위해)
            var pen = new Pen(Brushes.White, Math.Max(1.6, size * 0.08)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            Point P(double x, double y) => new(x * size, y * size);
            dc.DrawGeometry(null, pen, Polyline(P(0.26, 0.39), P(0.72, 0.39)));
            dc.DrawGeometry(null, pen, Polyline(P(0.60, 0.27), P(0.72, 0.39), P(0.60, 0.51)));
            dc.DrawGeometry(null, pen, Polyline(P(0.74, 0.63), P(0.28, 0.63)));
            dc.DrawGeometry(null, pen, Polyline(P(0.40, 0.51), P(0.28, 0.63), P(0.40, 0.75)));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Geometry Polyline(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
            ctx.PolyLineTo(points[1..], isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
