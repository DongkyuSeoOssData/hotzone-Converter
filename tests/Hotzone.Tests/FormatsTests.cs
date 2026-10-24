using Hotzone.Core;

namespace Hotzone.Tests;

public class FormatsTests
{
    [Theory]
    [InlineData(".JPG", "jpeg")]
    [InlineData("jfif", "jpeg")]
    [InlineData("tif", "tiff")]
    [InlineData("pdf", "pdf")]
    public void Normalize_aliases(string ext, string expected) => Assert.Equal(expected, Formats.Normalize(ext));

    [Fact]
    public void Sniff_signatures()
    {
        Assert.Equal("png", Formats.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("pdf", Formats.Sniff("%PDF-1.7"u8));
        Assert.Equal("webp", Formats.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal("heic", Formats.Sniff([0, 0, 0, 0x18, .. "ftypheic"u8]));
        Assert.Null(Formats.Sniff("hello"u8));
    }

    [Fact]
    public void Detect_trusts_signature_over_extension()
    {
        using var dir = new TempDir();
        var fake = dir.File("photo.jpg", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);
        Assert.Equal("png", Formats.Detect(fake));
    }

    [Fact]
    public void Detect_ooxml_uses_extension()
    {
        using var dir = new TempDir();
        var deck = dir.File("deck.pptx", [.. "PK\x03\x04"u8, 0, 0, 0, 0]);
        Assert.Equal("pptx", Formats.Detect(deck));
    }
}
