using System.IO;
using MDPreview.Win.Shell;
using Microsoft.Win32;

namespace MDPreview.Win.Io;

/// <summary>
/// 用户级（HKCU）Markdown 文件关联：只写 <c>HKCU\Software\Classes</c>，不需要管理员。
/// 安装时把原默认值备份到 <c>HKCU\Software\MDPreview\AssociationBackup</c>，卸载时还原，
/// 不劫持用户原有的编辑器选择。
/// </summary>
public static class FileAssociation
{
    public const string ProgId = "MDPreview.Markdown";
    public const string ProgIdLabel = "Markdown 文档";

    /// <summary>参与关联的扩展名（与 MarkdownExtensions 对齐）。</summary>
    public static readonly string[] Extensions = [".md", ".markdown", ".mdown", ".mkd", ".mdx"];

    const string ClassesRoot = @"Software\Classes";
    const string BackupRoot = @"Software\MDPreview\AssociationBackup";

    public static string ExecutablePath =>
        Path.Combine(AppInfo.BaseDirectory, "MDPreview.exe");

    /// <summary>扩展名 → 当前默认 ProgId；未设置返回空串。</summary>
    public static string CurrentProgId(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{extension}");
        return key?.GetValue(null) as string ?? "";
    }

    public static bool IsInstalled =>
        Extensions.All(ext => string.Equals(CurrentProgId(ext), ProgId, StringComparison.OrdinalIgnoreCase))
        && Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ProgId}\shell\open\command") is not null;

    /// <summary>写入关联；返回每个扩展名的结果描述。</summary>
    public static IReadOnlyList<string> Install()
    {
        var exe = ExecutablePath;
        if (!File.Exists(exe)) throw new FileNotFoundException("找不到 MDPreview.exe", exe);

        try
        {
            return InstallCore(exe);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "写 HKCU\\Software\\Classes 被拒绝（沙箱/受限令牌下会出现）。" +
                "请在普通用户会话里运行，或改用 tools\\associate.ps1 -Install。" +
                " 原始信息：" + ex.Message, ex);
        }
    }

    static IReadOnlyList<string> InstallCore(string exe)
    {
        var report = new List<string>();
        using (var progId = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}"))
        {
            progId.SetValue(null, ProgIdLabel);
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{exe}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{exe}\" \"%1\"");
        }

        foreach (var ext in Extensions)
        {
            // 只在首次安装时备份原默认值；重复安装不能把自身 ProgId 当成「原值」备份
            using (var existing = Registry.CurrentUser.OpenSubKey($@"{BackupRoot}\{ext}"))
            {
                if (existing is null)
                {
                    var previousDefault = CurrentProgId(ext);
                    using var backup = Registry.CurrentUser.CreateSubKey($@"{BackupRoot}\{ext}");
                    backup.SetValue("Default", previousDefault);
                }
            }
            var previous = (Registry.CurrentUser.OpenSubKey($@"{BackupRoot}\{ext}")?.GetValue("Default") as string) ?? "";

            using var extKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}");
            extKey.SetValue(null, ProgId);
            using var openWith = extKey.CreateSubKey("OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

            report.Add($"{ext}: {(previous.Length == 0 ? "(无)" : previous)} -> {ProgId}");
        }

        NotifyShell();
        return report;
    }

    /// <summary>撤销关联并还原安装前的默认值。</summary>
    public static IReadOnlyList<string> Uninstall()
    {
        var report = new List<string>();
        foreach (var ext in Extensions)
        {
            using (var backup = Registry.CurrentUser.OpenSubKey($@"{BackupRoot}\{ext}"))
            {
                var previous = backup?.GetValue("Default") as string ?? "";
                using var extKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}");
                if (string.Equals(CurrentProgId(ext), ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    if (previous.Length > 0 && !string.Equals(previous, ProgId, StringComparison.OrdinalIgnoreCase))
                        extKey.SetValue(null, previous);
                    else
                        extKey.DeleteValue(null!, throwOnMissingValue: false);
                }
                using (var openWith = extKey.OpenSubKey("OpenWithProgids", writable: true))
                    openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                report.Add($"{ext}: -> {(previous.Length == 0 ? "(无)" : previous)}");
            }
            Registry.CurrentUser.DeleteSubKeyTree($@"{BackupRoot}\{ext}", throwOnMissingSubKey: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{ProgId}", throwOnMissingSubKey: false);
        NotifyShell();
        return report;
    }

    public static IReadOnlyList<string> Status()
    {
        var lines = new List<string>
        {
            $"exe      {ExecutablePath} (exists={File.Exists(ExecutablePath)})",
            $"progId   {ProgId} (registered={Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ProgId}") is not null})",
            $"default  {(IsInstalled ? "MDPreview" : "other/none")}",
        };
        foreach (var ext in Extensions)
        {
            var current = CurrentProgId(ext);
            using var backup = Registry.CurrentUser.OpenSubKey($@"{BackupRoot}\{ext}");
            var previous = backup?.GetValue("Default") as string ?? "";
            lines.Add($"{ext,-11} current={(current.Length == 0 ? "(无)" : current)}  backup={(previous.Length == 0 ? "(无)" : previous)}");
        }
        return lines;
    }

    /// <summary>通知 Shell 刷新图标/关联（SHCNE_ASSOCCHANGED）。</summary>
    static void NotifyShell()
    {
        try
        {
            NativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn("SHChangeNotify failed: " + ex.Message);
        }
    }
}
