using System.Windows;
using System.Windows.Media;

namespace Hotzone.App;

public enum PanelThemeKind { GlassLight, GlassDark }

/// <summary>
/// 원형 패널의 색·질감. 글라스 테마는 iOS 18 제어 센터 느낌(반투명 유리 + 위쪽 광택 + 흰 테두리 + 시스템 블루).
/// Windows 아크릴 흐림은 사각형 창 전체에만 걸려 원판 모양에 쓸 수 없으므로, 유리감은 반투명 그라데이션과 광택으로 낸다.
/// </summary>
public sealed class PanelTheme
{
    public required string Name { get; init; }
    public required Brush Disc { get; init; }
    public required Brush DiscRim { get; init; }
    /// <summary>원판 위쪽 광택 (없으면 null).</summary>
    public Brush? Sheen { get; init; }
    public required Brush Guide { get; init; }
    public required Brush Bubble { get; init; }
    public required Brush BubbleRim { get; init; }
    public required Brush Label { get; init; }
    public required Brush Caption { get; init; }
    public required Brush Glyph { get; init; }
    public required Brush Hover { get; init; }
    public required Brush HoverText { get; init; }
    public required Brush Hub { get; init; }
    public Brush? HubRim { get; init; }
    public double ShadowOpacity { get; init; } = 0.5;
    public double BubbleShadowOpacity { get; init; }

    public static PanelTheme Get(PanelThemeKind kind) => kind switch
    {
        PanelThemeKind.GlassDark => GlassDark,
        _ => GlassLight,
    };

    public static readonly IReadOnlyList<PanelThemeKind> All = Enum.GetValues<PanelThemeKind>();

    private static readonly Brush SystemBlue = Vertical(Color.FromRgb(0x40, 0xA0, 0xFF), Color.FromRgb(0x0A, 0x7A, 0xFF));

    private static readonly PanelTheme GlassLight = new()
    {
        Name = "글라스 라이트",
        Disc = Vertical(Argb(0xC8, 0xFA, 0xFA, 0xFC), Argb(0xB4, 0xEE, 0xEE, 0xF3)),
        DiscRim = Vertical(Argb(0xF5, 0xFF, 0xFF, 0xFF), Argb(0x55, 0xFF, 0xFF, 0xFF)),
        Sheen = TopSheen(0x70),
        Guide = Solid(Argb(0x16, 0, 0, 0)),
        Bubble = Vertical(Argb(0xF5, 0xFF, 0xFF, 0xFF), Argb(0xDC, 0xF5, 0xF5, 0xF8)),
        BubbleRim = Solid(Argb(0x14, 0, 0, 0)),
        Label = Solid(Color.FromRgb(0x1C, 0x1C, 0x1E)),
        Caption = Solid(Color.FromRgb(0x86, 0x86, 0x8B)),
        Glyph = Solid(Color.FromRgb(0x0A, 0x7A, 0xFF)),
        Hover = SystemBlue,
        HoverText = Brushes.White,
        Hub = SystemBlue,
        HubRim = Solid(Argb(0xE6, 0xFF, 0xFF, 0xFF)),
        ShadowOpacity = 0.28,
        BubbleShadowOpacity = 0.14,
    };

    private static readonly PanelTheme GlassDark = new()
    {
        Name = "글라스 다크",
        Disc = Vertical(Argb(0xD2, 0x3A, 0x3A, 0x3D), Argb(0xCC, 0x1C, 0x1C, 0x1E)),
        DiscRim = Vertical(Argb(0x5C, 0xFF, 0xFF, 0xFF), Argb(0x10, 0xFF, 0xFF, 0xFF)),
        Sheen = TopSheen(0x26),
        Guide = Solid(Argb(0x18, 0xFF, 0xFF, 0xFF)),
        Bubble = Vertical(Argb(0x3A, 0xFF, 0xFF, 0xFF), Argb(0x1C, 0xFF, 0xFF, 0xFF)),
        BubbleRim = Vertical(Argb(0x55, 0xFF, 0xFF, 0xFF), Argb(0x0E, 0xFF, 0xFF, 0xFF)),
        Label = Brushes.White,
        Caption = Solid(Argb(0x8C, 0xFF, 0xFF, 0xFF)),
        Glyph = Solid(Argb(0xE6, 0xFF, 0xFF, 0xFF)),
        Hover = SystemBlue,
        HoverText = Brushes.White,
        Hub = SystemBlue,
        HubRim = Solid(Argb(0x5C, 0xFF, 0xFF, 0xFF)),
        ShadowOpacity = 0.5,
        BubbleShadowOpacity = 0.25,
    };

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static Brush Solid(Color c) => Freeze(new SolidColorBrush(c));

    private static Brush Vertical(Color top, Color bottom) => Freeze(new LinearGradientBrush(top, bottom, 90));

    /// <summary>위쪽에서 비치는 유리 광택.</summary>
    private static Brush TopSheen(byte alpha) => Freeze(new RadialGradientBrush
    {
        Center = new Point(0.5, 0.12),
        GradientOrigin = new Point(0.5, 0.0),
        RadiusX = 0.65,
        RadiusY = 0.45,
        GradientStops =
        {
            new GradientStop(Argb(alpha, 0xFF, 0xFF, 0xFF), 0),
            new GradientStop(Argb(0, 0xFF, 0xFF, 0xFF), 1),
        },
    });

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
