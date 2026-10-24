using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotzone.App;

public enum JobStatus { Running, Done, Failed }

/// <summary>
/// 드롭 한 번의 작업. 알림 창과 환경설정의 작업 기록이 같은 객체를 보고, 바뀔 때마다 <see cref="Changed"/>로 다시 그린다.
/// UI 스레드에서만 고친다.
/// </summary>
public sealed class JobRecord
{
    public required string Glyph { get; init; }
    /// <summary>"JPG 변환", "ZIP 압축" 같은 동작 이름.</summary>
    public required string Action { get; init; }
    /// <summary>"보고서.pdf" 또는 "보고서.pdf 외 2개".</summary>
    public required string Detail { get; init; }
    public DateTime Started { get; init; } = DateTime.Now;

    public JobStatus Status { get; set; } = JobStatus.Running;
    /// <summary>0~1. 진행률을 모르는 작업은 null.</summary>
    public double? Progress { get; set; }
    public List<string> Results { get; set; } = [];
    /// <summary>실패 이유나 일부 실패 안내.</summary>
    public string? Message { get; set; }

    [JsonIgnore]
    public string Title => Status switch
    {
        JobStatus.Running => $"{Action} 중…",
        JobStatus.Done => $"{Action} 완료",
        _ => $"{Action} 실패",
    };

    public event Action<JobRecord>? Changed;

    public void Report(double value)
    {
        if (Status != JobStatus.Running) return;
        Progress = Math.Clamp(value, 0, 1);
        Changed?.Invoke(this);
    }

    public void Complete(IReadOnlyList<string> results, string? warning = null)
    {
        Status = JobStatus.Done;
        Results = [.. results];
        Message = warning;
        Changed?.Invoke(this);
    }

    public void Fail(string message)
    {
        Status = JobStatus.Failed;
        Message = message;
        Changed?.Invoke(this);
    }
}

/// <summary>
/// 작업 기록: 진행 중 + 최근 끝난 작업 (최신이 앞). 끝난 작업은 %LOCALAPPDATA%\HotzoneConverter\history.json에 최근 100개까지 남긴다.
/// </summary>
public sealed class JobHistory
{
    private const int Keep = 100;
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotzoneConverter", "history.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly List<JobRecord> _items = [];

    public IReadOnlyList<JobRecord> Items => _items;

    /// <summary>작업이 추가되거나 지워졌을 때 (진행률 변화는 각 작업의 Changed).</summary>
    public event Action? ListChanged;

    public static JobHistory Load()
    {
        var history = new JobHistory();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<List<JobRecord>>(File.ReadAllText(FilePath), Json) is { } saved)
                history._items.AddRange(saved.Where(j => j.Status != JobStatus.Running));
        }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
        {
            Log.Error("작업 기록을 읽지 못했습니다", ex);
        }
        return history;
    }

    public void Add(JobRecord job)
    {
        _items.Insert(0, job);
        job.Changed += j => { if (j.Status != JobStatus.Running) Save(); };
        if (_items.Count > Keep) _items.RemoveRange(Keep, _items.Count - Keep);
        ListChanged?.Invoke();
    }

    /// <summary>끝난 기록을 모두 지운다 (진행 중인 작업은 남김).</summary>
    public void ClearFinished()
    {
        _items.RemoveAll(j => j.Status != JobStatus.Running);
        Save();
        ListChanged?.Invoke();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items.Where(j => j.Status != JobStatus.Running).ToList(), Json));
        }
        catch (IOException ex)
        {
            Log.Error("작업 기록 저장 실패", ex);
        }
    }
}
