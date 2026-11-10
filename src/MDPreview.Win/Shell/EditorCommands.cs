using System.IO;
using System.Text.Json.Nodes;

namespace MDPreview.Win.Shell;

/// <summary>命令的执行类别：壳动作 / 直接 op / formatCommand / tableCommand。</summary>
internal enum EditorCommandKind
{
    Shell,
    Op,
    Format,
    Table,
}

/// <summary>
/// 菜单与右键共用的命令定义（命令模式）：<see cref="Kind"/> 决定转发方式，
/// <see cref="Argument"/> 是内核命令名，<see cref="Payload"/> 是 formatCommand 的位置参数。
/// 新增命令只改这里；菜单栏与右键菜单都从同一份清单派生。
/// </summary>
internal sealed record EditorCommand(
    string Id,
    string Title,
    EditorCommandKind Kind,
    string? Op = null,
    string? Argument = null,
    JsonNode? Payload = null,
    bool RequiresTable = false,
    bool AvailableInCode = false,
    string? Gesture = null);

/// <summary>文档与编辑器命令的唯一来源。</summary>
internal static class EditorCommands
{
    public const string CopyFilePathId = "copyFilePath";
    public const string RevealInExplorerId = "revealInExplorer";

    // 壳侧动作（文件、剪贴板、链接、数学块、打印）
    public static readonly EditorCommand CopyFilePath = new(CopyFilePathId, "复制文件路径", EditorCommandKind.Shell);
    public static readonly EditorCommand RevealInExplorer = new(RevealInExplorerId, "在资源管理器中显示", EditorCommandKind.Shell);
    public static readonly EditorCommand Cut = new("cut", "剪切", EditorCommandKind.Shell, Gesture: "Ctrl+X");
    public static readonly EditorCommand Copy = new("copy", "拷贝", EditorCommandKind.Shell, AvailableInCode: true, Gesture: "Ctrl+C");
    public static readonly EditorCommand Paste = new("paste", "粘贴", EditorCommandKind.Shell, Gesture: "Ctrl+V");
    public static readonly EditorCommand SelectAll = new("selectAll", "全选", EditorCommandKind.Shell, AvailableInCode: true, Gesture: "Ctrl+A");
    public static readonly EditorCommand Link = new("link", "链接…", EditorCommandKind.Shell, Gesture: "Ctrl+K");
    public static readonly EditorCommand MathBlock = new("mathBlock", "数学公式块", EditorCommandKind.Shell, Gesture: "Ctrl+Alt+M");
    public static readonly EditorCommand InsertImage = new("insertImage", "插入图片…", EditorCommandKind.Shell, Gesture: "Ctrl+Shift+I");
    public static readonly EditorCommand Print = new("print", "打印…", EditorCommandKind.Shell, Gesture: "Ctrl+P");

    // 直接 op
    public static readonly EditorCommand Undo = new("undo", "撤销", EditorCommandKind.Op, "undo", Gesture: "Ctrl+Z");
    public static readonly EditorCommand Redo = new("redo", "重做", EditorCommandKind.Op, "redo", Gesture: "Ctrl+Y");
    public static readonly EditorCommand Find = new("find", "查找…", EditorCommandKind.Op, "openFindBar", AvailableInCode: true, Gesture: "Ctrl+F");
    public static readonly EditorCommand FindNext = new("findNext", "查找下一个", EditorCommandKind.Op, "findNext", AvailableInCode: true, Gesture: "Ctrl+G");
    public static readonly EditorCommand FindPrev = new("findPrev", "查找上一个", EditorCommandKind.Op, "findPrev", AvailableInCode: true, Gesture: "Ctrl+Shift+G");
    public static readonly EditorCommand ToggleSource = new("toggleSource", "源码模式", EditorCommandKind.Op, "toggleSource", Gesture: "Ctrl+/");
    public static readonly EditorCommand InsertTable = new(
        "insertTable", "插入表格", EditorCommandKind.Op, "insertTable",
        Payload: new JsonObject { ["row"] = 3, ["col"] = 3 }, Gesture: "Ctrl+Alt+T");

    // formatCommand：行内与块级格式
    public static readonly EditorCommand Bold = new("bold", "加粗", EditorCommandKind.Format, Argument: "toggleStrongCommand", Gesture: "Ctrl+B");
    public static readonly EditorCommand Italic = new("italic", "斜体", EditorCommandKind.Format, Argument: "toggleEmphasisCommand", Gesture: "Ctrl+I");
    public static readonly EditorCommand Strikethrough = new("strikethrough", "删除线", EditorCommandKind.Format, Argument: "toggleStrikethroughCommand", Gesture: "Ctrl+Shift+X");
    public static readonly EditorCommand InlineCode = new("inlineCode", "行内代码", EditorCommandKind.Format, Argument: "toggleInlineCodeCommand", Gesture: "Ctrl+Shift+C");
    public static readonly EditorCommand Heading1 = new("heading1", "标题 1", EditorCommandKind.Format, Argument: "wrapInHeadingCommand", Payload: JsonValue.Create(1), Gesture: "Ctrl+1");
    public static readonly EditorCommand Heading2 = new("heading2", "标题 2", EditorCommandKind.Format, Argument: "wrapInHeadingCommand", Payload: JsonValue.Create(2), Gesture: "Ctrl+2");
    public static readonly EditorCommand Heading3 = new("heading3", "标题 3", EditorCommandKind.Format, Argument: "wrapInHeadingCommand", Payload: JsonValue.Create(3), Gesture: "Ctrl+3");
    public static readonly EditorCommand BodyText = new("bodyText", "正文", EditorCommandKind.Format, Argument: "turnIntoTextCommand", Gesture: "Ctrl+0");
    public static readonly EditorCommand Quote = new("quote", "引用", EditorCommandKind.Format, Argument: "wrapInBlockquoteCommand", Gesture: "Ctrl+Shift+B");
    public static readonly EditorCommand BulletList = new("bulletList", "无序列表", EditorCommandKind.Format, Argument: "wrapInBulletListCommand", Gesture: "Ctrl+Shift+7");
    public static readonly EditorCommand OrderedList = new("orderedList", "有序列表", EditorCommandKind.Format, Argument: "wrapInOrderedListCommand", Gesture: "Ctrl+Shift+8");
    public static readonly EditorCommand CodeBlock = new("codeBlock", "代码块", EditorCommandKind.Format, Argument: "createCodeBlockCommand", Gesture: "Ctrl+Alt+C");
    public static readonly EditorCommand InsertHr = new("insertHr", "分割线", EditorCommandKind.Format, Argument: "insertHrCommand", Gesture: "Ctrl+Shift+-");

    // tableCommand：行列结构
    public static readonly EditorCommand RowAbove = new("rowAbove", "在上方插入行", EditorCommandKind.Table, Argument: "insertRowAbove", RequiresTable: true);
    public static readonly EditorCommand RowBelow = new("rowBelow", "在下方插入行", EditorCommandKind.Table, Argument: "insertRowBelow", RequiresTable: true);
    public static readonly EditorCommand ColLeft = new("colLeft", "在左侧插入列", EditorCommandKind.Table, Argument: "insertColLeft", RequiresTable: true);
    public static readonly EditorCommand ColRight = new("colRight", "在右侧插入列", EditorCommandKind.Table, Argument: "insertColRight", RequiresTable: true);
    public static readonly EditorCommand DeleteRow = new("deleteRow", "删除所在行", EditorCommandKind.Table, Argument: "deleteRow", RequiresTable: true);
    public static readonly EditorCommand DeleteColumn = new("deleteColumn", "删除所在列", EditorCommandKind.Table, Argument: "deleteColumn", RequiresTable: true);
    public static readonly EditorCommand DeleteTable = new("deleteTable", "删除表格", EditorCommandKind.Table, Argument: "deleteTable", RequiresTable: true);

    public static readonly IReadOnlyList<EditorCommand> All =
    [
        CopyFilePath, RevealInExplorer,
        Cut, Copy, Paste, SelectAll,
        Link, MathBlock, InsertImage, Print,
        Undo, Redo, Find, FindNext, FindPrev, ToggleSource, InsertTable,
        Bold, Italic, Strikethrough, InlineCode,
        Heading1, Heading2, Heading3, BodyText,
        Quote, BulletList, OrderedList, CodeBlock, InsertHr,
        RowAbove, RowBelow, ColLeft, ColRight, DeleteRow, DeleteColumn, DeleteTable,
    ];

    // 菜单分组（菜单栏与右键共用，顺序即展示顺序）
    public static readonly IReadOnlyList<EditorCommand> FileGroup = [CopyFilePath, RevealInExplorer];
    public static readonly IReadOnlyList<EditorCommand> UndoGroup = [Undo, Redo];
    public static readonly IReadOnlyList<EditorCommand> ClipboardGroup = [Cut, Copy, Paste, SelectAll];
    public static readonly IReadOnlyList<EditorCommand> FindGroup = [Find, FindNext, FindPrev];
    public static readonly IReadOnlyList<EditorCommand> InlineFormatGroup = [Bold, Italic, Strikethrough, InlineCode];
    public static readonly IReadOnlyList<EditorCommand> HeadingGroup = [Heading1, Heading2, Heading3, BodyText];
    public static readonly IReadOnlyList<EditorCommand> BlockGroup = [Quote, BulletList, OrderedList];
    public static readonly IReadOnlyList<EditorCommand> InsertBlockGroup = [CodeBlock, MathBlock, InsertTable];
    public static readonly IReadOnlyList<EditorCommand> TableInsertGroup = [RowAbove, RowBelow, ColLeft, ColRight];
    public static readonly IReadOnlyList<EditorCommand> TableDeleteGroup = [DeleteRow, DeleteColumn, DeleteTable];
}

/// <summary>文件级动作的参数口径（纯函数，单测覆盖）。</summary>
internal static class DocumentFileActions
{
    /// <summary>写入剪贴板的绝对路径（无 file:// 前缀）。</summary>
    public static string ClipboardText(string path) => Path.GetFullPath(path);

    /// <summary>Explorer 的定位参数：<c>/select,"C:\绝对路径"</c>。</summary>
    public static string RevealArguments(string path) => $"/select,\"{Path.GetFullPath(path)}\"";
}
