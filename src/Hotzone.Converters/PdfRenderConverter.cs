using System.Runtime.InteropServices;
using Hotzone.Core;
using PDFiumCore;
using VipsImage = NetVips.Image;

namespace Hotzone.Converters;

/// <summary>PDF → 페이지별 이미지 (PDFium 렌더, 기본 150 DPI).</summary>
[Converts("pdf -> jpeg, png", Cost = 2)]
public sealed class PdfRenderConverter : IConverter
{
    public string Id => "pdf.pdfium";

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var dpi = int.TryParse(request.Options.GetValueOrDefault("dpi"), out var d) ? d : 150;
            var ext = Formats.FileExtension(request.TargetFormat);
            var files = Pdfium.WithDocument(request.SourcePath, doc =>
            {
                var pages = new List<string>();
                var count = fpdfview.FPDF_GetPageCount(doc);
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var output = Path.Combine(request.WorkDir, $"page_{i + 1:D5}.{ext}");
                    RenderPage(doc, i, dpi, output);
                    pages.Add(output);
                    progress?.Report((i + 1.0) / count);
                }
                return pages;
            });
            return new ConversionResult(files);
        }, ct);

    private static void RenderPage(FpdfDocumentT doc, int index, int dpi, string output)
    {
        var page = fpdfview.FPDF_LoadPage(doc, index);
        try
        {
            var width = (int)Math.Round(fpdfview.FPDF_GetPageWidthF(page) / 72.0 * dpi);
            var height = (int)Math.Round(fpdfview.FPDF_GetPageHeightF(page) / 72.0 * dpi);
            var bitmap = fpdfview.FPDFBitmapCreate(width, height, 0);
            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                const int FPDF_ANNOT = 0x01, FPDF_LCD_TEXT = 0x02;
                fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, FPDF_ANNOT | FPDF_LCD_TEXT);
                var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                var buffer = new byte[stride * height];
                Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(bitmap), buffer, 0, buffer.Length);
                // PDFium 버퍼는 BGRx → RGB로 순서를 바꿔 저장
                using var bgrx = VipsImage.NewFromMemory(buffer, width, height, 4, NetVips.Enums.BandFormat.Uchar);
                using var rgb = bgrx[2].Bandjoin(bgrx[1], bgrx[0]);
                using var srgb = rgb.Copy(interpretation: NetVips.Enums.Interpretation.Srgb);
                srgb.WriteToFile(output);
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }
}
