using System.IO;
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
    private SettingsWindow? _settingsWindow;
    private ToastHost? _toasts;
    private JobHistory? _history;
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 개발용 명령: 미리보기 PNG, exe 아이콘 파일 만들기
        if (e.Args is ["--render-preview", var previewDir])
        {
            PreviewRenderer.Render(previewDir);
            Shutdown();
            return;
        }
        if (e.Args is ["--make-icon", var iconPath])
        {
            TrayIcon.SaveIco(iconPath);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("처리되지 않은 UI 예외", args.Exception);
            args.Handled = true; // 트레이 앱이 통째로 죽지 않게
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("처리되지 않은 예외", args.ExceptionObject as Exception);
        Log.Info($"시작 (버전 {AppInfo.Version})");

        _settings = AppSettings.Load();
        _history = JobHistory.Load();
        var registry = DefaultConverters.CreateRegistry();
        var queue = new ConversionQueue(registry);
        var controller = new PanelController(registry);

        _hook = new InputHook();
        _indicator = new ArmIndicator();
        _panel = new PanelWindow(controller);
        _hotzone = new HotzoneWindow(() => _hook.IsArmed);
        _toasts = new ToastHost();
        // 첫 무장 때 드래그 도중 창을 처음 만드느라 늦어지지 않게 미리 그려 둔다
        _panel.Prewarm();
        _indicator.Prewarm();
        ApplySettings(_settings);

        // 훅 이벤트는 훅 스레드에서 온다 → UI 스레드로 넘긴다 (BeginInvoke: 훅 콜백을 막지 않음)
        _hook.LeftButtonChanged += down => Dispatcher.BeginInvoke(() => _hotzone.SetClickThrough(!down));
        _hook.ArmedChanged += armed => Dispatcher.BeginInvoke(() =>
        {
            Log.Info(armed ? "무장" : "무장 해제");
            var hotzoneMode = _settings.Mode == OpenMode.Hotzone;
            _indicator.SetArmed(armed && hotzoneMode); // 커서 모드는 패널이 바로 떠서 표시가 필요 없다
            if (!armed) _panel.ScheduleCollapse();
            else if (hotzoneMode && _hotzone.DragInside is { } files) _panel.OpenFor(files); // 핫존 진입 → 스페이스 순서
        });
        _hook.SpaceTapped += () => Dispatcher.BeginInvoke(() =>
        {
            if (_settings.Mode != OpenMode.Cursor) return;
            // 한 번 더 누르면 닫고, 닫힌 상태면 지금 커서 위치에 연다
            if (_panel.IsVisible) _panel.Dismiss();
            else _panel.OpenAtCursor();
        });
        _hook.SpaceIgnored += reason => Dispatcher.BeginInvoke(() => Log.Info($"스페이스 무시: {reason}"));

        _hotzone.ArmedDragOver += files => _panel.OpenFor(files);
        _hotzone.DragLeft += () => _panel.ScheduleCollapse();
        _panel.ConvertRequested += (target, files) => _ = RunAsync(queue, target, files);

        // 모니터 연결·해상도·배율이 바뀌면 다시 붙인다
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_hotzone.IsVisible) _hotzone.Place();
        });
        _hook.Start();
        _tray = CreateTray();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _hotzone!.Settings = settings;
        _panel!.Settings = settings;
        _toasts!.Settings = settings;
        _toasts.Reposition();
        StartupRegistration.Apply(settings.LaunchAtStartup);
        // 커서 모드에서는 핫존 창이 가장자리 드래그를 가로채지 않게 아예 숨긴다
        if (settings.Mode == OpenMode.Hotzone)
        {
            _hotzone.Show();
            _hotzone.Place();
        }
        else
        {
            _hotzone.Hide();
        }
        Log.Info($"설정: {settings.Mode} / {settings.Theme} / 자동 실행 {settings.LaunchAtStartup} / {settings.Monitor ?? "주 모니터"} / {settings.Position}");
    }

    /// <summary>드롭 한 번 = 알림 카드 하나. 진행률을 보여주고 끝나면 열기 버튼을 단다.</summary>
    private async Task RunAsync(ConversionQueue queue, string target, IReadOnlyList<string> files)
    {
        var action = ActionName(target);
        var detail = files.Count == 1 ? Path.GetFileName(files[0]) : $"{Path.GetFileName(files[0])} 외 {files.Count - 1}개";
        var job = new JobRecord { Glyph = PanelArt.Glyph(target), Action = action, Detail = detail };
        _history!.Add(job);
        _toasts!.Add(job);
        // Progress<T>는 만든 스레드(UI)로 돌아와 보고하므로 카드를 바로 고칠 수 있다
        var progress = new Progress<double>(job.Report);

        // N:1 동작은 결과 하나
        Func<string>? single = target switch
        {
            PanelController.MergeTile => () => MergeService.MergeToPdf(files, progress),
            PanelController.ZipTile => () => FileTools.Zip(files, progress),
            _ => null,
        };
        if (single is not null)
        {
            try
            {
                var result = await Task.Run(single);
                Log.Info($"완료: {result}");
                job.Complete([result]);
            }
            catch (Exception ex)
            {
                Log.Error($"실패: {action} ← {string.Join(", ", files)}", ex);
                job.Fail(ex.Message);
            }
            return;
        }

        // 나머지는 파일마다 각자 출력 (한 폴더에 섞지 않음). 전체 진행률 = 파일별 진행률 평균
        var parts = new double[files.Count];
        var results = new List<string>();
        var errors = new List<string>();
        await Task.WhenAll(files.Select(async (file, i) =>
        {
            var one = new Progress<double>(p => { parts[i] = p; job.Report(parts.Average()); });
            try
            {
                var result = target switch
                {
                    PanelController.UnzipTile => await Task.Run(() => FileTools.Unzip(file)),
                    PanelController.ShrinkTile => await Task.Run(() => FileTools.Shrink(file)),
                    PanelController.SplitTile => await Task.Run(() => FileTools.SplitPdf(file, one)),
                    _ => await queue.ConvertAsync(file, target, progress: one),
                };
                Log.Info($"완료: {result}");
                results.Add(result);
            }
            catch (Exception ex)
            {
                Log.Error($"실패: {file}", ex);
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }));

        if (results.Count == 0) job.Fail(errors[0]);
        else job.Complete(results, errors.Count > 0 ? $"{errors.Count}개 실패 · 로그를 확인하세요" : null);
    }

    private static string ActionName(string target) => target switch
    {
        PanelController.MergeTile => "PDF 합치기",
        PanelController.SplitTile => "PDF 나누기",
        PanelController.ShrinkTile => "용량 줄이기",
        PanelController.ZipTile => "ZIP 압축",
        PanelController.UnzipTile => "압축 풀기",
        "jpeg" => "JPG 변환",
        "webp" => "WebP 변환",
        _ => $"{target.ToUpperInvariant()} 변환",
    };

    private void OpenSettings(string page = "general")
    {
        if (_settingsWindow is not { IsLoaded: true })
        {
            _settingsWindow = new SettingsWindow(_settings, _history!);
            _settingsWindow.Changed += ApplySettings;
            _settingsWindow.Show();
        }
        _settingsWindow.ShowPage(page);
        _settingsWindow.Activate();
    }

    private Forms.NotifyIcon CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("환경설정…", null, (_, _) => OpenSettings());
        menu.Items.Add("작업 기록", null, (_, _) => OpenSettings("history"));
        menu.Items.Add("로그 폴더 열기", null, (_, _) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Log.FilePath)!);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(Log.FilePath)}\"");
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Shutdown());
        var tray = new Forms.NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = $"Hotzone Converter {AppInfo.Version} — 파일을 끌다가 스페이스를 누르세요",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => OpenSettings();
        return tray;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        base.OnExit(e);
    }
}
