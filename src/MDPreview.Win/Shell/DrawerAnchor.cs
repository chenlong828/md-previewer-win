using System.IO;

namespace MDPreview.Win.Shell;

/// <summary>抽屉锚点：目录祖先链（盘符根 → 当前目录），供锚点菜单使用。</summary>
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
}
