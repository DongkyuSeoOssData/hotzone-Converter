using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Hotzone.Interop;

/// <summary>
/// WH_MOUSE_LL + WH_KEYBOARD_LL: 클릭 통과 토글과 스페이스 무장 (설계 3.3, 3.4).
/// 훅은 전용 스레드의 메시지 루프에서 돈다. 콜백은 플래그만 바꾸고 즉시 리턴해야
/// LowLevelHooksTimeout에 걸려 OS가 훅을 떼어내지 않는다. 이벤트는 훅 스레드에서 발생하므로
/// 구독자가 UI 스레드로 넘겨야 한다.
/// </summary>
public sealed unsafe class InputHook : IDisposable
{
    /// <summary>탐색기·바탕화면·작업 표시줄이 포그라운드일 때만 스페이스를 가로챈다 (설계 8).</summary>
    public static readonly HashSet<string> DefaultAllowedClasses =
        ["CabinetWClass", "ExploreWClass", "Progman", "WorkerW", "Shell_TrayWnd"];

    private static readonly TimeSpan ArmTimeout = TimeSpan.FromSeconds(10);
    private const int VK_SPACE = 0x20, VK_ESCAPE = 0x1B, VK_LBUTTON = 0x01;
    private const uint LLKHF_INJECTED = 0x10;

    // 저수준 훅 콜백은 static이어야 하므로 인스턴스는 하나만 둔다
    private static InputHook? _current;

    private readonly HashSet<string> _allowedClasses;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _buttonDown;
    private volatile bool _armed;
    private long _armedAtTicks;
    private bool _swallowingSpace;

    public event Action<bool>? LeftButtonChanged;
    public event Action<bool>? ArmedChanged;
    /// <summary>드래그 중 스페이스를 새로 누를 때마다 (자동 반복 제외). 이미 무장돼 있어도 발생한다.</summary>
    public event Action? SpaceTapped;
    /// <summary>드래그 중 스페이스를 눌렀지만 무시한 이유 (진단용, 예: 허용되지 않은 포그라운드 창).</summary>
    public event Action<string>? SpaceIgnored;

    public InputHook(IEnumerable<string>? allowedClasses = null)
    {
        _allowedClasses = new HashSet<string>(allowedClasses ?? DefaultAllowedClasses);
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
            throw new InvalidOperationException("InputHook은 하나만 실행할 수 있습니다");
        _thread = new Thread(Run) { IsBackground = true, Name = "InputHook" };
        _thread.Start();
    }

    public void Dispose()
    {
        if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread?.Join(TimeSpan.FromSeconds(1));
        Interlocked.CompareExchange(ref _current, null, this);
    }

    /// <summary>무장 고착 방지: 시간 초과나 버튼이 실제로 떼어져 있으면 해제한다.</summary>
    public bool IsArmed
    {
        get
        {
            if (_armed && (Environment.TickCount64 - Interlocked.Read(ref _armedAtTicks) > ArmTimeout.TotalMilliseconds
                           || (PInvoke.GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0))
                SetArmed(false);
            return _armed;
        }
    }

    private void SetArmed(bool value)
    {
        if (_armed == value) return;
        _armed = value;
        Interlocked.Exchange(ref _armedAtTicks, value ? Environment.TickCount64 : 0);
        ArmedChanged?.Invoke(value);
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        var module = PInvoke.GetModuleHandle((PCWSTR)null);
        var mouse = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, &MouseProc, (HINSTANCE)module.Value, 0);
        var keyboard = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &KeyboardProc, (HINSTANCE)module.Value, 0);
        try
        {
            MSG msg;
            while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0) > 0) { }
        }
        finally
        {
            PInvoke.UnhookWindowsHookEx(mouse);
            PInvoke.UnhookWindowsHookEx(keyboard);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT MouseProc(int code, WPARAM wParam, LPARAM lParam)
    {
        var self = _current;
        if (code >= 0 && self is not null)
        {
            if (wParam.Value == PInvoke.WM_LBUTTONDOWN)
            {
                self._buttonDown = true;
                self.LeftButtonChanged?.Invoke(true);
            }
            else if (wParam.Value == PInvoke.WM_LBUTTONUP)
            {
                self._buttonDown = false;
                self.SetArmed(false);
                self.LeftButtonChanged?.Invoke(false);
            }
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT KeyboardProc(int code, WPARAM wParam, LPARAM lParam)
    {
        var self = _current;
        if (code >= 0 && self is not null)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam.Value;
            var down = wParam.Value is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
            if (info->vkCode == VK_ESCAPE && down)
            {
                self.SetArmed(false);
            }
            else if (info->vkCode == VK_SPACE && ((uint)info->flags & LLKHF_INJECTED) == 0)
            {
                if (down && self._buttonDown && !self._swallowingSpace)
                {
                    var foreground = ForegroundClass();
                    if (self._allowedClasses.Contains(foreground))
                    {
                        self.SetArmed(true);
                        self._swallowingSpace = true;
                        self.SpaceTapped?.Invoke();
                        return (LRESULT)1;
                    }
                    self.SpaceIgnored?.Invoke($"포그라운드 창 '{foreground}'은 허용 목록에 없음");
                }
                if (self._swallowingSpace)
                {
                    // 자동 반복 keydown과 keyup도 같이 삼켜 뒤늦은 공백 입력을 막는다
                    if (!down) self._swallowingSpace = false;
                    return (LRESULT)1;
                }
            }
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static string ForegroundClass()
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* p = buffer)
        {
            var length = PInvoke.GetClassName(PInvoke.GetForegroundWindow(), p, buffer.Length);
            return new string(p, 0, length);
        }
    }
}
