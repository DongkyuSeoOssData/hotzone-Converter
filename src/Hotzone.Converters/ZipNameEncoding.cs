using System.Globalization;
using System.Text;

namespace Hotzone.Converters;

/// <summary>
/// UTF-8 플래그가 없는 ZIP 항목 이름 읽기용. .NET은 이런 이름을 UTF-8로만 읽어서
/// 구형 탐색기·압축 프로그램이 CP949로 쓴 한글 이름이 U+FFFD로 깨진다.
/// 항목마다 UTF-8로 엄격하게 읽어 보고, 잘못된 바이트가 있으면 지정한 코드 페이지로 읽는다 (7-Zip·반디집과 같은 방식).
/// 쓰기는 UTF-8로 한다 (FileTools.Zip은 이 클래스를 쓰지 않는다).
/// </summary>
internal sealed class ZipNameEncoding(Encoding legacy) : Encoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>현재 문화권의 OEM 코드 페이지 (한국어 949, 영어 437).</summary>
    public static Encoding SystemOem()
    {
        RegisterProvider(CodePagesEncodingProvider.Instance);
        return GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    private string Decode(byte[] bytes, int index, int count)
    {
        try { return StrictUtf8.GetString(bytes, index, count); }
        catch (DecoderFallbackException) { return legacy.GetString(bytes, index, count); }
    }

    public override string GetString(byte[] bytes, int index, int count) => Decode(bytes, index, count);

    public override int GetCharCount(byte[] bytes, int index, int count) => Decode(bytes, index, count).Length;

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        var text = Decode(bytes, byteIndex, byteCount);
        text.CopyTo(0, chars, charIndex, text.Length);
        return text.Length;
    }

    public override int GetMaxCharCount(int byteCount) =>
        Math.Max(UTF8.GetMaxCharCount(byteCount), legacy.GetMaxCharCount(byteCount));

    public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
        UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

    public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
}
