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
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "webp";
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
        return sniffed;
    }
}
