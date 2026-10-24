using System.IO;

namespace Hotzone.App;

/// <summary>%LOCALAPPDATA%\HotzoneConverter\hotzone.log — 드롭·변환 결과·오류 기록 (1MB 넘으면 새로 시작).</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotzoneConverter", "hotzone.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? error = null) =>
        Write("ERROR", error is null ? message : $"{message}\n{error}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}\n");
            }
        }
        catch (IOException) { } // 로그 실패가 변환을 막으면 안 된다
    }
}
