using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Hotzone.App;

/// <summary>완료/실패 토스트, "폴더 열기" 버튼 (설계 4.1).</summary>
public sealed class ToastNotifier
{
    public ToastNotifier()
    {
        ToastNotificationManagerCompat.OnActivated += args =>
        {
            var parsed = ToastArguments.Parse(args.Argument);
            if (parsed.TryGetValue("open", out var folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        };
    }

    public void Done(string result)
    {
        // 다중 페이지면 생성된 폴더를, 단일 파일이면 그 파일이 있는 폴더를 연다
        var folder = Directory.Exists(result) ? result : Path.GetDirectoryName(result)!;
        new ToastContentBuilder()
            .AddText("변환 완료")
            .AddText(Path.GetFileName(result))
            .AddButton(new ToastButton().SetContent("폴더 열기").AddArgument("open", folder))
            .Show();
    }

    public void Failed(string name, Exception error) =>
        new ToastContentBuilder()
            .AddText("변환 실패")
            .AddText($"{name}: {error.Message}")
            .Show();
}
