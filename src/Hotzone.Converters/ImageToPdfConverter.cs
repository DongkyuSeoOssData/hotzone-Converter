using Hotzone.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using VipsImage = NetVips.Image;

namespace Hotzone.Converters;

/// <summary>이미지 → PDF 한 페이지 (NetVips로 디코드 → PDFsharp로 배치).</summary>
[Converts("jpeg, png, webp, heic, avif, tiff, gif, bmp, svg -> pdf", Cost = 1)]
public sealed class ImageToPdfConverter : IConverter
{
    public string Id => "image.pdfsharp";

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var output = Path.Combine(request.WorkDir, Path.GetFileNameWithoutExtension(request.SourcePath) + ".pdf");
            using var document = new PdfDocument();
            AddImagePage(document, request.SourcePath);
            document.Save(output);
            return new ConversionResult([output]);
        }, ct);

    /// <summary>이미지 크기 그대로(72dpi 기준 포인트) 페이지를 하나 추가한다.</summary>
    internal static void AddImagePage(PdfDocument document, string imagePath)
    {
        using var image = ImageConverter.Load(imagePath);
        using var ready = ImageConverter.Prepare(image, "jpeg");
        // PDFsharp가 확실히 읽는 JPEG으로 넘긴다
        using var stream = new MemoryStream(ready.JpegsaveBuffer(q: 95));
        using var ximage = XImage.FromStream(stream);
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(ximage.PixelWidth);
        page.Height = XUnit.FromPoint(ximage.PixelHeight);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(ximage, 0, 0, page.Width.Point, page.Height.Point);
    }
}
