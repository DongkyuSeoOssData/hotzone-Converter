using Hotzone.Core;
using NetVips;
using VipsImage = NetVips.Image;

namespace Hotzone.Converters;

/// <summary>
/// 이미지 → Windows 아이콘(.ico). libvips에 ICO 저장기가 없어 직접 쓴다:
/// 정사각형 투명 캔버스에 맞춘 뒤 16~256px PNG들을 ICO 컨테이너에 담는다 (Vista 이상 PNG 압축 ICO).
/// </summary>
[Converts("jpeg, png, webp, avif, tiff, gif, bmp, svg -> ico", Cost = 1)]
public sealed class IcoConverter : IConverter
{
    private static readonly int[] Sizes = [256, 128, 64, 48, 32, 16];

    public string Id => "image.ico";

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var output = Path.Combine(request.WorkDir, Path.GetFileNameWithoutExtension(request.SourcePath) + ".ico");
            using var image = ImageConverter.Load(request.SourcePath);
            using var rotated = image.Autorot();
            using var rgba = rotated.HasAlpha() ? rotated.Copy() : rotated.Bandjoin(255);
            var side = Math.Max(rgba.Width, rgba.Height);
            using var square = rgba.Gravity(Enums.CompassDirection.Centre, side, side, extend: Enums.Extend.Background, background: [0, 0, 0, 0]);
            var pngs = Sizes.Where(s => s <= Math.Max(side, 16))
                .Select(s =>
                {
                    using var resized = square.Resize((double)s / side, kernel: Enums.Kernel.Lanczos3);
                    return (Size: s, Data: resized.PngsaveBuffer());
                })
                .ToList();
            Write(output, pngs);
            return new ConversionResult([output]);
        }, ct);

    private static void Write(string path, IReadOnlyList<(int Size, byte[] Data)> images)
    {
        using var stream = File.Create(path);
        Write(stream, images);
    }

    /// <summary>PNG들을 ICO 컨테이너로 쓴다 (앱 아이콘 만들기에도 쓴다).</summary>
    public static void Write(Stream stream, IReadOnlyList<(int Size, byte[] Data)> images)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 = 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);          // palette
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // planes
            writer.Write((ushort)32);       // bpp
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) writer.Write(data);
    }
}
