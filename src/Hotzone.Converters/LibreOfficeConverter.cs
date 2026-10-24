using System.Diagnostics;
using Hotzone.Core;

namespace Hotzone.Converters;

/// <summary>Office가 없고 LibreOffice가 설치된 경우의 대체 경로 (번들하지 않음).</summary>
[Converts("docx, pptx, xlsx -> pdf", Cost = 4, Requires = Requirements.LibreOffice)]
public sealed class LibreOfficeConverter : IConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public string Id => "office.libreoffice";

    public static string? FindSoffice()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [@"C:\Program Files\LibreOffice\program\soffice.exe", @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"]
            : ["/usr/bin/soffice", "/usr/local/bin/soffice"];
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(FindSoffice() ?? throw new FileNotFoundException("LibreOffice를 찾을 수 없습니다"))
        {
            ArgumentList = { "--headless", "--convert-to", "pdf", "--outdir", request.WorkDir, request.SourcePath },
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var output = Path.Combine(request.WorkDir, Path.GetFileNameWithoutExtension(request.SourcePath) + ".pdf");
        if (!File.Exists(output)) throw new InvalidOperationException($"LibreOffice 변환 실패 (exit {process.ExitCode})");
        return new ConversionResult([output]);
    }
}
