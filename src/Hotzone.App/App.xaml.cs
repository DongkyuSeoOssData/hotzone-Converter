using System.Windows;
using Hotzone.Converters;
using Hotzone.Core;
using Hotzone.Interop;
using Forms = System.Windows.Forms;

namespace Hotzone.App;

/// <summary>트레이 상주 앱: 컴포넌트를 엮는다 (설계 4).</summary>
public partial class App : Application
{
    private InputHook? _hook;
    private Forms.NotifyIcon? _tray;
    private HotzoneWindow? _hotzone;
    private PanelWindow? _panel;
    private ArmIndicator? _indicator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var registry = DefaultConverters.CreateRegistry();
        var queue = new ConversionQueue(registry);
        var controller = new PanelController(registry);
        var notifier = new ToastNotifier();

        _hook = new InputHook();
        _indicator = new ArmIndicator();
        _panel = new PanelWindow(controller);
        _hotzone = new HotzoneWindow(() => _hook.IsArmed);

        // 훅 이벤트는 훅 스레드에서 온다 → UI 스레드로 넘긴다 (BeginInvoke: 훅 콜백을 막지 않음)
        _hook.LeftButtonChanged += down => Dispatcher.BeginInvoke(() => _hotzone.SetClickThrough(!down));
        _hook.ArmedChanged += armed => Dispatcher.BeginInvoke(() =>
        {
            _indicator.SetArmed(armed);
            if (!armed) _panel.ScheduleCollapse();
            else if (_hotzone.DragInside is { } files) _panel.OpenFor(files); // 핫존 진입 → 스페이스 순서
        });

        _hotzone.ArmedDragOver += files => _panel.OpenFor(files);
        _hotzone.DragLeft += () => _panel.ScheduleCollapse();
        _panel.ConvertRequested += (target, files) => _ = RunAsync(queue, notifier, target, files);

        _hotzone.Show();
        _hook.Start();
        _tray = CreateTray();
    }

    private static async Task RunAsync(ConversionQueue queue, ToastNotifier notifier, string target, IReadOnlyList<string> files)
    {
        if (target == PanelController.MergeTile)
        {
            try { notifier.Done(await Task.Run(() => MergeService.MergeToPdf(files))); }
            catch (Exception ex) { notifier.Failed(Path.GetFileName(files[0]), ex); }
            return;
        }
        // 파일마다 각자 출력 (한 폴더에 섞지 않음)
        await Task.WhenAll(files.Select(async file =>
        {
            try { notifier.Done(await queue.ConvertAsync(file, target)); }
            catch (Exception ex) { notifier.Failed(Path.GetFileName(file), ex); }
        }));
    }

    private Forms.NotifyIcon CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("종료", null, (_, _) => Shutdown());
        return new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Hotzone Converter — 파일을 끌다가 스페이스를 탭하고 오른쪽 가장자리로",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
