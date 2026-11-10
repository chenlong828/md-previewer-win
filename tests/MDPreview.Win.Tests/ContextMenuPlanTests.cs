using MDPreview.Win.Shell;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>右键菜单计划：文件/格式/表格组的出现条件，以及文件动作的参数口径。</summary>
public sealed class ContextMenuPlanTests
{
    static IReadOnlyList<string> Ids(IReadOnlyList<EditorCommand> group) =>
        group.Select(command => command.Id).ToArray();

    [Fact]
    public void Saved_markdown_outside_table_has_file_and_editor_groups()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.md", isCodeDocument: false, inTable: false);

        Assert.Equal(4, groups.Count);
        Assert.Equal(new[] { EditorCommands.CopyFilePathId, EditorCommands.RevealInExplorerId }, Ids(groups[0]));
        Assert.Equal(new[] { "find", "toggleSource" }, Ids(groups[1]));
        Assert.Equal(new[] { "bold", "italic", "strikethrough", "inlineCode" }, Ids(groups[2]));
        Assert.Equal(new[] { "insertTable", "insertHr" }, Ids(groups[3]));
    }

    [Fact]
    public void Unsaved_document_has_no_file_group_but_keeps_editor_groups()
    {
        var groups = ContextMenuPlan.Build(null, isCodeDocument: false, inTable: false);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new[] { "find", "toggleSource" }, Ids(groups[0]));
        Assert.DoesNotContain(groups.SelectMany(group => group), c => c.Id == EditorCommands.CopyFilePathId);
    }

    [Fact]
    public void Saved_markdown_inside_table_adds_insert_and_delete_groups()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.md", isCodeDocument: false, inTable: true);

        Assert.Equal(6, groups.Count);
        Assert.Equal(new[] { "rowAbove", "rowBelow", "colLeft", "colRight" }, Ids(groups[4]));
        Assert.Equal(new[] { "deleteRow", "deleteColumn", "deleteTable" }, Ids(groups[5]));
        Assert.All(groups[4].Concat(groups[5]), command => Assert.True(command.RequiresTable));
    }

    [Fact]
    public void Code_document_only_gets_file_group()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.py", isCodeDocument: true, inTable: true);

        Assert.Single(groups);
        Assert.Equal(new[] { EditorCommands.CopyFilePathId, EditorCommands.RevealInExplorerId }, Ids(groups[0]));
    }

    [Fact]
    public void Table_commands_route_to_kernel_table_op()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.md", isCodeDocument: false, inTable: true);
        var commands = groups.SelectMany(group => group).Where(command => command.RequiresTable);

        Assert.All(commands, command =>
        {
            Assert.Equal(EditorCommandKind.Table, command.Kind);
            Assert.False(string.IsNullOrWhiteSpace(command.Argument));
        });
    }

    [Fact]
    public void Format_commands_carry_kernel_command_names()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.md", isCodeDocument: false, inTable: false);
        var format = groups[2];

        Assert.All(format, command => Assert.Equal(EditorCommandKind.Format, command.Kind));
        Assert.Equal("toggleStrongCommand", format[0].Argument);
    }

    [Fact]
    public void Heading_commands_carry_numeric_payload()
    {
        Assert.Equal(1, EditorCommands.Heading1.Payload!.GetValue<int>());
        Assert.Equal(3, EditorCommands.Heading3.Payload!.GetValue<int>());
    }

    [Fact]
    public void Clipboard_text_is_absolute_path_without_file_scheme()
    {
        const string path = @"C:\docs\a b\c.md";
        var text = DocumentFileActions.ClipboardText(path);

        Assert.Equal(path, text);
        Assert.DoesNotContain("file://", text);
    }

    [Fact]
    public void Reveal_arguments_select_the_file()
    {
        const string path = @"C:\docs\a b\c.md";
        var arguments = DocumentFileActions.RevealArguments(path);

        Assert.Equal("/select,\"C:\\docs\\a b\\c.md\"", arguments);
    }
}
