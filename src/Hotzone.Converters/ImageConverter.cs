using Hotzone.Core;
using NetVips;
using VipsImage = NetVips.Image;

namespace Hotzone.Converters;

/// <summary>이미지 ↔ 이미지 (NetVips/libvips).</summary>
[Converts("jpeg, png, webp, heic, avif, tiff, gif, bmp -> jpeg, png, webp", Cost = 1)]
public sealed class ImageConverter : IConverter
{
    public string Id => "image.netvips";

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var output = Path.Combine(request.WorkDir,
                Path.GetFileNameWithoutExtension(request.SourcePath) + "." + Formats.FileExtension(request.TargetFormat));
            using var image = VipsImage.NewFromFile(request.SourcePath, access: Enums.Access.Sequential);
            using var ready = Prepare(image, request.TargetFormat);
            var quality = int.TryParse(request.Options.GetValueOrDefault("quality"), out var q) ? q : 92;
            switch (request.TargetFormat)
            {
                case "jpeg": ready.Jpegsave(output, q: quality); break;
                case "webp": ready.Webpsave(output, q: quality); break;
                default: ready.WriteToFile(output); break;
            }
            return new ConversionResult([output]);
        }, ct);

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
