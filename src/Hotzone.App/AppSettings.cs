using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotzone.App;

/// <summary>핫존 위치: 모니터의 상하좌우 가장자리 가운데 또는 네 구석.</summary>
public enum HotzonePosition { Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>패널 여는 방식: 스페이스를 누르면 커서 위치에 바로 / 핫존으로 가져가야.</summary>
public enum OpenMode { Cursor, Hotzone }

/// <summary>%APPDATA%\HotzoneConverter\settings.json</summary>
public sealed record AppSettings
{
    public OpenMode Mode { get; init; } = OpenMode.Cursor;

    public PanelThemeKind Theme { get; init; } = PanelThemeKind.GlassLight;

    public bool LaunchAtStartup { get; init; } = true;

    /// <summary><c>\\.\DISPLAY1</c> 같은 모니터 장치 이름. 없거나 연결이 끊기면 주 모니터.</summary>
    public string? Monitor { get; init; }

    public HotzonePosition Position { get; init; } = HotzonePosition.Right;

    /// <summary>진행 알림을 띄울 모니터 (null = 주 모니터).</summary>
    public string? ToastMonitor { get; init; }

    /// <summary>진행 알림 위치: 네 구석 중 하나 (작업 표시줄은 피한다).</summary>
    public HotzonePosition ToastPosition { get; init; } = HotzonePosition.BottomRight;

    /// <summary>진행 알림 창을 띄울지. 꺼도 작업 기록에는 남는다.</summary>
    public bool ShowToasts { get; init; } = true;

    /// <summary>모든 작업이 끝난 뒤 알림 창이 사라지기까지 초. 0이면 닫기를 눌러야 닫힌다.</summary>
    public int ToastDismissSeconds { get; init; } = 5;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotzoneConverter", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
        {
            // 예: 지금은 없는 테마 이름이 저장돼 있으면 기본값으로 시작
            Log.Error("설정 파일을 읽지 못해 기본값을 씁니다", ex);
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
