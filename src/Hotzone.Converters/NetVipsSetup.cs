using System.Runtime.CompilerServices;

namespace Hotzone.Converters;

internal static class NetVipsSetup
{
    /// <summary>
    /// libvips 연산 캐시는 소스 파일 핸들을 계속 쥐고 있어서, Windows에서는 변환 후에도
    /// 원본을 지우거나 옮길 수 없게 된다. 캐시를 끈다.
    /// </summary>
    [ModuleInitializer]
    internal static void DisableCache()
    {
        NetVips.Cache.Max = 0;
        NetVips.Cache.MaxFiles = 0;
    }
}
