namespace MDPreview.Win.Shell;

/// <summary>
/// 右键菜单的纯逻辑：给定文档状态返回分组命令。不依赖 WebView2，可在单测里直接断言。
/// 分组之间由调用方插入分隔线；空分组不出现。
/// </summary>
internal static class ContextMenuPlan
{
    static readonly IReadOnlyList<EditorCommand> ContextFindGroup =
        [EditorCommands.Find, EditorCommands.ToggleSource];

    static readonly IReadOnlyList<EditorCommand> ContextInsertGroup =
        [EditorCommands.InsertTable, EditorCommands.InsertHr];

    public static IReadOnlyList<IReadOnlyList<EditorCommand>> Build(string? documentPath, bool isCodeDocument, bool inTable)
    {
        var groups = new List<IReadOnlyList<EditorCommand>>();
        // 未保存文档没有路径：不生成文件级动作，也不伪造路径。
        if (documentPath is not null) groups.Add(EditorCommands.FileGroup);
        if (isCodeDocument) return groups;

        groups.Add(ContextFindGroup);
        groups.Add(EditorCommands.InlineFormatGroup);
        groups.Add(ContextInsertGroup);
        // 表格组只在光标位于表格内时出现；代码文档整组不出现。
        if (inTable)
        {
            groups.Add(EditorCommands.TableInsertGroup);
            groups.Add(EditorCommands.TableDeleteGroup);
        }
        return groups;
    }
}
