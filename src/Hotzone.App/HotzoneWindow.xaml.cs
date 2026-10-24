using System.Windows;
using System.Windows.Interop;
using Hotzone.Interop;

namespace Hotzone.App;

/// <summary>
/// 화면 가장자리의 얇은 드롭 타깃 창 (설계 3.2, 3.5).
/// WPF는 AllowDrop 창을 OLE IDropTarget으로 등록하므로 탐색기 드래그의 DragEnter/DragOver를 그대로 받는다.
/// </summary>
public partial class HotzoneWindow : Window
{
    private const double Thickness = 4;
    private const double HeightRatio = 0.4;

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
        Place();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowStyles.MakeOverlay(_hwnd, clickThrough: true);
        };
        DragEnter += OnDrag;
        DragOver += OnDrag;
        DragLeave += (_, _) => { DragInside = null; DragLeft?.Invoke(); };
        Drop += (_, e) => { DragInside = null; e.Effects = DragDropEffects.None; e.Handled = true; };
    }

    /// <summary>기본 위치: 주 모니터 오른쪽 가장자리 세로 중앙. TODO: 다중 모니터, 작업 표시줄 회피.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Width = Thickness;
        Height = area.Height * HeightRatio;
        Left = area.Right - Thickness;
        Top = area.Top + (area.Height - Height) / 2;
    }

    public void SetClickThrough(bool enabled)
    {
        if (_hwnd == 0) return;
        // 전체 화면 앱 위에서는 계속 클릭 통과로 둬서 핫존을 사실상 끈다
        if (!enabled && WindowStyles.IsFullscreenAppRunning()) return;
        WindowStyles.SetClickThrough(_hwnd, enabled);
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
