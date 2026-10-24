using System.Security.Cryptography;
using Hotzone.Converters;
using Hotzone.Core;
using NetVips;

namespace Hotzone.Tests;

/// <summary>실제 NetVips/PDFium/PDFsharp로 변환을 돌린다 (Office 제외, OS 무관).</summary>
public class ConversionTests
{
    private static readonly ConversionQueue Queue = new(
        new ConverterRegistry(_ => false)
            .Register(new ImageConverter())
            .Register(new ImageToPdfConverter())
            .Register(new PdfRenderConverter()),
        maxParallel: 1);

    private static string MakePng(string dir, string name, int g = 0)
    {
        var path = Path.Combine(dir, name);
        using var image = NetVips.Image.Black(40, 30, bands: 3).Linear([1, 1, 1], [255, g, 0]).Cast(Enums.BandFormat.Uchar);
        image.WriteToFile(path);
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task Png_to_jpeg_keeps_original()
    {
        using var dir = new TempDir();
        var src = MakePng(dir.Path, "a.png");
        var before = Hash(src);

        var dest = await Queue.ConvertAsync(src, "jpeg");

        Assert.Equal(Path.Combine(dir.Path, "a.jpg"), dest);
        Assert.Equal("jpeg", Formats.Detect(dest));
        Assert.Equal(before, Hash(src));
    }

    [Fact]
    public async Task Merged_pdf_renders_to_numbered_pages()
    {
        using var dir = new TempDir();
        var pdf = MergeService.MergeToPdf([MakePng(dir.Path, "img0.png"), MakePng(dir.Path, "img1.png", 100)]);
        Assert.Equal("img0_merged.pdf", Path.GetFileName(pdf));

        var dest = await Queue.ConvertAsync(pdf, "png");

        Assert.True(Directory.Exists(dest));
        Assert.Equal(["img0_merged_01.png", "img0_merged_02.png"], Directory.GetFiles(dest).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Webp_to_pdf()
    {
        using var dir = new TempDir();
        var webp = Path.Combine(dir.Path, "s.webp");
        using (var img = NetVips.Image.NewFromFile(MakePng(dir.Path, "s.png"))) img.WriteToFile(webp);
        File.Delete(Path.Combine(dir.Path, "s.png"));

        var dest = await Queue.ConvertAsync(webp, "pdf");

        Assert.Equal("pdf", Formats.Detect(dest));
    }
}
