using System.IO.Compression;
using System.Text;
using Hotzone.Core;
using NetVips;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Hotzone.Converters;

/// <summary>
/// 형식 변환이 아닌 파일 도구: ZIP 압축·풀기, 용량 줄이기, PDF 나누기.
/// 결과는 항상 원본 옆에 새로 만들고(원본 수정 없음), 이름이 겹치면 <c>(1)</c>을 붙인다.
/// 중간 실패로 반쪽 결과가 남지 않게 같은 폴더의 <c>.part</c>에 만든 뒤 이름을 바꾼다.
/// </summary>
public static class FileTools
{
    /// <summary>용량 줄이기를 지원하는 형식 (같은 형식으로 다시 저장).</summary>
    public static readonly IReadOnlySet<string> Shrinkable = new HashSet<string> { "jpeg", "png", "webp", "avif" };

    private const int ShrinkMaxSide = 2560;

    /// <summary>
    /// 파일·폴더들을 ZIP 하나로. 1개면 <c>이름.zip</c>, 여러 개면 <c>첫이름 외 N개.zip</c>.
    /// 한글 이름은 UTF-8 플래그와 함께 저장돼 탐색기·반디집·7-Zip에서 그대로 보인다.
    /// </summary>
    public static string Zip(IReadOnlyList<string> paths, IProgress<double>? progress = null)
    {
        var first = Path.GetFullPath(paths[0]).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(first)!;
        var stem = Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first);
        var name = paths.Count == 1 ? stem : $"{stem} 외 {paths.Count - 1}개";
        var dest = OutputWriter.UniquePath(Path.Combine(parent, name + ".zip"));
        var part = dest + ".part";

        // 진행률을 내려고 넣을 항목을 먼저 모은다 (파일 = 원본 경로, 빈 폴더 = null)
        var entries = new List<(string? File, string Name)>();
        foreach (var raw in paths)
        {
            var path = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar);
            var root = Path.GetDirectoryName(path)!;
            if (Directory.Exists(path)) CollectDirectory(entries, root, path);
            else entries.Add((path, Path.GetFileName(path)));
        }

        try
        {
            using (var archive = ZipFile.Open(part, ZipArchiveMode.Create))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var (file, entryName) = entries[i];
                    if (file is null) archive.CreateEntry(entryName);
                    else archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    progress?.Report((i + 1.0) / entries.Count);
                }
            }
            File.Move(part, dest);
        }
        catch
        {
            File.Delete(part);
            throw;
        }
        return dest;
    }

    private static void CollectDirectory(List<(string? File, string Name)> entries, string root, string dir)
    {
        var children = Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).ToList();
        // 빈 폴더도 남긴다
        if (children.Count == 0) entries.Add((null, EntryName(root, dir) + "/"));
        foreach (var child in children)
        {
            if (File.Exists(child)) entries.Add((child, EntryName(root, child)));
            else if (!Directory.EnumerateFileSystemEntries(child).Any()) entries.Add((null, EntryName(root, child) + "/"));
        }
    }

    private static string EntryName(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// ZIP을 원본 옆 <c>이름</c> 폴더에 푼다. 경로 탈출(../) 항목은 .NET이 막는다.
    /// UTF-8 플래그가 없는 항목 이름이 모두 UTF-8로 읽히면 UTF-8, 아니면 전부 <paramref name="legacyNames"/>로 읽는다
    /// (기본: 시스템 OEM 코드 페이지. 한국어 Windows 탐색기가 만든 ZIP은 CP949).
    /// </summary>
    public static string Unzip(string zip, Encoding? legacyNames = null)
    {
        var full = Path.GetFullPath(zip);
        var dest = OutputWriter.UniquePath(
            Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full)), isDirectory: true);
        var part = dest + ".part";
        try
        {
            ZipFile.ExtractToDirectory(full, part, ZipNameEncoding.For(full, legacyNames ?? ZipNameEncoding.SystemOem()));
            Directory.Move(part, dest);
        }
        catch
        {
            if (Directory.Exists(part)) Directory.Delete(part, recursive: true);
            throw;
        }
        return dest;
    }

    /// <summary>
    /// 같은 형식으로 다시 저장해 용량을 줄인다: 긴 변 2560px로 축소, 손실 형식은 품질 80, PNG는 256색 팔레트.
    /// 결과는 <c>이름_small.확장자</c>.
    /// </summary>
    public static string Shrink(string path)
    {
        var format = Formats.Detect(path);
        if (!Shrinkable.Contains(format)) throw new NotSupportedException($"{format}은 용량 줄이기를 지원하지 않습니다");
        var full = Path.GetFullPath(path);
        var dest = OutputWriter.UniquePath(Path.Combine(Path.GetDirectoryName(full)!,
            $"{Path.GetFileNameWithoutExtension(full)}_small.{Formats.FileExtension(format)}"));
        var part = dest + ".part";

        using var image = ImageConverter.Load(full);
        using var rotated = image.Autorot();
        var longest = Math.Max(rotated.Width, rotated.Height);
        using var sized = longest > ShrinkMaxSide ? rotated.Resize((double)ShrinkMaxSide / longest) : rotated.Copy();
        var buffer = format switch
        {
            "jpeg" => sized.JpegsaveBuffer(q: 80, optimizeCoding: true, keep: Enums.ForeignKeep.None),
            "webp" => sized.WebpsaveBuffer(q: 80, keep: Enums.ForeignKeep.None),
            "avif" => sized.HeifsaveBuffer(q: 50, compression: Enums.ForeignHeifCompression.Av1, keep: Enums.ForeignKeep.None),
            _ => sized.PngsaveBuffer(compression: 9, palette: true, keep: Enums.ForeignKeep.None),
        };
        File.WriteAllBytes(part, buffer);
        File.Move(part, dest);
        return dest;
    }

    /// <summary>PDF를 페이지별 PDF로 나눈다. 결과는 출력 규칙대로 원본 옆 <c>이름/이름_01.pdf</c> ...</summary>
    public static string SplitPdf(string path, IProgress<double>? progress = null)
    {
        var work = Path.Combine(Path.GetTempPath(), "hotzone_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var pages = new List<string>();
            using (var input = PdfReader.Open(path, PdfDocumentOpenMode.Import))
            {
                for (var i = 0; i < input.PageCount; i++)
                {
                    using var single = new PdfDocument();
                    single.AddPage(input.Pages[i]);
                    var page = Path.Combine(work, $"page_{i + 1:D5}.pdf");
                    single.Save(page);
                    pages.Add(page);
                    progress?.Report((i + 1.0) / input.PageCount);
                }
            }
            if (pages.Count < 2) throw new InvalidOperationException("한 페이지짜리 PDF는 나눌 수 없습니다");
            return OutputWriter.Write(path, "pdf", pages);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }
}
