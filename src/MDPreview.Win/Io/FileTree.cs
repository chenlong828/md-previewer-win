using System.IO;

namespace MDPreview.Win.Io;

/// <summary>抽屉里的一行：目录或可打开的文件。</summary>
public sealed record FileEntry(string Path, string Name, bool IsDirectory);

/// <summary>
/// 文件抽屉的取数：一层目录的枚举与排序（目录在前、名称序、跳过隐藏项）。
/// 不做递归——展开时再取下一层，避免大目录一次性扫盘。
/// 另含锚点归属判定：某路径是否仍在抽屉根（锚点）子树内。
/// </summary>
public static class FileTree
{
    /// <summary>可直接在编辑器里打开的扩展名（其余只显示不可点，避免把二进制塞进内核）。</summary>
    static readonly HashSet<string> OpenableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mdx", ".txt", ".text",
        ".json", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".conf", ".env",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".svelte",
        ".css", ".scss", ".less", ".html", ".htm", ".xml", ".svg",
        ".cs", ".fs", ".vb", ".java", ".kt", ".swift", ".m", ".mm",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".rs", ".go", ".py", ".rb", ".php",
        ".lua", ".pl", ".r", ".jl", ".sql", ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd",
        ".log", ".csv", ".tsv", ".diff", ".patch",
    };

    public static bool IsOpenable(string path) => OpenableExtensions.Contains(Path.GetExtension(path));

    /// <summary>枚举一层：目录永远列出（可展开），文件限于可打开的文本类型。</summary>
    public static IReadOnlyList<FileEntry> Enumerate(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return [];

        var entries = new List<FileEntry>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(dir);
                if (IsHidden(name)) continue;
                entries.Add(new FileEntry(dir, name, true));
            }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (IsHidden(name) || !IsOpenable(file)) continue;
                entries.Add(new FileEntry(file, name, false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"drawer enumerate failed for {directory}: {ex.Message}");
        }

        return entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// <paramref name="path"/> 是否位于 <paramref name="root"/> 子树内（按目录段比较，大小写不敏感）。
    /// 只做前缀的字符串比较会把 <c>C:\work2</c> 误判进 <c>C:\work</c>，故补齐分隔符再比较。
    /// </summary>
    public static bool IsWithin(string root, string path)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return false;
        var anchor = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(path);
        if (string.Equals(full, anchor, StringComparison.OrdinalIgnoreCase)) return true;
        return full.StartsWith(anchor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>文档所在目录 → 抽屉根；无文档时退回用户目录。</summary>
    public static string RootFor(string? documentPath) =>
        documentPath is not null && Path.GetDirectoryName(documentPath) is { Length: > 0 } dir && Directory.Exists(dir)
            ? dir
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static bool IsHidden(string name) =>
        name.StartsWith('.') || name.StartsWith('$') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase);
}
