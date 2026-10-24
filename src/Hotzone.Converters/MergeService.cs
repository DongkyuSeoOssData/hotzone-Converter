using Hotzone.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Hotzone.Converters;

/// <summary>N:1 합치기: 이미지 N장 → PDF 1개, PDF N개 → 병합 PDF (설계 4.4 ④). 이미지·PDF가 섞여도 끈 순서대로.</summary>
public static class MergeService
{
    private static readonly HashSet<string> ImageFormats = ["jpeg", "png", "webp", "heic", "avif", "tiff", "gif", "bmp"];

    public static bool CanMerge(IReadOnlyCollection<string> formats) =>
        formats.Count >= 2 && formats.All(f => f == "pdf" || ImageFormats.Contains(f));

    public static string MergeToPdf(IReadOnlyList<string> files)
    {
        var first = files[0];
        var dest = OutputWriter.UniquePath(Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(first))!,
            Path.GetFileNameWithoutExtension(first) + "_merged.pdf"));
        var temp = Path.Combine(Path.GetTempPath(), $"hotzone_{Guid.NewGuid():N}.pdf");

        using (var output = new PdfDocument())
        {
            foreach (var file in files)
            {
                if (Formats.Detect(file) == "pdf")
                {
                    using var input = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                    foreach (var page in input.Pages) output.AddPage(page);
                }
                else
                {
                    ImageToPdfConverter.AddImagePage(output, file);
                }
            }
            output.Save(temp);
        }
        File.Move(temp, dest);
        return dest;
    }
}
