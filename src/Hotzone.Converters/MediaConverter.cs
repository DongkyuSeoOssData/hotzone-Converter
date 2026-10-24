using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hotzone.Core;

namespace Hotzone.Converters;

/// <summary>
/// 영상·오디오 (FFmpeg 프로세스 호출, 링크하지 않음). LGPL 빌드만 쓴다 (x264 등 GPL 없음).
/// H.264·AAC는 특허 라이선스를 갖춘 Windows 내장 Media Foundation 인코더(h264_mf, aac_mf)만 쓴다.
/// MP3(특허 만료)는 libmp3lame(LGPL), GIF·WAV는 FFmpeg 내장.
/// FFmpeg 위치: 앱 폴더 tools\ffmpeg, %LOCALAPPDATA%\HotzoneConverter\ffmpeg, PATH.
/// </summary>
// 영상 → GIF 결과를 다시 이미지 변환에 넘기면 MP4에 PNG·PDF 타일이 생기므로 체인 첫 단계로 쓰지 않는다
[Converts("mp4, mov, mkv, webm, avi -> gif", Cost = 3, Requires = Requirements.FFmpeg, Chainable = false)]
[Converts("mp4, mov, mkv, webm, avi -> mp4, mp3", Cost = 3, Requires = Requirements.FFmpeg)]
[Converts("wav, m4a, flac, ogg, mp3 -> mp3, wav, m4a", Cost = 2, Requires = Requirements.FFmpeg)]
public sealed class MediaConverter : IConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
    private static bool? _windowsEncoders;
    private static readonly Regex DurationPattern = new(@"Duration: (\d+:\d{2}:\d{2}\.\d+)");
    private static readonly Regex TimePattern = new(@"time=(\d+:\d{2}:\d{2}\.\d+)");

    public string Id => "media.ffmpeg";

    public static string? FindFfmpeg()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotzoneConverter", "ffmpeg", "ffmpeg.exe"),
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim('"'), "ffmpeg.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var ffmpeg = FindFfmpeg() ?? throw new FileNotFoundException("FFmpeg를 찾을 수 없습니다");
        var output = Path.Combine(request.WorkDir,
            Path.GetFileNameWithoutExtension(request.SourcePath) + "." + Formats.FileExtension(request.TargetFormat));
        var input = request.SourcePath;

        switch (request.TargetFormat)
        {
            case "mp4":
                // 코덱이 이미 MP4에 맞으면(H.264/AAC MOV 등) 재인코딩 없이 컨테이너만 바꾼다: 빠르고 무손실
                if (await RunAsync(ffmpeg, ["-i", input, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", output], ct, throwOnError: false, progress: progress) == 0)
                    break;
                File.Delete(output);
                await RequireWindowsEncodersAsync(ffmpeg, ct);
                await RunAsync(ffmpeg, ["-i", input, "-map", "0:v:0", "-map", "0:a?",
                    "-c:v", "h264_mf", "-b:v", "6M", "-pix_fmt", "nv12", "-c:a", "aac_mf", "-b:a", "192k",
                    "-movflags", "+faststart", output], ct, progress: progress);
                break;
            case "gif":
                // 2패스 팔레트: 최대 폭 640, 12fps, 무한 반복
                await RunAsync(ffmpeg, ["-i", input, "-vf",
                    "fps=12,scale='min(640,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4",
                    "-loop", "0", output], ct, progress: progress);
                break;
            case "mp3":
                await RunAsync(ffmpeg, ["-i", input, "-vn", "-c:a", "libmp3lame", "-q:a", "2", output], ct, progress: progress);
                break;
            case "wav":
                await RunAsync(ffmpeg, ["-i", input, "-vn", "-c:a", "pcm_s16le", output], ct, progress: progress);
                break;
            case "m4a":
                await RequireWindowsEncodersAsync(ffmpeg, ct);
                await RunAsync(ffmpeg, ["-i", input, "-vn", "-c:a", "aac_mf", "-b:a", "192k", output], ct, progress: progress);
                break;
            default:
                throw new NotSupportedException($"{request.TargetFormat} 출력은 지원하지 않습니다");
        }
        return new ConversionResult([output]);
    }

    /// <summary>
    /// H.264·AAC 인코딩은 Windows가 라이선스를 갖춘 Media Foundation 인코더만 쓴다 (특허 문제 회피).
    /// N 에디션처럼 미디어 기능이 없는 Windows에서는 대신 쓰지 않고 안내와 함께 실패한다.
    /// </summary>
    private static async Task RequireWindowsEncodersAsync(string ffmpeg, CancellationToken ct)
    {
        if (!await HasWindowsEncodersAsync(ffmpeg, ct))
            throw new NotSupportedException("Windows 미디어 인코더(H.264/AAC)를 찾을 수 없습니다. N 에디션이면 '미디어 기능 팩'을 설치하세요.");
    }

    /// <summary>FFmpeg 빌드에 Windows Media Foundation 인코더(h264_mf, aac_mf)가 있는지.</summary>
    public static async Task<bool> HasWindowsEncodersAsync(string ffmpeg, CancellationToken ct = default)
    {
        if (_windowsEncoders is { } known) return known;
        var list = new StringBuilder();
        await RunAsync(ffmpeg, ["-encoders"], ct, output: list);
        var text = list.ToString();
        return (_windowsEncoders = text.Contains(" h264_mf ") && text.Contains(" aac_mf ")).Value;
    }

    private static async Task<int> RunAsync(string ffmpeg, IEnumerable<string> args, CancellationToken ct,
        bool throwOnError = true, StringBuilder? output = null, IProgress<double>? progress = null)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-y" }.Concat(args)) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        // FFmpeg는 진행 상황을 stderr로 쏟아내므로 끝부분만 보관 (버퍼가 차서 멈추지 않게 계속 읽는다)
        var tail = new Queue<string>();
        var stderr = Task.Run(async () =>
        {
            TimeSpan? duration = null;
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
            {
                // 진행률: 입력 길이(Duration) 대비 지금까지 처리한 시간(time=)
                if (duration is null && DurationPattern.Match(line) is { Success: true } d)
                    duration = TimeSpan.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                else if (duration is { TotalSeconds: > 0 } total && TimePattern.Match(line) is { Success: true } t)
                    progress?.Report(Math.Min(1, TimeSpan.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture) / total));
                tail.Enqueue(line);
                if (tail.Count > 8) tail.Dequeue();
            }
        }, ct);
        var stdout = Task.Run(async () =>
        {
            var text = await process.StandardOutput.ReadToEndAsync(ct);
            output?.Append(text);
        }, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stderr, stdout);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg 실패 (exit {process.ExitCode}): {string.Join(" / ", tail)}");
        return process.ExitCode;
    }
}
