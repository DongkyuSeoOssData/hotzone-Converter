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
            using var image = LoadForConversion(request.SourcePath);
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

    /// <summary>
    /// 보통은 순차 접근으로 열어 메모리를 아낀다 (임의 접근은 큰 JPEG에서 피크 메모리가 두 배 넘게 는다).
    /// EXIF 회전이 있는 사진(휴대폰 세로 사진)은 순차 접근으로 열면 Autorot이 "out of order read"로 실패하므로 임의 접근으로 연다.
    /// </summary>
    internal static VipsImage LoadForConversion(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bool rotated;
        using (var header = VipsImage.NewFromBuffer(bytes)) // 헤더만 읽는다 (픽셀은 디코딩하지 않음)
            rotated = header.Contains("orientation") && header.Get("orientation") is int orientation && orientation > 1;
        return VipsImage.NewFromBuffer(bytes, access: rotated ? Enums.Access.Random : Enums.Access.Sequential);
    }

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
