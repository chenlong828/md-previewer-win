using MDPreview.Win.Shell;
using Xunit;

namespace MDPreview.Win.Tests;

/// <summary>鍙抽敭鑿滃崟璁″垝锛氭枃浠剁粍/琛ㄦ牸缁勭殑鍑虹幇鏉′欢锛屼互鍙婃枃浠跺姩浣滅殑鍙傛暟鍙ｅ緞銆?/summary>
public sealed class ContextMenuPlanTests
{
    static IReadOnlyList<string> Ids(IReadOnlyList<EditorCommand> group) =>
        group.Select(command => command.Id).ToArray();

    [Fact]
    public void Saved_markdown_outside_table_has_only_file_group()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.md", isCodeDocument: false, inTable: false);

        Assert.Single(groups);
        Assert.Equal(new[] { EditorCommands.CopyFilePathId, EditorCommands.RevealInExplorerId }, Ids(groups[0]));
    }

    [Fact]
    public void Unsaved_document_has_no_file_group()
    {
        var groups = ContextMenuPlan.Build(null, isCodeDocument: false, inTable: false);

        Assert.Empty(groups);
    }

    [Fact]
    public void Saved_markdown_inside_table_adds_insert_and_delete_groups()
    {
        var groups = ContextMenuPlan.Build(@"C:\docs\a.md", isCodeDocument: false, inTable: true);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new[] { "rowAbove", "rowBelow", "colLeft", "colRight" }, Ids(groups[1]));
        Assert.Equal(new[] { "deleteRow", "deleteColumn", "deleteTable" }, Ids(groups[2]));
        Assert.All(groups[1].Concat(groups[2]), command => Assert.True(command.RequiresTable));
    }

    [Fact]
    public void Code_document_never_gets_table_group()
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
            Assert.Equal("tableCommand", command.Op);
            Assert.False(string.IsNullOrWhiteSpace(command.OpName));
        });
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

