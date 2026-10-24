using System.IO;
using Hotzone.Converters;
using Hotzone.Core;

namespace Hotzone.App;

/// <summary>입력 파일 → 표시할 버블 결정 (설계 4.4 ④⑤). 형식 변환 + 파일 도구(합치기·나누기·용량·ZIP).</summary>
public sealed class PanelController(ConverterRegistry registry)
{
    public const string MergeTile = "merge_pdf";
    public const string SplitTile = "split_pdf";
    public const string ShrinkTile = "shrink";
    public const string ZipTile = "zip";
    public const string UnzipTile = "unzip";

    // 카테고리 기본 우선순위 (사용 빈도 재정렬은 Phase 2)
    private static readonly string[] TargetOrder =
        ["pdf", "jpeg", "png", "webp", "mp4", "mp3", "gif", "avif", "txt", "m4a", "wav", "tiff", "ico"];
    private const int MaxTargets = 6;
    // DragEnter에서는 앞의 몇 개 파일만 판별 (패널 지연 방지), 나머지는 드롭 후 변환 시 검증
    private const int SniffLimit = 5;

    /// <param name="Caption">버블 아래 작은 설명 (없으면 null).</param>
    public record Tile(string Target, string Label, string? Caption = null);

    public IReadOnlyList<Tile> TilesFor(IReadOnlyList<string> files)
    {
        var anyFolder = files.Any(Directory.Exists);
        List<string> formats = anyFolder ? [] : files.Take(SniffLimit).Select(Formats.Detect).ToList();

        var targets = anyFolder ? new HashSet<string>() : registry.CommonTargets(formats);
        var ordered = TargetOrder.Where(targets.Contains).Concat(targets.Except(TargetOrder).Order()).Take(MaxTargets);
        var tiles = ordered.Select(t => new Tile(t, Label(t), formats.Any(f => IsLossy(f, t)) ? "⚠ 손실" : null)).ToList();

        if (!anyFolder)
        {
            var all = files.Count <= SniffLimit ? formats : files.Select(Formats.Detect).ToList();
            if (MergeService.CanMerge(all)) tiles.Add(new Tile(MergeTile, "PDF", "합치기"));
            if (all.All(f => f == "pdf")) tiles.Add(new Tile(SplitTile, "PDF", "나누기"));
            if (all.All(FileTools.Shrinkable.Contains)) tiles.Add(new Tile(ShrinkTile, "용량", "줄이기"));
            if (all.All(f => f == "zip")) tiles.Add(new Tile(UnzipTile, "ZIP", "풀기"));
        }
        // 압축은 형식과 상관없이 항상 (폴더 포함)
        tiles.Add(new Tile(ZipTile, "ZIP", "압축"));
        return tiles;
    }

    private static string Label(string format) => format switch
    {
        "jpeg" => "JPG",
        "webp" => "WebP",
        _ => format.ToUpperInvariant(),
    };

    // PDF·오피스 → 이미지는 텍스트 선택이 안 된다
    private static bool IsLossy(string from, string to) =>
        from is "pdf" or "docx" or "pptx" or "xlsx" && Formats.IsImage(to);
}
