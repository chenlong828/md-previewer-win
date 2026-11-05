using System.IO;

namespace MDPreview.Win.Tests;

/// <summary>
/// 单元测试进程的数据目录：必须在任何 AppInfo 访问之前落到仓库内（%TEMP% 在受限环境下不可写），
/// 否则测试会写进真实的 %APPDATA%\MDPreview。
/// </summary>
internal static class TestBootstrap
{
    internal static readonly string DataDirectory = ResolveDataDirectory();

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(DataDirectory);
        Environment.SetEnvironmentVariable("MDP_USER_DATA", DataDirectory);
    }

    static string ResolveDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "kernel")))
            dir = dir.Parent;

        var root = dir?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(root, "artifacts", "test-data", Guid.NewGuid().ToString("N"));
    }

    internal static string NewCaseDirectory(string name)
    {
        var path = Path.Combine(DataDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
