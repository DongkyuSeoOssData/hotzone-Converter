namespace Hotzone.Core;

/// <summary>작업 큐: 동시 실행 수 제한, 체인 실행, 출력 쓰기 (설계 4.1).</summary>
public sealed class ConversionQueue(ConverterRegistry registry, int? maxParallel = null)
{
    private readonly SemaphoreSlim _slots = new(maxParallel ?? Math.Max(1, Environment.ProcessorCount / 2));

    public async Task<string> ConvertAsync(
        string sourcePath,
        string targetFormat,
        IReadOnlyDictionary<string, string>? options = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var path = registry.FindPath(Formats.Detect(sourcePath), targetFormat)
            ?? throw new NotSupportedException($"{Path.GetFileName(sourcePath)} → {targetFormat} 변환 경로가 없습니다");

        await _slots.WaitAsync(ct);
        var work = Path.Combine(Path.GetTempPath(), "hotzone_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            IReadOnlyList<string> current = [sourcePath];
            for (var step = 0; step < path.Count; step++)
            {
                var edge = path[step];
                var outputs = new List<string>();
                for (var i = 0; i < current.Count; i++)
                {
                    var dir = Path.Combine(work, $"step{step}", i.ToString());
                    Directory.CreateDirectory(dir);
                    var request = new ConversionRequest(current[i], edge.To, dir, options ?? new Dictionary<string, string>());
                    // 전체 진행률 = (끝난 단계 + 이번 단계 안에서의 비율) / 단계 수
                    var (s, n, index) = (step, current.Count, i);
                    var scaled = progress is null ? null : new Scaled(p => progress.Report((s + (index + p) / n) / path.Count));
                    var result = await edge.Converter.ConvertAsync(request, scaled, ct);
                    scaled?.Report(1);
                    outputs.AddRange(result.Files);
                }
                current = outputs;
            }
            return OutputWriter.Write(sourcePath, targetFormat, current);
        }
        finally
        {
            _slots.Release();
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>받은 값을 바로 변환해 넘긴다 (Progress&lt;T&gt;와 달리 스레드를 갈아타지 않음).</summary>
    private sealed class Scaled(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(Math.Clamp(value, 0, 1));
    }
}
