using System.IO;
using System.Reflection;

namespace MDPreview.Win;

/// <summary>运行期路径与常量：内核资源、用户设置、日志。</summary>
public static class AppInfo
{
    public const string AppName = "MDPreview";
    public const string VirtualHost = "mdpreview.local";
    public const string PageUrl = "https://mdpreview.local/index.html";

    /// <summary>本地图片的同源 URL 前缀（壳在 WebResourceRequested 里读盘应答）。</summary>
    public const string ResourcePrefix = "https://mdpreview.local/__res";

    public const int ProtocolVersion = 2;

    public static string BaseDirectory { get; } =
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;

    /// <summary>
    /// 用户数据目录：绿色版优先（exe 旁有 <c>portable.flag</c> 即用 exe/data），
    /// 其次环境变量 <c>MDP_USER_DATA</c>（测试/沙箱用），最后 %APPDATA%\MDPreview。
    /// </summary>
    public static string UserDataDirectory { get; } = ResolveUserDataDirectory();

    static string ResolveUserDataDirectory()
    {
        if (File.Exists(Path.Combine(BaseDirectory, "portable.flag")))
            return Path.Combine(BaseDirectory, "data");

        var overridden = Environment.GetEnvironmentVariable("MDP_USER_DATA");
        if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    }

    /// <summary>内核产物目录（kernel/dist → 输出目录 web/）。</summary>
    public static string WebDirectory { get; } = Path.Combine(BaseDirectory, "web");

    public static string SettingsPath { get; } = Path.Combine(UserDataDirectory, "settings.json");

    public static string LogPath { get; } = Path.Combine(UserDataDirectory, "mdpreview.log");

    /// <summary>离线运行所需的内核产物是否就位。</summary>
    public static bool HasKernel => File.Exists(Path.Combine(WebDirectory, "index.html"));
}
