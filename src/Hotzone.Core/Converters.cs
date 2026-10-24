namespace Hotzone.Core;

/// <summary>
/// 변환기가 선언하는 간선. 예: <c>[Converts("pdf -> jpeg, png", Cost = 2)]</c>.
/// <see cref="Requires"/> 조건이 충족되지 않으면 간선 자체가 제외되어 타일이 숨겨진다 (설계 4.4 ②).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ConvertsAttribute(string spec) : Attribute
{
    public string Spec { get; } = spec;
    public int Cost { get; init; } = 1;
    public string? Requires { get; init; }

    public (string[] From, string[] To) Parse()
    {
        var parts = Spec.Split("->", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) throw new FormatException($"잘못된 간선 선언: {Spec}");
        static string[] List(string s) =>
            s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
             .Select(Formats.Normalize).ToArray();
        return (List(parts[0]), List(parts[1]));
    }
}

public interface IConverter
{
    string Id { get; }

    /// <summary>결과물(파일 1개 또는 페이지 N장)만 <see cref="ConversionRequest.WorkDir"/>에 만든다. 저장 위치는 OutputWriter가 정한다.</summary>
    Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct);
}

public sealed record ConversionRequest(
    string SourcePath,
    string TargetFormat,
    string WorkDir,
    IReadOnlyDictionary<string, string> Options);

/// <summary>순서가 곧 페이지 순서.</summary>
public sealed record ConversionResult(IReadOnlyList<string> Files);

public sealed record Edge(string From, string To, int Cost, IConverter Converter);
