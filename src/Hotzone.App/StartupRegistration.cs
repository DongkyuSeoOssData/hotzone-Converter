using Microsoft.Win32;

namespace Hotzone.App;

/// <summary>Windows 시작 시 자동 실행: HKCU\...\Run 값 (관리자 권한 불필요, 현재 사용자만).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HotzoneConverter";

    /// <summary>켜면 지금 실행 중인 exe 경로로 등록(옮겼으면 새 경로로 갱신), 끄면 지운다.</summary>
    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
            {
                var command = $"\"{exe}\"";
                if (key.GetValue(ValueName) as string == command) return;
                key.SetValue(ValueName, command);
                Log.Info($"시작 프로그램 등록: {command}");
            }
            else if (!enabled && key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("시작 프로그램 해제");
            }
        }
        catch (Exception ex)
        {
            Log.Error("시작 프로그램 설정 실패", ex);
        }
    }
}
