using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hotzone.Converters;
using Hotzone.Core;

namespace Hotzone.Cli;

/// <summary>
/// 명령줄 진입점 <c>hzc</c>. 드래그 메뉴와 같은 변환기·파일 도구를 그대로 쓰고, 결과도 같은 규칙으로 원본 옆에 만든다.
/// 종료 코드: 0 = 전부 성공, 1 = 하나 이상 실패, 2 = 잘못된 사용법.
/// </summary>
public static class CliApp
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    private static readonly Dictionary<string, ImageTransform> Rotations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["right"] = ImageTransform.RotateRight,
        ["left"] = ImageTransform.RotateLeft,
        ["180"] = ImageTransform.Rotate180,
    };

    private static readonly Dictionary<string, ImageTransform> Flips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["horizontal"] = ImageTransform.FlipHorizontal,
        ["vertical"] = ImageTransform.FlipVertical,
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 한글 경로를 \uXXXX로 바꾸지 않는다
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public const string HelpText = """
        hzc - Hotzone Converter 명령줄 도구 (결과는 항상 원본 옆에 새 파일로 만든다)

        사용법:
          hzc actions <파일...>               이 파일들로 할 수 있는 변환·도구 목록
          hzc convert <파일...> --to <형식>   형식 변환 (jpg, png, webp, avif, tiff, gif, ico, pdf, txt, mp4, mp3, wav, m4a ...)
          hzc merge <파일...>                 이미지·PDF 여러 개를 끈 순서대로 PDF 하나로 합치기
          hzc split <PDF...>                  PDF를 페이지별 PDF로 나누기
          hzc shrink <이미지...>              용량 줄이기 (긴 변 2560px, JPG·PNG·WebP·AVIF)
          hzc rotate <이미지...> --by <right|left|180>
          hzc flip <이미지...> --axis <horizontal|vertical>
          hzc zip <파일·폴더...>              ZIP 하나로 압축
          hzc unzip <ZIP...>                  압축 풀기
          hzc check                           FFmpeg·Office·LibreOffice 설치 여부
          hzc install-skill                   Claude Code 스킬을 사용자 폴더(~/.claude/skills)에 설치

        옵션:
          --json      결과를 JSON으로 출력
          --version   버전
          --          이후 인수는 모두 파일로 취급

        종료 코드: 0 성공, 1 하나 이상 실패, 2 잘못된 사용법
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ParsedArgs parsed;
        try
        {
            parsed = ParsedArgs.Parse(args);
        }
        catch (UsageException ex)
        {
            stderr.WriteLine($"오류: {ex.Message}");
            return Usage;
        }

        if (parsed.Flags.Contains("version"))
        {
            stdout.WriteLine(Version());
            return Ok;
        }
        if (parsed.Command is null or "help" || parsed.Flags.Contains("help"))
        {
            stdout.WriteLine(HelpText);
            return parsed.Command is null && !parsed.Flags.Contains("help") ? Usage : Ok;
        }

        try
        {
            return await DispatchAsync(parsed, stdout, stderr);
        }
        catch (UsageException ex)
        {
            stderr.WriteLine($"오류: {ex.Message}");
            stderr.WriteLine("도움말: hzc help");
            return Usage;
        }
    }

    private static async Task<int> DispatchAsync(ParsedArgs a, TextWriter stdout, TextWriter stderr)
    {
        var json = a.Flags.Contains("json");
        switch (a.Command)
        {
            case "check":
                return Check(stdout, json);
            case "install-skill":
                return InstallSkill(stdout, stderr, json);
            case "actions":
                return Actions(a.RequireFiles(), stdout, json);
            case "convert":
            {
                var files = a.RequireFiles();
                var target = Formats.Normalize(a.Require("to"));
                var queue = new ConversionQueue(DefaultConverters.CreateRegistry());
                return await EachAsync(files, f => queue.ConvertAsync(f, target), stdout, stderr, json);
            }
            case "merge":
            {
                var files = a.RequireFiles();
                if (!MergeService.CanMerge(files.Select(Formats.Detect).ToList()))
                    throw new UsageException("합치기는 이미지·PDF 2개 이상만 됩니다");
                return await SingleAsync(files, () => MergeService.MergeToPdf(files), stdout, stderr, json);
            }
            case "zip":
            {
                var files = a.RequireFiles(allowFolders: true);
                return await SingleAsync(files, () => FileTools.Zip(files), stdout, stderr, json);
            }
            case "split":
                return await EachAsync(a.RequireFiles(), f => Task.Run(() => FileTools.SplitPdf(f)), stdout, stderr, json);
            case "shrink":
                return await EachAsync(a.RequireFiles(), f => Task.Run(() => FileTools.Shrink(f)), stdout, stderr, json);
            case "unzip":
                return await EachAsync(a.RequireFiles(), f => Task.Run(() => FileTools.Unzip(f)), stdout, stderr, json);
            case "rotate":
            {
                var by = a.Require("by");
                if (!Rotations.TryGetValue(by, out var t)) throw new UsageException($"--by는 right, left, 180 중 하나입니다 (받은 값: {by})");
                return await EachAsync(a.RequireFiles(), f => Task.Run(() => FileTools.Transform(f, t)), stdout, stderr, json);
            }
            case "flip":
            {
                var axis = a.Require("axis");
                if (!Flips.TryGetValue(axis, out var t)) throw new UsageException($"--axis는 horizontal, vertical 중 하나입니다 (받은 값: {axis})");
                return await EachAsync(a.RequireFiles(), f => Task.Run(() => FileTools.Transform(f, t)), stdout, stderr, json);
            }
            default:
                throw new UsageException($"알 수 없는 명령: {a.Command}");
        }
    }

    /// <summary>파일마다 결과 하나. 하나가 실패해도 나머지는 계속한다. 출력 순서는 입력 순서.</summary>
    private static async Task<int> EachAsync(IReadOnlyList<string> files, Func<string, Task<string>> run, TextWriter stdout, TextWriter stderr, bool json)
    {
        var results = await Task.WhenAll(files.Select(async f =>
        {
            try { return new FileResult(f, await run(f), null); }
            catch (Exception ex) { return new FileResult(f, null, Message(ex)); }
        }));
        return Report(results, stdout, stderr, json);
    }

    /// <summary>여러 파일 → 결과 하나 (합치기, 압축).</summary>
    private static async Task<int> SingleAsync(IReadOnlyList<string> files, Func<string> run, TextWriter stdout, TextWriter stderr, bool json)
    {
        FileResult result;
        try { result = new FileResult(files[0], await Task.Run(run), null) { Sources = files }; }
        catch (Exception ex) { result = new FileResult(files[0], null, Message(ex)) { Sources = files }; }
        return Report([result], stdout, stderr, json);
    }

    private static int Report(IReadOnlyList<FileResult> results, TextWriter stdout, TextWriter stderr, bool json)
    {
        var failed = results.Count(r => r.Error is not null);
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { ok = failed == 0, results }, Json));
        }
        else
        {
            foreach (var r in results)
            {
                if (r.Output is not null) stdout.WriteLine(r.Output);
                else stderr.WriteLine($"실패: {Path.GetFileName(r.Source)}: {r.Error}");
            }
        }
        return failed == 0 ? Ok : Failed;
    }

    private static int Actions(IReadOnlyList<string> files, TextWriter stdout, bool json)
    {
        var registry = DefaultConverters.CreateRegistry();
        var anyFolder = files.Any(Directory.Exists);
        List<string> formats = anyFolder ? [] : files.Select(Formats.Detect).ToList();
        List<string> targets = anyFolder ? [] : registry.CommonTargets(formats).Select(Formats.FileExtension).Order().ToList();

        var tools = new List<string>();
        if (!anyFolder)
        {
            if (MergeService.CanMerge(formats)) tools.Add("merge");
            if (formats.All(f => f == "pdf")) tools.Add("split");
            if (formats.All(FileTools.Shrinkable.Contains)) tools.Add("shrink");
            if (formats.All(FileTools.Transformable.Contains)) tools.AddRange(["rotate", "flip"]);
            if (formats.All(f => f == "zip")) tools.Add("unzip");
        }
        tools.Add("zip");

        // PDF·오피스 → 이미지는 텍스트 선택이 안 된다 (메뉴의 "⚠ 손실" 표시와 같은 기준)
        var lossy = targets.Where(t => Formats.IsImage(Formats.Normalize(t)) && formats.Any(f => f is "pdf" or "docx" or "pptx" or "xlsx")).ToList();

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                detected = files.Select((f, i) => new { file = f, format = anyFolder ? (Directory.Exists(f) ? "folder" : Formats.Detect(f)) : formats[i] }),
                convertTo = targets,
                lossy,
                tools,
            }, Json));
        }
        else
        {
            stdout.WriteLine($"형식 변환 (convert --to): {(targets.Count == 0 ? "없음" : string.Join(", ", targets))}");
            if (lossy.Count > 0) stdout.WriteLine($"  텍스트 선택이 안 되는 결과: {string.Join(", ", lossy)}");
            stdout.WriteLine($"도구: {string.Join(", ", tools)}");
        }
        return Ok;
    }

    public const string SkillName = "hotzone-convert";

    /// <summary>
    /// hzc 옆에 함께 배포된 스킬(<c>skills\hotzone-convert</c>)을 Claude Code 사용자 스킬 폴더로 복사한다.
    /// 대상: <c>%CLAUDE_CONFIG_DIR%\skills</c>, 없으면 <c>~/.claude/skills</c>. 이미 있으면 새 버전으로 바꾼다.
    /// </summary>
    private static int InstallSkill(TextWriter stdout, TextWriter stderr, bool json)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "skills", SkillName);
        if (!File.Exists(Path.Combine(source, "SKILL.md")))
        {
            stderr.WriteLine($"실패: 함께 배포된 스킬 파일이 없습니다: {source}");
            return Failed;
        }
        var config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var dest = Path.Combine(config, "skills", SkillName);
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"실패: {ex.Message}");
            return Failed;
        }
        if (json) stdout.WriteLine(JsonSerializer.Serialize(new { ok = true, installed = dest }, Json));
        else
        {
            stdout.WriteLine(dest);
            stderr.WriteLine("설치했습니다. Claude Code를 새로 열면 파일 변환 요청에 이 스킬을 씁니다.");
        }
        return Ok;
    }

    private static int Check(TextWriter stdout, bool json)
    {
        var status = new Dictionary<string, bool>
        {
            ["ffmpeg"] = Requirements.IsMet(Requirements.FFmpeg),
            ["office"] = Requirements.IsMet(Requirements.Office),
            ["libreOffice"] = Requirements.IsMet(Requirements.LibreOffice),
        };
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { version = Version(), windows = OperatingSystem.IsWindows(), tools = status }, Json));
            return Ok;
        }
        stdout.WriteLine($"hzc {Version()}");
        stdout.WriteLine($"FFmpeg (영상·오디오): {Mark(status["ffmpeg"])}");
        stdout.WriteLine($"Microsoft Office (문서 → PDF): {Mark(status["office"])}");
        stdout.WriteLine($"LibreOffice (문서 → PDF, Office가 없을 때): {Mark(status["libreOffice"])}");
        return Ok;

        static string Mark(bool ok) => ok ? "있음" : "없음";
    }

    private static string Version()
    {
        var info = typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus < 0 ? info : info[..plus];
    }

    private static string Message(Exception ex) => ex is AggregateException { InnerException: { } inner } ? inner.Message : ex.Message;

    internal sealed record FileResult(string Source, string? Output, string? Error)
    {
        /// <summary>합치기·압축처럼 여러 입력이 결과 하나가 될 때만 채운다.</summary>
        public IReadOnlyList<string>? Sources { get; init; }
    }
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed class ParsedArgs
{
    private static readonly HashSet<string> ValueOptions = ["to", "by", "axis"];
    private static readonly HashSet<string> FlagOptions = ["json", "help", "version"];

    public string? Command { get; private init; }
    public List<string> Files { get; } = [];
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ParsedArgs Parse(IReadOnlyList<string> args)
    {
        string? command = null;
        var result = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onlyFiles = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!onlyFiles && arg == "--") { onlyFiles = true; continue; }
            if (!onlyFiles && arg is "-h" or "/?") { flags.Add("help"); continue; }
            if (!onlyFiles && arg.StartsWith("--"))
            {
                var body = arg[2..];
                string? inline = null;
                var eq = body.IndexOf('=');
                if (eq >= 0) (body, inline) = (body[..eq], body[(eq + 1)..]);
                if (FlagOptions.Contains(body)) { flags.Add(body); continue; }
                if (!ValueOptions.Contains(body)) throw new UsageException($"알 수 없는 옵션: --{body}");
                var value = inline ?? (i + 1 < args.Count ? args[++i] : throw new UsageException($"--{body} 뒤에 값이 필요합니다"));
                options[body] = value;
                continue;
            }
            if (command is null && !onlyFiles) command = arg.ToLowerInvariant();
            else result.Add(arg);
        }

        var parsed = new ParsedArgs { Command = command };
        parsed.Files.AddRange(result);
        foreach (var (k, v) in options) parsed.Options[k] = v;
        parsed.Flags.UnionWith(flags);
        return parsed;
    }

    public string Require(string option) =>
        Options.TryGetValue(option, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new UsageException($"{Command}에는 --{option} 옵션이 필요합니다");

    public IReadOnlyList<string> RequireFiles(bool allowFolders = false)
    {
        if (Files.Count == 0) throw new UsageException($"{Command}에 파일을 하나 이상 주세요");
        var full = Files.Select(Path.GetFullPath).ToList();
        foreach (var f in full)
        {
            if (File.Exists(f)) continue;
            if (Directory.Exists(f))
            {
                if (allowFolders || Command == "actions") continue;
                throw new UsageException($"폴더는 zip만 됩니다: {f}");
            }
            throw new UsageException($"파일이 없습니다: {f}");
        }
        return full;
    }
}
