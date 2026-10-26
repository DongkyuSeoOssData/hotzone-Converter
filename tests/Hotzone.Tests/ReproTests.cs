using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Hotzone.Converters;
using Hotzone.Core;
using NetVips;
using Xunit.Abstractions;

namespace Hotzone.Tests;

/// <summary>재현 후 고친 문제 3건의 회귀 테스트: EXIF 회전 JPEG, 범용 브랜드 m4a, UTF-8 플래그 없는 ZIP 이름.</summary>
public class ReproTests(ITestOutputHelper log)
{
    private static readonly ConversionQueue Queue = new(
        new ConverterRegistry(_ => false)
            .Register(new ImageConverter())
            .Register(new ImageToPdfConverter()),
        maxParallel: 1);

    /// <summary>휴대폰 세로 사진처럼 EXIF orientation이 붙은 JPEG. 순차 접근으로 연 뒤 Autorot한다.</summary>
    [Theory]
    [InlineData(1, "png")]
    [InlineData(3, "png")]
    [InlineData(6, "png")]
    [InlineData(8, "png")]
    [InlineData(6, "webp")]
    [InlineData(6, "tiff")]
    [InlineData(6, "pdf")]
    public async Task Exif_rotated_jpeg_converts_upright(int orientation, string target)
    {
        using var dir = new TempDir();
        var src = Path.Combine(dir.Path, "photo.jpg");
        using (var image = NetVips.Image.Black(4000, 3000, bands: 3).Linear([1, 1, 1], [10, 120, 200]).Cast(Enums.BandFormat.Uchar))
        using (var tagged = image.Mutate(m => m.Set(GValue.GIntType, "orientation", orientation)))
            tagged.Jpegsave(src, q: 90);

        var dest = await Queue.ConvertAsync(src, target);

        if (target == "pdf")
        {
            log.WriteLine($"orientation {orientation} → pdf: {new FileInfo(dest).Length} bytes");
            return;
        }
        using var result = NetVips.Image.NewFromBuffer(File.ReadAllBytes(dest));
        log.WriteLine($"orientation {orientation} → {target}: {result.Width}x{result.Height}");
        Assert.Equal(orientation is 6 or 8 ? 3000 : 4000, result.Width);
    }

    /// <summary>.m4a인데 ftyp 브랜드가 M4A가 아닌 파일 (녹음 앱 등).</summary>
    [Theory]
    [InlineData("M4A ")]
    [InlineData("isom")]
    [InlineData("mp42")]
    [InlineData("3gp4")]
    [InlineData("dash")]
    public void M4a_extension_is_detected_as_audio(string brand)
    {
        using var dir = new TempDir();
        var path = dir.File("voice.m4a", [0, 0, 0, 0x20, .. Encoding.ASCII.GetBytes("ftyp" + brand), .. new byte[20]]);

        var detected = Formats.Detect(path);

        var registry = new ConverterRegistry(_ => true).Register(new ImageConverter()).Register(new MediaConverter());
        log.WriteLine($"brand '{brand}' → {detected}, 버블: {string.Join(", ", registry.TargetsFor(detected).Order())}");
        Assert.Equal("m4a", detected);
    }

    /// <summary>UTF-8 플래그 없이 CP949로 이름을 기록한 ZIP (구형 탐색기·압축 프로그램 기본값).</summary>
    [Fact]
    public void Cp949_zip_keeps_korean_names()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "받은자료.zip");
        using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create, leaveOpen: false, Encoding.GetEncoding(949)))
        using (var writer = new StreamWriter(archive.CreateEntry("보고서.txt").Open()))
            writer.Write("x");
        Assert.False(HasUtf8Flag(zip));

        // 기본값은 시스템 OEM 코드 페이지라 CI(영어 Windows, 437)와 결과가 달라지므로 949를 지정한다
        var folder = FileTools.Unzip(zip, Encoding.GetEncoding(949));

        var names = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
        log.WriteLine($"풀린 이름: {string.Join(", ", names)} ({string.Join(" ", names[0]!.Select(c => ((int)c).ToString("X4")))})");
        Assert.Equal(["보고서.txt"], names);
    }

    /// <summary>플래그 없이 UTF-8 바이트로 이름을 쓴 ZIP (macOS·리눅스 도구). 레거시 코드 페이지로 잘못 읽지 않아야 한다.</summary>
    [Fact]
    public void Utf8_names_without_flag_stay_utf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "mac.zip");
        // Latin1은 바이트를 그대로 옮기고 UTF-8 플래그를 켜지 않는다: 이름 바이트 = "보고서.txt"의 UTF-8
        var raw = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("보고서.txt"));
        using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create, leaveOpen: false, Encoding.Latin1))
        using (var writer = new StreamWriter(archive.CreateEntry(raw).Open()))
            writer.Write("x");
        Assert.False(HasUtf8Flag(zip));

        var folder = FileTools.Unzip(zip, Encoding.GetEncoding(949));

        Assert.Equal(["보고서.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    /// <summary>
    /// CP949 이름 중 일부만 우연히 올바른 UTF-8이어도(<c>짤</c> = C2 A9 = UTF-8 <c>©</c>) ZIP 전체를 같은 인코딩으로 읽어
    /// 같은 폴더가 둘로 갈라지지 않는다 (교차 리뷰 Codex 1번).
    /// </summary>
    [Fact]
    public void Cp949_folder_is_not_split_by_accidental_utf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "짤방.zip");
        using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create, leaveOpen: false, Encoding.GetEncoding(949)))
        {
            foreach (var name in new[] { "짤/a.txt", "짤/보고서.txt" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("x");
            }
        }

        var folder = FileTools.Unzip(zip, Encoding.GetEncoding(949));

        Assert.Equal(["짤"], Directory.GetDirectories(folder).Select(Path.GetFileName));
        Assert.Equal(["a.txt", "보고서.txt"], Directory.GetFiles(Path.Combine(folder, "짤")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>기본 코드 페이지는 현재 문화권이 아니라 Windows 시스템 OEM 값을 따른다 (교차 리뷰 Codex 2번).</summary>
    [Fact]
    public void System_oem_ignores_current_culture()
    {
        if (!OperatingSystem.IsWindows()) return;
        var system = (int)GetOEMCP();
        var saved = CultureInfo.CurrentCulture;
        // 시스템 OEM과 다른 OEM 코드 페이지를 가진 문화권으로 바꾼다
        CultureInfo.CurrentCulture = new CultureInfo(system == 949 ? "en-US" : "ko-KR");
        try
        {
            Assert.NotEqual(system, CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            Assert.Equal(system, ZipNameEncoding.SystemOem().CodePage);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>첫 로컬 파일 헤더의 general purpose bit 11.</summary>
    private static bool HasUtf8Flag(string zip) => (BitConverter.ToUInt16(File.ReadAllBytes(zip), 6) & 0x800) != 0;
}
