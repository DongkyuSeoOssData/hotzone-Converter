using Hotzone.Core;
using NetVips;
using VipsImage = NetVips.Image;

namespace Hotzone.Converters;

/// <summary>이미지 ↔ 이미지 (NetVips/libvips).</summary>
[Converts("jpeg, png, webp, heic, avif, tiff, gif, bmp, svg -> jpeg, png, webp, avif, tiff, gif", Cost = 1)]
public sealed class ImageConverter : IConverter
{
    public string Id => "image.netvips";

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var output = Path.Combine(request.WorkDir,
                Path.GetFileNameWithoutExtension(request.SourcePath) + "." + Formats.FileExtension(request.TargetFormat));
            // 순차 접근으로 열면 EXIF 회전(Autorot)이 "out of order read"로 실패한다 (휴대폰 세로 사진)
            using var image = Load(request.SourcePath);
            using var ready = Prepare(image, request.TargetFormat);
            var quality = int.TryParse(request.Options.GetValueOrDefault("quality"), out var q) ? q : 92;
            switch (request.TargetFormat)
            {
                case "jpeg": ready.Jpegsave(output, q: quality); break;
                case "webp": ready.Webpsave(output, q: quality); break;
                case "avif": ready.Heifsave(output, q: quality - 30, compression: Enums.ForeignHeifCompression.Av1); break;
                default: ready.WriteToFile(output); break;
            }
            return new ConversionResult([output]);
        }, ct);

    /// <summary>
    /// 원본을 메모리로 읽어 연다. NewFromFile은 libvips 캐시가 파일 핸들을 계속 잡고 있어
    /// 변환 후에도 원본을 지우거나 옮길 수 없게 된다 (WebP에서 재현).
    /// </summary>
    internal static VipsImage Load(string path, Enums.Access access = Enums.Access.Random) =>
        VipsImage.NewFromBuffer(File.ReadAllBytes(path), access: access);

    /// <summary>JPEG은 알파가 없으니 흰 배경으로 평탄화한다.</summary>
    internal static VipsImage Prepare(VipsImage image, string targetFormat)
    {
        var rotated = image.Autorot();
        if (targetFormat == "jpeg" && rotated.HasAlpha())
        {
            var flat = rotated.Flatten(background: [255, 255, 255]);
            rotated.Dispose();
            return flat;
        }
        return rotated;
    }
}
