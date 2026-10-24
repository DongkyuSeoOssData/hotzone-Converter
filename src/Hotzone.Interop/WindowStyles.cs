using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Hotzone.Interop;

/// <summary>핫존·패널 창의 확장 스타일 (설계 3.2).</summary>
public static class WindowStyles
{
    private const WINDOW_EX_STYLE OverlayStyles =
        WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_LAYERED;

    public static void MakeOverlay(nint hwnd, bool clickThrough) => Update(hwnd, OverlayStyles, 0, clickThrough);

    /// <summary>평소엔 클릭 통과, 왼쪽 버튼 down 동안만 드롭을 받는다 (설계 3.3).</summary>
    public static void SetClickThrough(nint hwnd, bool enabled) => Update(hwnd, 0, 0, enabled);

    private static void Update(nint hwnd, WINDOW_EX_STYLE add, WINDOW_EX_STYLE remove, bool clickThrough)
    {
        var h = (HWND)hwnd;
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style = (style | add) & ~remove;
        style = clickThrough ? style | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : style & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)style);
    }

    /// <summary>물리 픽셀로 창을 옮긴다. 크기를 안 주면 위치만 바꾼다.</summary>
    public static void Move(nint hwnd, int x, int y, int? width = null, int? height = null)
    {
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        if (width is null || height is null) flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        PInvoke.SetWindowPos((HWND)hwnd, HWND.Null, x, y, width ?? 0, height ?? 0, flags);
    }

    /// <summary>최상단 창들 중에서도 맨 위로 (활성화하지 않음).</summary>
    public static void BringToTop(nint hwnd) =>
        PInvoke.SetWindowPos((HWND)hwnd, (HWND)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public static (int Left, int Top, int Right, int Bottom) Bounds(nint hwnd)
    {
        PInvoke.GetWindowRect((HWND)hwnd, out var r);
        return (r.left, r.top, r.right, r.bottom);
    }

    public static (int X, int Y) CursorPosition()
    {
        PInvoke.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>전체 화면 앱(게임·영상·프레젠테이션) 위에서는 핫존을 끈다 (설계 8).</summary>
    public static bool IsFullscreenAppRunning()
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Failed) return false;
        return state is Windows.Win32.UI.Shell.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
            or Windows.Win32.UI.Shell.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
            or Windows.Win32.UI.Shell.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
    }
}
