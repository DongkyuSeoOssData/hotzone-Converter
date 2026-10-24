using Hotzone.Core;

namespace Hotzone.Tests;

public class OutputWriterTests
{
    [Fact]
    public void Unique_path_appends_number()
    {
        using var dir = new TempDir();
        dir.File("a.jpg", []);
        dir.File("a (1).jpg", []);
        Assert.Equal("a (2).jpg", Path.GetFileName(OutputWriter.UniquePath(Path.Combine(dir.Path, "a.jpg"))));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 2)]
    [InlineData(100, 3)]
    public void Page_number_width(int count, int width) => Assert.Equal(width, OutputWriter.PageNumberWidth(count));

    [Fact]
    public void Single_result_goes_next_to_source()
    {
        using var dir = new TempDir();
        var src = dir.File("보고서.pdf", "%PDF"u8.ToArray());
        var work = dir.Sub("work");
        var result = Path.Combine(work, "x.jpg");
        File.WriteAllBytes(result, [1]);
        Assert.Equal(Path.Combine(dir.Path, "보고서.jpg"), OutputWriter.Write(src, "jpeg", [result]));
    }

    [Fact]
    public void Multi_page_goes_to_numbered_folder()
    {
        using var dir = new TempDir();
        var src = dir.File("보고서.pdf", "%PDF"u8.ToArray());
        dir.Sub("보고서"); // 이름 충돌
        var work = dir.Sub("work");
        var pages = Enumerable.Range(0, 3).Select(i =>
        {
            var p = Path.Combine(work, $"p{i}.png");
            File.WriteAllBytes(p, []);
            return p;
        }).ToList();

        var dest = OutputWriter.Write(src, "png", pages);

        Assert.Equal("보고서 (1)", Path.GetFileName(dest));
        Assert.Equal(["보고서_01.png", "보고서_02.png", "보고서_03.png"],
            Directory.GetFiles(dest).Select(Path.GetFileName).Order());
    }
}
