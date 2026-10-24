using System.Reflection;

namespace Hotzone.App;

/// <summary>앱이 스스로 아는 버전 (Directory.Build.props의 Version 한 곳에서 온다).</summary>
internal static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
