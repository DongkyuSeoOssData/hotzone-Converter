using System.IO.Compression;
using Hotzone.Converters;
using Hotzone.Core;

namespace Hotzone.Tests;

/// <summary>파일 도구(ZIP·용량·나누기)와 새 변환(텍스트·ICO·AVIF), 형식 판별 확장.</summary>
public class ToolsTests
{
    private static readonly ConversionQueue Queue = new(
        new ConverterRegistry(_ => false)
            .Register(new ImageConverter())
            .Register(new ImageToPdfConverter())
            .Register(new IcoConverter())
            .Register(new PdfRenderConverter())
            .Register(new PdfTextConverter()),
        maxParallel: 1);

    private static string MakePng(string dir, string name, int width = 40, int height = 30)
    {
        var path = Path.Combine(dir, name);
        using var image = NetVips.Image.Black(width, height, bands: 3).Linear([1, 1, 1], [255, 80, 0]).Cast(NetVips.Enums.BandFormat.Uchar);
        image.WriteToFile(path);
        return path;
    }

    [Fact]
    public void Zip_keeps_korean_names_and_folders()
    {
        using var dir = new TempDir();
        var file = dir.File("보고서.txt", "안녕"u8.ToArray());
        var folder = dir.Sub("사진 모음");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(folder, "빈 폴더"));

        var zip = FileTools.Zip([file, folder]);

        Assert.Equal("보고서 외 1개.zip", Path.GetFileName(zip));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["보고서.txt", "사진 모음/a.txt", "사진 모음/빈 폴더/"], archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Single_file_zip_then_unzip_next_to_it()
    {
        using var dir = new TempDir();
        var file = dir.File("메모.txt", "내용"u8.ToArray());

        var zip = FileTools.Zip([file]);
        Assert.Equal(Path.Combine(dir.Path, "메모.zip"), zip);

        var folder = FileTools.Unzip(zip);
        Assert.Equal(Path.Combine(dir.Path, "메모"), folder);
        Assert.Equal("내용", File.ReadAllText(Path.Combine(folder, "메모.txt")));
        // 같은 이름이 또 있으면 (1)
        Assert.Equal(Path.Combine(dir.Path, "메모 (1)"), FileTools.Unzip(zip));
    }

    [Fact]
    public void Shrink_downscales_large_images()
    {
        using var dir = new TempDir();
        var png = MakePng(dir.Path, "big.png", 4000, 1000);

        var small = FileTools.Shrink(png);

        Assert.Equal("big_small.png", Path.GetFileName(small));
        using var image = NetVips.Image.NewFromFile(small);
        Assert.Equal(2560, image.Width);
    }

    [Fact]
    public async Task Split_pdf_into_page_folder()
    {
        using var dir = new TempDir();
        var pdf = MergeService.MergeToPdf([MakePng(dir.Path, "p1.png"), MakePng(dir.Path, "p2.png")]);

        var folder = FileTools.SplitPdf(pdf);

        Assert.Equal(["p1_merged_01.pdf", "p1_merged_02.pdf"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        // 나눈 페이지도 다시 이미지로 바뀌는 정상 PDF
        Assert.Equal("png", Formats.Detect(await Queue.ConvertAsync(Directory.GetFiles(folder)[0], "png")));
    }

    [Fact]
    public async Task Pdf_to_text_and_image_to_ico_and_avif()
    {
        using var dir = new TempDir();
        var png = MakePng(dir.Path, "icon.png", 300, 200);
        var pdf = MergeService.MergeToPdf([png, MakePng(dir.Path, "b.png")]);

        var txt = await Queue.ConvertAsync(pdf, "txt");
        Assert.EndsWith(".txt", txt); // 이미지만 있는 PDF라 내용은 비어 있다

        var ico = await Queue.ConvertAsync(png, "ico");
        var header = File.ReadAllBytes(ico)[..6];
        Assert.Equal(new byte[] { 0, 0, 1, 0, 6, 0 }, header); // 256·128·64·48·32·16

        var avif = await Queue.ConvertAsync(png, "avif");
        Assert.Equal("avif", Formats.Detect(avif));
    }

    [Fact]
    public void Sniff_media_signatures()
    {
        Assert.Equal("mov", Formats.Sniff([0, 0, 0, 0x14, .. "ftypqt  "u8]));
        Assert.Equal("m4a", Formats.Sniff([0, 0, 0, 0x20, .. "ftypM4A "u8]));
        Assert.Equal("wav", Formats.Sniff("RIFF\0\0\0\0WAVEfmt "u8));
        Assert.Equal("mkv", Formats.Sniff([0x1A, 0x45, 0xDF, 0xA3, 0]));
        Assert.Equal("mp3", Formats.Sniff("ID3\x04"u8));
        Assert.Equal("flac", Formats.Sniff("fLaC"u8));
        Assert.Equal("jpeg", Formats.Sniff([0xFF, 0xD8, 0xFF, 0xE0]));
    }

    [Fact]
    public void Video_to_gif_does_not_chain_into_images()
    {
        var registry = new ConverterRegistry(_ => true).Register(new ImageConverter()).Register(new MediaConverter());

        var targets = registry.TargetsFor("mp4");

        Assert.Contains("gif", targets);
        Assert.Contains("mp3", targets);
        Assert.DoesNotContain("png", targets);
        Assert.Null(registry.FindPath("mp4", "png"));
    }
}
