using System.Runtime.CompilerServices;

namespace Hotzone.Converters;

internal static class NetVipsSetup
{
    /// <summary>
    /// libvips 연산 캐시는 소스 파일 핸들을 계속 쥐고 있어서, Windows에서는 변환 후에도
    /// 원본을 지우거나 옮길 수 없게 된다. 캐시를 끈다.
    /// </summary>
#pragma warning disable CA2255 // 라이브러리지만 어떤 진입점보다 먼저 꺼야 한다
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void DisableCache()
    {
        NetVips.Cache.Max = 0;
        NetVips.Cache.MaxFiles = 0;
    }
}
