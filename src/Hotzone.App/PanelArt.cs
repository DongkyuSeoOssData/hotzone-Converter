using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Hotzone.Core;

namespace Hotzone.App;

/// <summary>원판·가운데 원·버블 그리기. 패널과 환경설정의 테마 미리보기가 같은 그림을 쓴다.</summary>
public static class PanelArt
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Display, Segoe UI, Malgun Gothic");

    /// <summary>버블 하나의 부품. 마우스(드래그)가 올라오면 색을 바꾸고 크기를 키운다.</summary>
    public sealed record BubbleParts(Grid Root, Ellipse Circle, IReadOnlyList<TextBlock> Texts, ScaleTransform Scale)
    {
        public void Highlight(PanelTheme theme, bool on)
        {
            Circle.Fill = on ? theme.Hover : theme.Bubble;
            Texts[0].Foreground = on ? theme.HoverText : theme.Glyph;
            Texts[1].Foreground = on ? theme.HoverText : theme.Label;
            if (Texts.Count > 2) Texts[2].Foreground = on ? theme.HoverText : theme.Caption;
        }
    }

    /// <summary>원판 + 위쪽 광택. 열림 애니메이션용 ScaleTransform이 붙어 있다.</summary>
    public static Grid Disc(PanelTheme theme, double outer)
    {
        var grid = new Grid
        {
            Width = 2 * outer, Height = 2 * outer,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
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

    public static BubbleParts Bubble(PanelTheme theme, PanelController.Tile tile, double size)
    {
        var circle = new Ellipse { Fill = theme.Bubble, Stroke = theme.BubbleRim, StrokeThickness = 1 };
        if (theme.BubbleShadowOpacity > 0)
            circle.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = theme.BubbleShadowOpacity };

        var scaleText = size / 66;
        var texts = new List<TextBlock>
        {
            new() { Text = Glyph(tile.Target), FontFamily = IconFont, FontSize = 17 * scaleText, Foreground = theme.Glyph, HorizontalAlignment = HorizontalAlignment.Center },
            new()
            {
                Text = tile.Label, FontFamily = TextFont, FontSize = 13 * scaleText, FontWeight = FontWeights.SemiBold,
                Foreground = theme.Label, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1 * scaleText, 0, 0),
            },
        };
        if (tile.Caption is not null)
            texts.Add(new TextBlock { Text = tile.Caption, FontFamily = TextFont, FontSize = 9.5 * scaleText, Foreground = theme.Caption, HorizontalAlignment = HorizontalAlignment.Center });

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        foreach (var t in texts) stack.Children.Add(t);

        var scale = new ScaleTransform(1, 1);
        var root = new Grid
        {
            Width = size, Height = size,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, new TranslateTransform() } },
            Children = { circle, stack },
        };
        return new BubbleParts(root, circle, texts, scale);
    }

    /// <summary>버블 아이콘 (Segoe Fluent Icons / MDL2 코드포인트).</summary>
    public static string Glyph(string target) => target switch
    {
        PanelController.ZipTile => "",     // Package
        PanelController.UnzipTile => "",   // Folder
        PanelController.MergeTile => "",   // Copy
        PanelController.SplitTile => "",   // Cut
        PanelController.ShrinkTile => "",  // BackToWindow (줄이기 화살표)
        "pdf" or "txt" => "",              // Document
        "mp4" => "",                       // Video
        "mp3" or "wav" or "m4a" => "",     // Audio
        _ when Formats.IsImage(target) || target == "ico" => "", // Photo
        _ => "",                           // Page
    };
}
