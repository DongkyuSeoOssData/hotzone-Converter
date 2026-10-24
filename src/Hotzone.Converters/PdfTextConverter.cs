using System.Text;
using Hotzone.Core;
using PDFiumCore;

namespace Hotzone.Converters;

/// <summary>PDF → 텍스트(.txt, UTF-8). 페이지 사이는 폼 피드 대신 빈 줄 두 개로 구분한다. 스캔 PDF는 빈 결과.</summary>
[Converts("pdf -> txt", Cost = 1)]
public sealed class PdfTextConverter : IConverter
{
    public string Id => "pdf.text";

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var output = Path.Combine(request.WorkDir, Path.GetFileNameWithoutExtension(request.SourcePath) + ".txt");
            var text = Pdfium.WithDocument(request.SourcePath, doc =>
            {
                var sb = new StringBuilder();
                var count = fpdfview.FPDF_GetPageCount(doc);
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (i > 0) sb.Append("\r\n\r\n");
                    sb.Append(PageText(doc, i));
                    progress?.Report((i + 1.0) / count);
                }
                return sb.ToString();
            });
            File.WriteAllText(output, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // 메모장 호환
            return new ConversionResult([output]);
        }, ct);

    private static string PageText(FpdfDocumentT doc, int index)
    {
        var page = fpdfview.FPDF_LoadPage(doc, index);
        try
        {
            var textPage = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var chars = fpdf_text.FPDFTextCountChars(textPage);
                if (chars <= 0) return "";
                var buffer = new ushort[chars + 1]; // UTF-16 + 끝 0
                var written = fpdf_text.FPDFTextGetText(textPage, 0, chars, ref buffer[0]);
                var units = new char[Math.Max(0, written - 1)];
                for (var i = 0; i < units.Length; i++) units[i] = (char)buffer[i];
                return new string(units);
            }
            finally
            {
                fpdf_text.FPDFTextClosePage(textPage);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }
}
