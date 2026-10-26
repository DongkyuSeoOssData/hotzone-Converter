namespace Hotzone.Core;

/// <summary>형식 판별: 확장자는 힌트, 매직 넘버로 확정 (설계 4.4 ①).</summary>
public static class Formats
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "jpeg", ["jpeg"] = "jpeg", ["jfif"] = "jpeg", ["jpe"] = "jpeg",
        ["tif"] = "tiff", ["tiff"] = "tiff",
        ["heif"] = "heic", ["heic"] = "heic",
    };

    // OOXML(docx/pptx/xlsx)은 모두 ZIP이라 시그니처만으로 구분이 안 된다
    private static readonly HashSet<string> Ooxml = ["docx", "pptx", "xlsx"];

    /// <summary>libvips로 읽을 수 있는 이미지 (HEIC는 디코더가 없으면 변환 시 실패할 수 있다).</summary>
    public static readonly IReadOnlySet<string> Images =
        new HashSet<string> { "jpeg", "png", "webp", "heic", "avif", "tiff", "gif", "bmp", "svg" };

    public static bool IsImage(string format) => Images.Contains(format);

    public static string Normalize(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return Aliases.TryGetValue(ext, out var canonical) ? canonical : ext;
    }

    /// <summary>표시·저장용 확장자 (jpeg → jpg).</summary>
    public static string FileExtension(string format) => format == "jpeg" ? "jpg" : format;

    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "jpeg";
        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8))
        {
            if (head[8..12].SequenceEqual("WEBP"u8)) return "webp";
            if (head[8..12].SequenceEqual("WAVE"u8)) return "wav";
            if (head[8..12].SequenceEqual("AVI "u8)) return "avi";
        }
        if (head.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 })) return "mkv"; // EBML (webm은 확장자로 구분)
        if (head.StartsWith("fLaC"u8)) return "flac";
        if (head.StartsWith("OggS"u8)) return "ogg";
        if (head.StartsWith("ID3"u8) || (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)) return "mp3";
        if (head.StartsWith("%PDF-"u8)) return "pdf";
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)) return "gif";
        if (head.StartsWith("BM"u8)) return "bmp";
        if (head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8)) return "tiff";
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = System.Text.Encoding.ASCII.GetString(head[8..12]);
            return brand switch
            {
                "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1" => "heic",
                "avif" or "avis" => "avif",
                "qt  " => "mov",
                "M4A " or "M4B " => "m4a",
                _ => "mp4",
            };
        }
        if (head.StartsWith("PK\x03\x04"u8)) return "zip";
        return null;
    }

    /// <summary>파일의 실제 형식. 시그니처가 확장자와 다르면 시그니처를 믿는다.</summary>
    public static string Detect(string path)
    {
        var hint = Normalize(Path.GetExtension(path));
        Span<byte> head = stackalloc byte[32];
        int read;
        try
        {
            using var fs = File.OpenRead(path);
            read = fs.Read(head);
        }
        catch (IOException) { return hint; }
        catch (UnauthorizedAccessException) { return hint; }

        var sniffed = Sniff(head[..read]);
        if (sniffed is null) return hint;
        if (sniffed == "zip" && Ooxml.Contains(hint)) return hint;
        if (sniffed == "mkv" && hint == "webm") return hint;
        // 녹음 앱 등은 .m4a에도 범용 브랜드(isom, mp42 ...)를 써서 시그니처만으로는 영상과 구분이 안 된다
        if (sniffed == "mp4" && hint == "m4a") return hint;
        // JPEG 시그니처(FF D8 FF)가 MP3 프레임 동기 비트와 겹치므로 MP3 판정은 확장자가 맞을 때만 믿는다
        if (sniffed == "mp3" && hint != "mp3" && head[0] == 0xFF) return hint;
        return sniffed;
    }
}
