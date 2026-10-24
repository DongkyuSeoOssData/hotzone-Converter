using Hotzone.Converters;
using Hotzone.Core;

namespace Hotzone.App;

/// <summary>입력 파일 → 표시할 형식 타일 결정 (설계 4.4 ④⑤).</summary>
public sealed class PanelController(ConverterRegistry registry)
{
    public const string MergeTile = "merge_pdf";

    // 카테고리 기본 우선순위 (사용 빈도 재정렬은 Phase 2)
    private static readonly string[] TargetOrder = ["pdf", "jpeg", "png", "webp"];
    private const int MaxTiles = 6;
    // DragEnter에서는 앞의 몇 개 파일만 판별 (패널 지연 방지), 나머지는 드롭 후 변환 시 검증
    private const int SniffLimit = 5;

    public record Tile(string Target, string Label, bool Lossy);

    public IReadOnlyList<Tile> TilesFor(IReadOnlyList<string> files)
    {
        var formats = files.Take(SniffLimit).Select(Formats.Detect).ToList();
        var targets = registry.CommonTargets(formats);
        var ordered = TargetOrder.Where(targets.Contains).Concat(targets.Except(TargetOrder).Order()).Take(MaxTiles);
        var tiles = ordered
            .Select(t => new Tile(t, Label(t), formats.Any(f => IsLossy(f, t))))
            .ToList();
        if (MergeService.CanMerge(files.Select(Formats.Detect).ToList()))
            tiles.Add(new Tile(MergeTile, "PDF로 합치기", false));
        return tiles;
    }

    private static string Label(string format) => format switch
    {
        "jpeg" => "JPG",
        "webp" => "WebP",
        _ => format.ToUpperInvariant(),
    };

    // PDF → 이미지는 텍스트 선택이 안 된다
    private static bool IsLossy(string from, string to) => from is "pdf" or "docx" or "pptx" or "xlsx" && to is "jpeg" or "png";
}
