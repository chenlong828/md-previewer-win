using System.IO;

namespace MDPreview.Win;

/// <summary>轻量日志：写入用户数据目录，诊断子命令与故障排查共用。</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppInfo.UserDataDirectory);
                File.AppendAllText(AppInfo.LogPath, line + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
