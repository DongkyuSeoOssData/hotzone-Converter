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

    /// <summary>왼쪽 절반은 빨강, 오른쪽 절반은 파랑인 40×20 그림. 방향이 눈에 보이게 한다.</summary>
    private static string MakeSplitPng(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        using var left = NetVips.Image.Black(20, 20, bands: 3).Linear([1, 1, 1], [255, 0, 0]);
        using var right = NetVips.Image.Black(20, 20, bands: 3).Linear([1, 1, 1], [0, 0, 255]);
        using var joined = left.Join(right, NetVips.Enums.Direction.Horizontal);
        using var image = joined.Cast(NetVips.Enums.BandFormat.Uchar);
        image.WriteToFile(path);
        return path;
    }

    private static bool IsRed(NetVips.Image image, int x, int y) => image.Getpoint(x, y)[0] > 200;

    [Theory]
    [InlineData(ImageTransform.RotateRight, 20, 40, "a_rotated.png", 10, 5, true)]   // 시계 방향: 왼쪽이 위로
    [InlineData(ImageTransform.RotateLeft, 20, 40, "a_rotated.png", 10, 5, false)]   // 반시계: 왼쪽이 아래로
    [InlineData(ImageTransform.Rotate180, 40, 20, "a_rotated.png", 5, 10, false)]
    [InlineData(ImageTransform.FlipHorizontal, 40, 20, "a_flipped.png", 5, 10, false)]
    [InlineData(ImageTransform.FlipVertical, 40, 20, "a_flipped.png", 5, 10, true)]
    public void Transform_turns_image_and_keeps_original(ImageTransform transform, int width, int height, string name, int x, int y, bool red)
    {
        using var dir = new TempDir();
        var src = MakeSplitPng(dir.Path, "a.png");
        var before = File.ReadAllBytes(src);

        var dest = FileTools.Transform(src, transform);

        Assert.Equal(name, Path.GetFileName(dest));
        Assert.Equal(before, File.ReadAllBytes(src));
        using var result = NetVips.Image.NewFromBuffer(File.ReadAllBytes(dest));
        Assert.Equal((width, height), (result.Width, result.Height));
        Assert.Equal(red, IsRed(result, x, y));
    }

    [Fact]
    public void Transform_starts_from_what_exif_shows()
    {
        using var dir = new TempDir();
        var src = Path.Combine(dir.Path, "photo.jpg");
        using (var image = NetVips.Image.Black(40, 30, bands: 3).Linear([1, 1, 1], [10, 120, 200]).Cast(NetVips.Enums.BandFormat.Uchar))
        using (var tagged = image.Mutate(m => m.Set(NetVips.GValue.GIntType, "orientation", 6)))
            tagged.Jpegsave(src, q: 90);

        // EXIF로 30×40으로 보이는 사진을 오른쪽으로 돌리면 40×30
        using var result = NetVips.Image.NewFromBuffer(File.ReadAllBytes(FileTools.Transform(src, ImageTransform.RotateRight)));

        Assert.Equal((40, 30), (result.Width, result.Height));
        Assert.False(result.Contains("orientation") && (int)result.Get("orientation") > 1); // 이중 회전 방지
    }

    [Fact]
    public void Transform_avoids_name_collision_and_rejects_gif()
    {
        using var dir = new TempDir();
        var src = MakeSplitPng(dir.Path, "a.png");
        FileTools.Transform(src, ImageTransform.Rotate180);

        Assert.Equal("a_rotated (1).png", Path.GetFileName(FileTools.Transform(src, ImageTransform.Rotate180)));

        var gif = Path.Combine(dir.Path, "b.gif");
        using (var image = NetVips.Image.NewFromFile(src)) image.WriteToFile(gif);
        Assert.Throws<NotSupportedException>(() => FileTools.Transform(gif, ImageTransform.Rotate180));
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
