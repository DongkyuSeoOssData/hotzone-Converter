using PDFiumCore;

namespace Hotzone.Converters;

/// <summary>PDFium은 스레드 안전하지 않다: 초기화와 모든 호출을 한 잠금 아래에서 한다.</summary>
internal static class Pdfium
{
    private static readonly object Lock = new();
    private static bool _initialized;

    /// <summary>문서를 열어 <paramref name="work"/>를 실행하고 닫는다.</summary>
    public static T WithDocument<T>(string path, Func<FpdfDocumentT, T> work)
    {
        lock (Lock)
        {
            if (!_initialized)
            {
                fpdfview.FPDF_InitLibrary();
                _initialized = true;
            }
            var doc = fpdfview.FPDF_LoadDocument(path, null)
                ?? throw new InvalidDataException($"PDF를 열 수 없습니다: {Path.GetFileName(path)}");
            try { return work(doc); }
            finally { fpdfview.FPDF_CloseDocument(doc); }
        }
    }
}
