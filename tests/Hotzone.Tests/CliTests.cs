using System.Text.Json;
using Hotzone.Cli;

namespace Hotzone.Tests;

/// <summary>명령줄 도구 hzc: 인수 해석, 종료 코드, 결과 출력.</summary>
public class CliTests
{
    private static string MakePng(string dir, string name, int width = 40, int height = 30)
    {
        var path = Path.Combine(dir, name);
        using var image = NetVips.Image.Black(width, height, bands: 3).Linear([1, 1, 1], [255, 80, 0]).Cast(NetVips.Enums.BandFormat.Uchar);
        image.WriteToFile(path);
        return path;
    }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await CliApp.RunAsync(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task Convert_writes_next_to_source_and_prints_path()
    {
        using var dir = new TempDir();
        var png = MakePng(dir.Path, "사진.png");

        var (code, stdout, _) = await Run("convert", png, "--to", "jpg");

        Assert.Equal(CliApp.Ok, code);
        var output = stdout.Trim();
        Assert.Equal(Path.Combine(dir.Path, "사진.jpg"), output);
        Assert.True(File.Exists(output));
        Assert.True(File.Exists(png)); // 원본은 그대로
    }

    [Fact]
    public async Task Json_output_keeps_input_order_and_reports_failures()
    {
        using var dir = new TempDir();
        var a = MakePng(dir.Path, "a.png");
        var b = MakePng(dir.Path, "b.png");

        var (code, stdout, _) = await Run("convert", a, b, "--to=mp3", "--json");

        Assert.Equal(CliApp.Failed, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var results = doc.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal([a, b], results.Select(r => r.GetProperty("source").GetString()));
        Assert.All(results, r => Assert.True(r.TryGetProperty("error", out _)));
    }

    [Fact]
    public async Task Merge_and_zip_make_one_result()
    {
        using var dir = new TempDir();
        var a = MakePng(dir.Path, "a.png");
        var b = MakePng(dir.Path, "b.png");

        var (mergeCode, merged, _) = await Run("merge", a, b);
        var (zipCode, zipped, _) = await Run("zip", a, b);

        Assert.Equal(CliApp.Ok, mergeCode);
        Assert.Equal(Path.Combine(dir.Path, "a_merged.pdf"), merged.Trim());
        Assert.Equal(CliApp.Ok, zipCode);
        Assert.Equal(Path.Combine(dir.Path, "a 외 1개.zip"), zipped.Trim());
    }

    [Fact]
    public async Task Rotate_uses_same_naming_as_menu()
    {
        using var dir = new TempDir();
        var png = MakePng(dir.Path, "a.png", 40, 30);

        var (code, stdout, _) = await Run("rotate", png, "--by", "right");

        Assert.Equal(CliApp.Ok, code);
        using var rotated = NetVips.Image.NewFromFile(stdout.Trim());
        Assert.Equal((30, 40), (rotated.Width, rotated.Height));
        Assert.EndsWith("a_rotated.png", stdout.Trim());
    }

    [Fact]
    public async Task Actions_lists_formats_and_tools()
    {
        using var dir = new TempDir();
        var a = MakePng(dir.Path, "a.png");
        var b = MakePng(dir.Path, "b.png");

        var (code, stdout, _) = await Run("actions", a, b, "--json");

        Assert.Equal(CliApp.Ok, code);
        using var doc = JsonDocument.Parse(stdout);
        var targets = doc.RootElement.GetProperty("convertTo").EnumerateArray().Select(e => e.GetString()).ToList();
        var tools = doc.RootElement.GetProperty("tools").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("jpg", targets);
        Assert.DoesNotContain("png", targets); // 자기 자신으로의 변환은 숨김
        Assert.Contains("merge", tools);
        Assert.Contains("rotate", tools);
        Assert.Contains("zip", tools);
    }

    [Theory]
    [InlineData("convert")]                          // 파일 없음
    [InlineData("frobnicate", "x")]                  // 모르는 명령
    [InlineData("rotate", "--by", "up", "a.png")]    // 잘못된 값
    [InlineData("convert", "--wat")]                 // 모르는 옵션
    public async Task Bad_usage_returns_2(params string[] args)
    {
        var (code, _, stderr) = await Run(args);
        Assert.Equal(CliApp.Usage, code);
        Assert.StartsWith("오류:", stderr);
    }

    [Fact]
    public async Task Missing_file_is_usage_error_without_side_effects()
    {
        using var dir = new TempDir();
        var (code, _, stderr) = await Run("convert", Path.Combine(dir.Path, "없음.png"), "--to", "jpg");
        Assert.Equal(CliApp.Usage, code);
        Assert.Contains("파일이 없습니다", stderr);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public async Task Double_dash_treats_rest_as_files()
    {
        using var dir = new TempDir();
        var png = MakePng(dir.Path, "--json.png");

        var (code, stdout, _) = await Run("convert", "--to", "webp", "--", png);

        Assert.Equal(CliApp.Ok, code);
        Assert.EndsWith("--json.webp", stdout.Trim());
    }

    [Fact]
    public async Task Install_skill_copies_bundled_skill_into_claude_config()
    {
        using var dir = new TempDir();
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", dir.Path);
        try
        {
            var (first, stdout, _) = await Run("install-skill");
            var (again, _, _) = await Run("install-skill"); // 업데이트: 다시 설치해도 된다

            Assert.Equal(CliApp.Ok, first);
            Assert.Equal(CliApp.Ok, again);
            var skill = Path.Combine(dir.Path, "skills", CliApp.SkillName, "SKILL.md");
            Assert.Equal(Path.GetDirectoryName(skill), stdout.Trim());
            Assert.Contains("name: hotzone-convert", File.ReadAllText(skill));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
        }
    }
}
