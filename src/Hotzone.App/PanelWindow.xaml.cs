using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Hotzone.Interop;

namespace Hotzone.App;

/// <summary>형식 타일 패널. MVP는 세로 리스트 (반원 팬은 열린 질문).</summary>
public partial class PanelWindow : Window
{
    private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x24));

    private readonly PanelController _controller;
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private IReadOnlyList<string>? _files;

    public event Action<string, IReadOnlyList<string>>? ConvertRequested;

    public PanelWindow(PanelController controller)
    {
        _controller = controller;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle, clickThrough: false);
        _collapse.Tick += (_, _) => { _collapse.Stop(); Hide(); _files = null; };
        DragEnter += (_, _) => _collapse.Stop();
        DragLeave += (_, _) => ScheduleCollapse();
    }

    public void OpenFor(IReadOnlyList<string> files)
    {
        _collapse.Stop();
        if (IsVisible && ReferenceEquals(files, _files)) return;
        _files = files;

        var tiles = _controller.TilesFor(files);
        if (tiles.Count == 0) return;
        Tiles.Children.Clear();
        foreach (var tile in tiles) Tiles.Children.Add(CreateTile(tile));

        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    }

    public void ScheduleCollapse() => _collapse.Start();

    private Border CreateTile(PanelController.Tile tile)
    {
        var border = new Border
        {
            Height = 56,
            Margin = new Thickness(0, 3, 0, 3),
            CornerRadius = new CornerRadius(8),
            Background = IdleBrush,
            AllowDrop = true,
            Child = new TextBlock
            {
                Text = tile.Lossy ? tile.Label + "  ⚠" : tile.Label,
                Foreground = Brushes.White,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ToolTip = tile.Lossy ? "텍스트 선택이 안 되는 이미지로 변환됩니다" : null,
        };
        border.DragEnter += (_, e) =>
        {
            border.Background = HoverBrush;
            e.Effects = DragDropEffects.Copy; // 원본 이동 방지
            e.Handled = true;
        };
        border.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        border.DragLeave += (_, _) => border.Background = IdleBrush;
        border.Drop += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            border.Background = IdleBrush;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            {
                Hide();
                _files = null;
                ConvertRequested?.Invoke(tile.Target, paths);
            }
        };
        return border;
    }
}
