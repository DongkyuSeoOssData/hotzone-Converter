namespace Hotzone.Core;

/// <summary>작업 큐: 동시 실행 수 제한, 체인 실행, 출력 쓰기 (설계 4.1).</summary>
public sealed class ConversionQueue(ConverterRegistry registry, int? maxParallel = null)
{
    private readonly SemaphoreSlim _slots = new(maxParallel ?? Math.Max(1, Environment.ProcessorCount / 2));

    public async Task<string> ConvertAsync(
        string sourcePath,
        string targetFormat,
        IReadOnlyDictionary<string, string>? options = null,
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
                    var result = await edge.Converter.ConvertAsync(request, null, ct);
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
}
