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

    private sealed class Collect : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) { lock (Values) Values.Add(value); }
    }

    [Fact]
    public async Task Progress_rises_to_one_across_pages()
    {
        using var dir = new TempDir();
        var pdf = MergeService.MergeToPdf([MakePng(dir.Path, "a.png"), MakePng(dir.Path, "b.png", 50), MakePng(dir.Path, "c.png", 90)]);
        var progress = new Collect();

        await Queue.ConvertAsync(pdf, "png", progress: progress);

        Assert.True(progress.Values.Count >= 3);
        Assert.Equal(progress.Values.Order(), progress.Values); // 줄어들지 않는다
        Assert.Equal(1.0, progress.Values[^1], 3);
    }

    [Fact]
    public void Zip_reports_progress_per_entry()
    {
        using var dir = new TempDir();
        var progress = new Collect();

        FileTools.Zip([dir.File("1.txt", [1]), dir.File("2.txt", [2])], progress);

        Assert.Equal([0.5, 1.0], progress.Values);
    }

    [Fact]
    public async Task Korean_named_pdf_to_jpeg()
    {
        using var dir = new TempDir();
        var merged = MergeService.MergeToPdf([MakePng(dir.Path, "p.png")]);
        var pdf = Path.Combine(dir.Sub("한글 폴더"), "보고서.pdf");
        File.Move(merged, pdf);

        var dest = await Queue.ConvertAsync(pdf, "jpeg");

        Assert.Equal(Path.Combine(Path.GetDirectoryName(pdf)!, "보고서.jpg"), dest);
        Assert.Equal("jpeg", Formats.Detect(dest));
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
