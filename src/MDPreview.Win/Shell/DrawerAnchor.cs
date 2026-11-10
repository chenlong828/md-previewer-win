using System.IO;
using MDPreview.Win.Io;

namespace MDPreview.Win.Shell;

/// <summary>抽屉锚点：目录祖先链（盘符根 → 当前目录）与打开文档后的锚点归属。</summary>
internal static class DrawerAnchor
{
    public static IReadOnlyList<string> Chain(string directory)
    {
        var chain = new List<string>();
        var current = Path.GetFullPath(directory);
        while (!string.IsNullOrEmpty(current))
        {
            chain.Add(current);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>菜单里显示的目录名；盘符根（GetFileName 为空）回退为完整路径。</summary>
    public static string DisplayName(string directory)
    {
        var name = Path.GetFileName(directory);
        return string.IsNullOrEmpty(name) ? directory : name;
    }

    /// <summary>
    /// 打开文档后的抽屉锚点。与 macOS 的 <c>openDocument(at:reanchor:)</c> 同口径：
    /// 外部来源（<paramref name="reanchor"/> = true，如 Ctrl+O）以文件所在目录为重锚；
    /// 抽屉来源（false）在文件仍属当前锚点子树时保持锚点——这是「锁定祖先目录后跨多层打开」的契约。
    /// </summary>
    public static string RootAfterOpen(string? currentRoot, string path, bool reanchor)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("无法从路径得到目录：" + path, nameof(path));
        if (!reanchor && currentRoot is not null && FileTree.IsWithin(currentRoot, path)) return currentRoot;
        return directory;
    }
}
