using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hotzone.App;

/// <summary>개발용: <c>HotzoneConverter.exe --render-preview 폴더</c> → 테마별 패널과 환경설정 창을 PNG로 저장하고 종료.</summary>
internal static class PreviewRenderer
{
    public static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var kind in PanelTheme.All)
        {
            Save(Panel(PanelTheme.Get(kind)), Path.Combine(dir, $"panel_{kind}.png"));
            Save(Toasts(kind), Path.Combine(dir, $"toast_{kind}.png"));
        }

        Save(new Image { Source = TrayIcon.ImageSource, Width = 256, Height = 256 }, Path.Combine(dir, "icon.png"));

        var history = new JobHistory();
        foreach (var job in SampleJobs()) history.Add(job);
        var settings = new SettingsWindow(new AppSettings(), history) { Left = -10000, Top = -10000, ShowActivated = false };
        settings.Show();
        var content = (Panel)settings.Content;
        content.Background = settings.Background; // 창 배경은 내용물에 안 들어가므로 같이 칠한다
        foreach (var page in new[] { "general", "look", "toast", "history" })
        {
            settings.ShowPage(page);
            settings.UpdateLayout();
            Save(content, Path.Combine(dir, $"settings_{page}.png"));
        }
        settings.Close();
    }

    /// <summary>진행 중(진행률 있음·없음) / 완료 / 실패 예시.</summary>
    private static List<JobRecord> SampleJobs()
    {
        var failed = new JobRecord { Glyph = PanelArt.Glyph(PanelController.ZipTile), Action = "ZIP 압축", Detail = "사진 모음", Started = DateTime.Now.AddMinutes(-12) };
        failed.Fail("다른 프로그램이 파일을 사용 중입니다.");
        var done = new JobRecord { Glyph = PanelArt.Glyph("jpeg"), Action = "JPG 변환", Detail = "보고서.pdf", Started = DateTime.Now.AddMinutes(-3) };
        done.Complete([@"C:\보고서\보고서_01.jpg"]);
        var flowing = new JobRecord { Glyph = PanelArt.Glyph("png"), Action = "PNG 변환", Detail = "스크린샷 외 4개" };
        var running = new JobRecord { Glyph = PanelArt.Glyph("mp4"), Action = "MP4 변환", Detail = "여행 영상.mov" };
        running.Report(0.42);
        return [failed, done, flowing, running]; // 마지막이 맨 위에 온다
    }

    private static FrameworkElement Panel(PanelTheme theme)
    {
        const double size = 460, c = size / 2, outer = 163, ring = 112, bubble = 66;
        var canvas = new Canvas
        {
            Width = size, Height = size,
            Background = new LinearGradientBrush(
                [new GradientStop(Color.FromRgb(0x5B, 0x8C, 0xFF), 0), new GradientStop(Color.FromRgb(0xB0, 0x6A, 0xF5), 0.55), new GradientStop(Color.FromRgb(0xFF, 0x9E, 0x6B), 1)],
                new Point(0, 0), new Point(1, 1)),
        };
        void Put(FrameworkElement e, double x, double y)
        {
            Canvas.SetLeft(e, x - e.Width / 2);
            Canvas.SetTop(e, y - e.Height / 2);
            canvas.Children.Add(e);
        }
        Put(PanelArt.Disc(theme, outer), c, c);
        Put(PanelArt.Hub(theme, 24), c, c);
        PanelController.Tile[] tiles =
        [
            new("jpeg", "JPG"), new("png", "PNG"), new("pdf", "PDF"), new("webp", "WebP"), new("mp4", "MP4"),
            new(PanelController.ShrinkTile, "용량", "줄이기"), new(PanelController.MergeTile, "PDF", "합치기"), new(PanelController.ZipTile, "ZIP", "압축"),
        ];
        for (var i = 0; i < tiles.Length; i++)
        {
            var parts = PanelArt.Bubble(theme, tiles[i], bubble);
            if (i == 1) parts.Highlight(theme, true);
            var angle = -Math.PI / 2 + 2 * Math.PI * i / tiles.Length;
            Put(parts.Root, c + Math.Cos(angle) * ring, c + Math.Sin(angle) * ring);
        }
        return canvas;
    }

    /// <summary>진행 중 / 완료 / 실패 카드 세 장을 배경화면 위에.</summary>
    private static FrameworkElement Toasts(PanelThemeKind kind)
    {
        var host = new ToastHost { Settings = new AppSettings { Theme = kind } };
        foreach (var job in SampleJobs()) host.Add(job);
        var card = host.DetachCard();
        host.Hide();
        return new Border
        {
            Padding = new Thickness(30),
            Background = new LinearGradientBrush(
                [new GradientStop(Color.FromRgb(0x5B, 0x8C, 0xFF), 0), new GradientStop(Color.FromRgb(0xB0, 0x6A, 0xF5), 0.55), new GradientStop(Color.FromRgb(0xFF, 0x9E, 0x6B), 1)],
                new Point(0, 0), new Point(1, 1)),
            Child = card,
        };
    }

    private static void Save(FrameworkElement element, string path)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
