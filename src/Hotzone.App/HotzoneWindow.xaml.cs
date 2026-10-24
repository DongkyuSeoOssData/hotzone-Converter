using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Hotzone.Interop;

namespace Hotzone.App;

/// <summary>
/// 화면 가장자리의 얇은 드롭 타깃 창 (설계 3.2, 3.5).
/// WPF는 AllowDrop 창을 OLE IDropTarget으로 등록하므로 탐색기 드래그의 DragEnter/DragOver를 그대로 받는다.
/// </summary>
public partial class HotzoneWindow : Window
{
    private const double Thickness = 4;
    private const double LengthRatio = 0.4;
    private const double CornerSize = 16;

    private readonly Func<bool> _isArmed;
    private nint _hwnd;

    /// <summary>파일 드래그가 핫존 안에 있는 동안의 경로 목록 (DragEnter에서 한 번만 추출해 캐시).</summary>
    public IReadOnlyList<string>? DragInside { get; private set; }

    public event Action<IReadOnlyList<string>>? ArmedDragOver;
    public event Action? DragLeft;

    public HotzoneWindow(Func<bool> isArmed)
    {
        _isArmed = isArmed;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowStyles.MakeOverlay(_hwnd, clickThrough: true);
        };
        DpiChanged += (_, _) => Place();
        DragEnter += OnDrag;
        DragOver += OnDrag;
        DragLeave += (_, _) => { DragInside = null; DragLeft?.Invoke(); };
        Drop += (_, e) => { DragInside = null; e.Effects = DragDropEffects.None; e.Handled = true; };
    }

    public AppSettings Settings { get; set; } = new();

    /// <summary>
    /// 설정한 모니터·위치에 붙인다 (Show 뒤, 설정·화면 구성이 바뀔 때 호출).
    /// 가장자리는 두께 4 × 길이 40% 띠, 구석은 작은 정사각형.
    /// </summary>
    public void Place()
    {
        if (_hwnd == 0) return;
        var bounds = Edge.Screen(Settings.Monitor).Bounds;
        var position = Settings.Position;
        // 먼저 1px로 줄여 대상 모니터 중앙에 둔다 (가장자리에 두면 옆 모니터 DPI로 판정될 수 있다).
        // DPI 변경은 비동기로 늦게 올 수 있어서 DpiChanged에서 다시 부른다.
        // 크기는 WPF(DIP)에 맡기고 (픽셀로 직접 주면 WPF가 배율로 한 번 더 나눈다) 위치만 실제 크기 기준 픽셀로 맞춘다
        WindowStyles.Move(_hwnd, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 1, 1);
        var scale = VisualTreeHelper.GetDpi(this);
        if (Edge.IsCorner(position))
        {
            Width = Height = CornerSize;
        }
        else if (position is HotzonePosition.Left or HotzonePosition.Right)
        {
            Width = Thickness;
            Height = bounds.Height * LengthRatio / scale.DpiScaleY;
        }
        else
        {
            Width = bounds.Width * LengthRatio / scale.DpiScaleX;
            Height = Thickness;
        }
        UpdateLayout();
        var (left, top, right, bottom) = WindowStyles.Bounds(_hwnd);
        var at = Edge.Clamp(bounds, Edge.Anchor(bounds, position), right - left, bottom - top);
        WindowStyles.Move(_hwnd, at.X, at.Y);
    }

    public void SetClickThrough(bool enabled)
    {
        if (_hwnd == 0) return;
        // 전체 화면 앱 위에서는 계속 클릭 통과로 둬서 핫존을 사실상 끈다
        if (!enabled && WindowStyles.IsFullscreenAppRunning()) return;
        WindowStyles.SetClickThrough(_hwnd, enabled);
        // 아래쪽 가장자리 등에서 작업 표시줄(역시 최상단)에 가리지 않게 드래그 시작마다 맨 위로 올린다
        if (!enabled) WindowStyles.BringToTop(_hwnd);
    }

    private void OnDrag(object sender, DragEventArgs e)
    {
        e.Handled = true;
        // CF_HDROP 없으면 무시 (텍스트·브라우저 이미지 드래그)
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        DragInside ??= paths;
        // DragOver는 핫존 안에 있는 동안 계속 오므로, 진입 후 스페이스를 탭해도 다음 DragOver에서 펼쳐진다
        if (_isArmed())
        {
            e.Effects = DragDropEffects.Copy;
            ArmedDragOver?.Invoke(DragInside);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }
}
