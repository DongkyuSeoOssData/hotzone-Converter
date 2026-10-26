using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Hotzone.Converters;

/// <summary>
/// UTF-8 플래그가 없는 ZIP 항목 이름의 인코딩 고르기. .NET은 이런 이름을 UTF-8로만 읽어서
/// 구형 탐색기·압축 프로그램이 CP949로 쓴 한글 이름이 U+FFFD로 깨진다.
/// 판별은 ZIP 전체 단위로 한다: 플래그 없는 이름이 모두 올바른 UTF-8이면 UTF-8, 하나라도 아니면 전부 레거시 코드 페이지.
/// 항목마다 판별하면 CP949 이름 중 우연히 UTF-8로도 읽히는 것만 다르게 풀려 같은 폴더가 둘로 갈라진다 (<c>짤/</c> → <c>©/</c>).
/// </summary>
internal static class ZipNameEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>이 ZIP의 플래그 없는 항목 이름을 읽을 인코딩.</summary>
    public static Encoding For(string zip, Encoding legacy)
    {
        var probe = new Utf8Probe();
        // 항목 이름은 중앙 디렉터리를 읽을 때 디코딩된다. 플래그 있는 항목은 .NET이 UTF-8로 읽어 probe를 거치지 않는다
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Read, probe)) _ = archive.Entries.Count;
        return probe.AllUtf8 ? Encoding.UTF8 : legacy;
    }

    /// <summary>
    /// Windows 시스템 OEM 코드 페이지 (한국어 949, 영어 437). 현재 문화권의 OEMCodePage는
    /// 지역 형식 설정을 따라가서 시스템 값과 다를 수 있다 (OEM 949 + 문화권 en-US → 437).
    /// </summary>
    public static Encoding SystemOem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = OperatingSystem.IsWindows() ? (int)GetOEMCP() : CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
        return Encoding.GetEncoding(codePage);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>받은 이름 바이트가 모두 올바른 UTF-8인지만 기록한다. 이름 자체는 버린다.</summary>
    private sealed class Utf8Probe : Encoding
    {
        public bool AllUtf8 { get; private set; } = true;

        private string Decode(byte[] bytes, int index, int count)
        {
            try { return StrictUtf8.GetString(bytes, index, count); }
            catch (DecoderFallbackException)
            {
                AllUtf8 = false;
                return Latin1.GetString(bytes, index, count);
            }
        }

        public override string GetString(byte[] bytes, int index, int count) => Decode(bytes, index, count);

        public override int GetCharCount(byte[] bytes, int index, int count) => Decode(bytes, index, count).Length;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            var text = Decode(bytes, byteIndex, byteCount);
            text.CopyTo(0, chars, charIndex, text.Length);
            return text.Length;
        }

        public override int GetMaxCharCount(int byteCount) => byteCount;

        public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
            UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

        public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
    }
}
