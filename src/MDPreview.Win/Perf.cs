using System.Diagnostics;

namespace MDPreview.Win;

/// <summary>
/// 启动/打开链路的阶段计时：进程相对毫秒写进日志，只做观测，不改变任何行为。
/// 打开一份文档的日志序列（t 为进程启动后的毫秒）：
/// <c>app.startup → window.loaded → webview.env → webview.ready → nav.start → kernel.boot → init.done</c>。
/// </summary>
public static class Perf
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Mark(string phase) => Log.Info($"[perf] {phase} t={Clock.ElapsedMilliseconds}ms");

    /// <summary>进程启动（exe 进入）到此刻的毫秒数；取不到进程时间时返回 -1。</summary>
    public static long SinceProcessStart()
    {
        try
        {
            var started = Process.GetCurrentProcess().StartTime;
            return (long)(DateTime.Now - started).TotalMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
