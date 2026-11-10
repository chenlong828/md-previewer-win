namespace MDPreview.Win.Shell;

/// <summary>
/// 右键菜单的纯逻辑：给定文档状态返回分组命令。不依赖 WebView2，可在单测里直接断言。
/// 分组之间由调用方插入分隔线；空分组不出现。
/// </summary>
internal static class ContextMenuPlan
{
    static readonly IReadOnlyList<EditorCommand> FileGroup =
        [EditorCommands.CopyFilePath, EditorCommands.RevealInExplorer];

    static readonly IReadOnlyList<EditorCommand> InsertGroup =
        [EditorCommands.RowAbove, EditorCommands.RowBelow, EditorCommands.ColLeft, EditorCommands.ColRight];

    static readonly IReadOnlyList<EditorCommand> DeleteGroup =
        [EditorCommands.DeleteRow, EditorCommands.DeleteColumn, EditorCommands.DeleteTable];

    public static IReadOnlyList<IReadOnlyList<EditorCommand>> Build(string? documentPath, bool isCodeDocument, bool inTable)
    {
        var groups = new List<IReadOnlyList<EditorCommand>>();
        // 未保存文档没有路径：不生成文件级动作，也不伪造路径。
        if (documentPath is not null) groups.Add(FileGroup);
        // 表格组只在 Markdown 编辑面、且光标在表格内时出现；代码文档整组不出现。
        if (!isCodeDocument && inTable)
        {
            groups.Add(InsertGroup);
            groups.Add(DeleteGroup);
        }
        return groups;
    }
}
