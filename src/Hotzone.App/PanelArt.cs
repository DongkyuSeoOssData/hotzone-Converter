using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Hotzone.Core;

namespace Hotzone.App;

/// <summary>원판·가운데 원·피자 칸 그리기. 패널과 환경설정의 테마 미리보기가 같은 그림을 쓴다.</summary>
public static class PanelArt
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Display, Segoe UI, Malgun Gothic");

    /// <summary>원판 + 위쪽 광택. 열림 애니메이션용 ScaleTransform이 붙어 있다.</summary>
    public static Grid Disc(PanelTheme theme, double outer)
    {
        var grid = new Grid
        {
            Width = 2 * outer, Height = 2 * outer,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            CacheMode = new BitmapCache(), // 그림자를 애니메이션 프레임마다 다시 계산하지 않게
        };
        grid.Children.Add(new Ellipse
        {
            Fill = theme.Disc, Stroke = theme.DiscRim, StrokeThickness = 1.2,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 6, Direction = 270, Opacity = theme.ShadowOpacity },
        });
        if (theme.Sheen is { } sheen)
            grid.Children.Add(new Ellipse { Fill = sheen, Margin = new Thickness(1.5), IsHitTestVisible = false });
        return grid;
    }

    public static Ellipse Hub(PanelTheme theme, double radius) => new()
    {
        Width = 2 * radius, Height = 2 * radius,
        Fill = theme.Hub,
        Stroke = theme.HubRim, StrokeThickness = theme.HubRim is null ? 0 : 3,
        IsHitTestVisible = false,
    };

    /// <summary>피자 칸 하나의 부품. 드래그가 올라오면 칸 전체가 강조된다.</summary>
    public sealed record SectorParts(Grid Root, Shape Slice, IReadOnlyList<TextBlock> Texts, ScaleTransform Scale)
    {
        public void Highlight(PanelTheme theme, bool on)
        {
            Slice.Fill = on ? theme.Hover : Brushes.Transparent;
            Texts[0].Foreground = on ? theme.HoverText : theme.Glyph;
            Texts[1].Foreground = on ? theme.HoverText : theme.Label;
            if (Texts.Count > 2) Texts[2].Foreground = on ? theme.HoverText : theme.Caption;
        }
    }

    /// <summary>
    /// 원판 위의 칸 하나: <paramref name="start"/>~<paramref name="end"/> 각도(라디안), 반지름 <paramref name="inner"/>~<paramref name="ringOuter"/>.
    /// 칸 전체가 드롭 영역이다. 결과는 원판과 같은 크기(한 변 2×<paramref name="discRadius"/>)이고 가운데가 원 중심이라,
    /// 커지는 애니메이션이 파란 점에서 시작한다.
    /// </summary>
    public static SectorParts Sector(PanelTheme theme, PanelController.Tile tile, double discRadius, double inner, double ringOuter, double start, double end, double fontScale = 1)
    {
        var center = new Point(discRadius, discRadius);
        Point On(double radius, double angle) => new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);

        // 한 칸이 원 전체면 호를 그릴 수 없으므로 아주 조금 비운다
        if (end - start >= 2 * Math.PI) end = start + 2 * Math.PI - 0.001;
        var large = end - start > Math.PI;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(On(inner, start), isFilled: true, isClosed: true);
            ctx.LineTo(On(ringOuter, start), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(On(ringOuter, end), new Size(ringOuter, ringOuter), 0, large, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(On(inner, end), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(On(inner, start), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        var slice = new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.Transparent, Stroke = theme.Guide, StrokeThickness = 1 };

        var texts = new List<TextBlock>
        {
            new() { Text = Glyph(tile.Target), FontFamily = IconFont, FontSize = 18 * fontScale, Foreground = theme.Glyph, HorizontalAlignment = HorizontalAlignment.Center },
            new()
            {
                Text = tile.Label, FontFamily = TextFont, FontSize = 13 * fontScale, FontWeight = FontWeights.SemiBold,
                Foreground = theme.Label, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1 * fontScale, 0, 0),
            },
        };
        if (tile.Caption is not null)
            texts.Add(new TextBlock { Text = tile.Caption, FontFamily = TextFont, FontSize = 9.5 * fontScale, Foreground = theme.Caption, HorizontalAlignment = HorizontalAlignment.Center });
        var stack = new StackPanel();
        foreach (var t in texts) stack.Children.Add(t);

        // 글자는 칸의 가운데에
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var middle = On((inner + ringOuter) / 2, (start + end) / 2);
        Canvas.SetLeft(stack, middle.X - stack.DesiredSize.Width / 2);
        Canvas.SetTop(stack, middle.Y - stack.DesiredSize.Height / 2);
        var labels = new Canvas { IsHitTestVisible = false, Children = { stack } };

        var scale = new ScaleTransform(1, 1);
        var root = new Grid
        {
            Width = 2 * discRadius, Height = 2 * discRadius,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, new TranslateTransform() } },
            Children = { slice, labels },
        };
        return new SectorParts(root, slice, texts, scale);
    }

    /// <summary>버블 아이콘 (Segoe Fluent Icons / MDL2 코드포인트).</summary>
    public static string Glyph(string target) => target switch
    {
        PanelController.ZipTile => "",     // Package
        PanelController.UnzipTile => "",   // Folder
        PanelController.MergeTile => "",   // Copy
        PanelController.SplitTile => "",   // Cut
        PanelController.ShrinkTile => "",  // BackToWindow (줄이기 화살표)
        PanelController.TransformGroup or "rotate_right" or "rotate_left" or "rotate_180" => "", // Rotate
        "flip_horizontal" or "flip_vertical" => "",  // Switch
        "pdf" or "txt" => "",              // Document
        "mp4" => "",                       // Video
        "mp3" or "wav" or "m4a" => "",     // Audio
        _ when Formats.IsImage(target) || target == "ico" => "", // Photo
        _ => "",                           // Page
    };
}
