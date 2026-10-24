using System.Diagnostics;
using Hotzone.Converters;
using Hotzone.Core;

namespace Hotzone.Tests;

/// <summary>FFmpeg로 실제 영상·오디오 변환. FFmpeg나 Windows 미디어 인코더가 없는 PC(서버 CI 등)에서는 아무것도 하지 않고 통과한다.</summary>
public class MediaTests
{
    private static readonly ConversionQueue Queue = new(
        new ConverterRegistry(Requirements.IsMet).Register(new MediaConverter()), maxParallel: 1);

    private static async Task<bool> Available() =>
        MediaConverter.FindFfmpeg() is { } ffmpeg && await MediaConverter.HasWindowsEncodersAsync(ffmpeg);

    /// <summary>2초짜리 테스트 화면 + 사인파 영상을 만든다.</summary>
    private static string MakeVideo(string dir, string name, string videoCodec)
    {
        var path = Path.Combine(dir, name);
        var psi = new ProcessStartInfo(MediaConverter.FindFfmpeg()!)
        {
            ArgumentList =
            {
                "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=24",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-c:v", videoCodec, "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", path,
            },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return path;
    }

    [Fact]
    public async Task Mov_to_mp4_gif_mp3()
    {
        if (!await Available()) return;
        using var dir = new TempDir();
        var mov = MakeVideo(dir.Path, "영상.mov", "h264_mf");
        Assert.Equal("mov", Formats.Detect(mov));

        Assert.Equal("mp4", Formats.Detect(await Queue.ConvertAsync(mov, "mp4"))); // H.264/AAC → 컨테이너만 바꿈
        Assert.Equal("gif", Formats.Detect(await Queue.ConvertAsync(mov, "gif")));
        Assert.Equal("mp3", Formats.Detect(await Queue.ConvertAsync(mov, "mp3")));
    }

    [Fact]
    public async Task Mkv_with_incompatible_codec_is_reencoded()
    {
        if (!await Available()) return;
        using var dir = new TempDir();
        // MPEG-4 Part 2 + AAC는 MP4에 복사도 되지만, FFV1은 안 돼서 재인코딩 경로를 탄다
        var mkv = MakeVideo(dir.Path, "clip.mkv", "ffv1");

        var mp4 = await Queue.ConvertAsync(mkv, "mp4");

        Assert.Equal("mp4", Formats.Detect(mp4));
        Assert.True(new FileInfo(mp4).Length > 1000);
    }
}
