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
    public const string TransformGroup = "transform";

    /// <summary>회전·반전 동작. 버블 대상 이름 → (동작, 알림에 쓰는 이름).</summary>
    private static readonly Dictionary<string, (ImageTransform Transform, string Action)> Transforms = new()
    {
        ["rotate_right"] = (ImageTransform.RotateRight, "오른쪽 90° 회전"),
        ["rotate_left"] = (ImageTransform.RotateLeft, "왼쪽 90° 회전"),
        ["rotate_180"] = (ImageTransform.Rotate180, "180° 회전"),
        ["flip_horizontal"] = (ImageTransform.FlipHorizontal, "좌우 반전"),
        ["flip_vertical"] = (ImageTransform.FlipVertical, "상하 반전"),
    };

    /// <summary>"회전" 버블을 펼치면 나오는 버블들.</summary>
    private static readonly Tile[] TransformTiles =
    [
        new("rotate_right", "오른쪽 90°"),
        new("rotate_left", "왼쪽 90°"),
        new("rotate_180", "180°"),
        new("flip_horizontal", "좌우 반전"),
        new("flip_vertical", "상하 반전"),
    ];

    public static ImageTransform? TransformFor(string target) =>
        Transforms.TryGetValue(target, out var t) ? t.Transform : null;

    public static string? TransformAction(string target) =>
        Transforms.TryGetValue(target, out var t) ? t.Action : null;

    // 카테고리 기본 우선순위 (사용 빈도 재정렬은 Phase 2)
    private static readonly string[] TargetOrder =
        ["pdf", "jpeg", "png", "webp", "mp4", "mp3", "gif", "avif", "txt", "m4a", "wav", "tiff", "ico"];
    private const int MaxTargets = 6;
    // DragEnter에서는 앞의 몇 개 파일만 판별 (패널 지연 방지), 나머지는 드롭 후 변환 시 검증
    private const int SniffLimit = 5;

    /// <param name="Caption">버블 아래 작은 설명 (없으면 null).</param>
    /// <param name="Children">있으면 놓는 버블이 아니라, 위에 머물면 이 버블들로 펼쳐지는 묶음 버블.</param>
    public record Tile(string Target, string Label, string? Caption = null, IReadOnlyList<Tile>? Children = null);

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
            if (all.All(FileTools.Transformable.Contains)) tiles.Add(new Tile(TransformGroup, "회전", "반전", TransformTiles));
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
