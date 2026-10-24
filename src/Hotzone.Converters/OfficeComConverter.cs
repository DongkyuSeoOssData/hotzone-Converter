using System.Runtime.InteropServices;
using Hotzone.Core;

namespace Hotzone.Converters;

/// <summary>
/// MS Office COM으로 문서 → PDF, PPTX → 이미지 (Slide.Export 직접 간선). 설계 8의 Office 리스크 대응:
/// 변환 전용 새 인스턴스, DisplayAlerts 끔, 읽기 전용, 동시 실행 1개, 작업별 타임아웃.
/// interop 어셈블리 없이 late binding(dynamic)으로 부른다.
/// </summary>
[Converts("docx, pptx, xlsx -> pdf", Cost = 2, Requires = Requirements.Office)]
[Converts("pptx -> jpeg, png", Cost = 2, Requires = Requirements.Office)]
public sealed class OfficeComConverter : IConverter
{
    private const int WdFormatPdf = 17;
    private const int XlTypePdf = 0;
    private const int PpSaveAsPdf = 32;
    private const int MsoFalse = 0;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);
    private static readonly SemaphoreSlim Gate = new(1);

    public string Id => "office.com";

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<ConversionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // COM 오피스 자동화는 STA 스레드에서
            var thread = new Thread(() =>
            {
                try
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Office COM은 Windows 전용입니다");
                    tcs.SetResult(Run(request));
                }
                catch (Exception e) { tcs.SetException(e); }
            }) { IsBackground = true };
            if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            // TODO: 타임아웃 시 이 작업이 띄운 Office 프로세스(PID)만 골라 종료
            return await tcs.Task.WaitAsync(Timeout, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ConversionResult Run(ConversionRequest request)
    {
        var src = Path.GetFullPath(request.SourcePath);
        var stem = Path.GetFileNameWithoutExtension(src);
        var pdf = Path.Combine(request.WorkDir, stem + ".pdf");
        var kind = Formats.Detect(src);

        // Activator.CreateInstance는 항상 새 인스턴스를 만든다 (사용자가 열어둔 창에 붙지 않음)
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID(kind switch
        {
            "docx" => "Word.Application",
            "xlsx" => "Excel.Application",
            _ => "PowerPoint.Application",
        })!)!;
        try
        {
            switch (kind)
            {
                case "docx":
                {
                    app.DisplayAlerts = 0;
                    dynamic doc = app.Documents.Open(src, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                    doc.SaveAs2(pdf, WdFormatPdf);
                    doc.Close(false);
                    return new ConversionResult([pdf]);
                }
                case "xlsx":
                {
                    app.DisplayAlerts = false;
                    dynamic wb = app.Workbooks.Open(src, ReadOnly: true);
                    foreach (dynamic ws in wb.Worksheets)
                    {
                        // 인쇄 영역 미설정 시 잘림 방지: 시트를 한 페이지 너비에 맞춤
                        ws.PageSetup.Zoom = false;
                        ws.PageSetup.FitToPagesWide = 1;
                        ws.PageSetup.FitToPagesTall = false;
                    }
                    wb.ExportAsFixedFormat(XlTypePdf, pdf);
                    wb.Close(false);
                    return new ConversionResult([pdf]);
                }
                default:
                {
                    dynamic pres = app.Presentations.Open(src, ReadOnly: -1, Untitled: 0, WithWindow: MsoFalse);
                    try
                    {
                        if (request.TargetFormat == "pdf")
                        {
                            pres.SaveAs(pdf, PpSaveAsPdf);
                            return new ConversionResult([pdf]);
                        }
                        var width = int.TryParse(request.Options.GetValueOrDefault("width"), out var w) ? w : 1920;
                        var height = (int)Math.Round(width * (double)pres.PageSetup.SlideHeight / (double)pres.PageSetup.SlideWidth);
                        var ext = Formats.FileExtension(request.TargetFormat);
                        var files = new List<string>();
                        var count = (int)pres.Slides.Count;
                        for (var i = 1; i <= count; i++)
                        {
                            var output = Path.Combine(request.WorkDir, $"slide_{i:D5}.{ext}");
                            pres.Slides[i].Export(output, ext.ToUpperInvariant(), width, height);
                            files.Add(output);
                        }
                        return new ConversionResult(files);
                    }
                    finally
                    {
                        pres.Close();
                    }
                }
            }
        }
        finally
        {
            app.Quit();
            Marshal.FinalReleaseComObject(app);
        }
    }
}
