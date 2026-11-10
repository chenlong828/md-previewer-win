using System.IO;

namespace MDPreview.Win.Shell;

/// <summary>
/// 右键命令定义：<see cref="Op"/> 为空表示壳侧动作，否则发内核 op（参数名见 <see cref="OpName"/>）。
/// 新增命令只改这里；菜单计划与 WebView2 接线都从同一份清单派生。
/// </summary>
internal sealed record EditorCommand(
    string Id,
    string Title,
    string? Op = null,
    string? OpName = null,
    bool RequiresTable = false);

/// <summary>文档右键命令的唯一来源。</summary>
internal static class EditorCommands
{
    public const string CopyFilePathId = "copyFilePath";
    public const string RevealInExplorerId = "revealInExplorer";

    public static readonly EditorCommand CopyFilePath = new(CopyFilePathId, "复制文件路径");
    public static readonly EditorCommand RevealInExplorer = new(RevealInExplorerId, "在资源管理器中显示");

    public static readonly EditorCommand RowAbove = new("rowAbove", "在上方插入行", "tableCommand", "insertRowAbove", true);
    public static readonly EditorCommand RowBelow = new("rowBelow", "在下方插入行", "tableCommand", "insertRowBelow", true);
    public static readonly EditorCommand ColLeft = new("colLeft", "在左侧插入列", "tableCommand", "insertColLeft", true);
    public static readonly EditorCommand ColRight = new("colRight", "在右侧插入列", "tableCommand", "insertColRight", true);
    public static readonly EditorCommand DeleteRow = new("deleteRow", "删除所在行", "tableCommand", "deleteRow", true);
    public static readonly EditorCommand DeleteColumn = new("deleteColumn", "删除所在列", "tableCommand", "deleteColumn", true);
    public static readonly EditorCommand DeleteTable = new("deleteTable", "删除表格", "tableCommand", "deleteTable", true);

    public static readonly IReadOnlyList<EditorCommand> All =
    [
        CopyFilePath, RevealInExplorer,
        RowAbove, RowBelow, ColLeft, ColRight,
        DeleteRow, DeleteColumn, DeleteTable,
    ];
}

/// <summary>文件级动作的参数口径（纯函数，单测覆盖）。</summary>
internal static class DocumentFileActions
{
    /// <summary>写入剪贴板的绝对路径（无 file:// 前缀）。</summary>
    public static string ClipboardText(string path) => Path.GetFullPath(path);

    /// <summary>Explorer 的定位参数：<c>/select,"C:\绝对路径"</c>。</summary>
    public static string RevealArguments(string path) => $"/select,\"{Path.GetFullPath(path)}\"";
}
