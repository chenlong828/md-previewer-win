using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MDPreview.Win.Shell;

/// <summary>
/// 命令执行（菜单栏与右键共用）：壳动作 / 直接 op / formatCommand / tableCommand 的唯一分发点。
/// </summary>
public partial class DocumentWindow
{
    void ExecuteEditorCommand(EditorCommand command)
    {
        switch (command.Kind)
        {
            case EditorCommandKind.Shell:
                ExecuteShellCommand(command.Id);
                break;
            case EditorCommandKind.Op:
                _ = RunOpAsync(command.Op!, ClonePayload(command.Payload));
                break;
            case EditorCommandKind.Format:
                var format = new JsonObject { ["name"] = command.Argument };
                if (command.Payload is not null) format["payload"] = command.Payload.DeepClone();
                _ = RunOpAsync("formatCommand", format);
                break;
            case EditorCommandKind.Table:
                _ = RunOpAsync("tableCommand", new JsonObject { ["name"] = command.Argument });
                break;
        }
    }

    static JsonObject? ClonePayload(JsonNode? payload) =>
        payload is JsonObject obj ? (JsonObject)obj.DeepClone() : null;

    void ExecuteShellCommand(string id)
    {
        switch (id)
        {
            case EditorCommands.CopyFilePathId:
                CopyDocumentPath();
                break;
            case EditorCommands.RevealInExplorerId:
                RevealDocument();
                break;
            case "cut":
                SendClipboardKey(Key.X);
                break;
            case "copy":
                SendClipboardKey(Key.C);
                break;
            case "paste":
                SendClipboardKey(Key.V);
                break;
            case "selectAll":
                SendClipboardKey(Key.A);
                break;
            case "link":
                AskForLink();
                break;
            case "mathBlock":
                _ = InsertMathBlockAsync();
                break;
            case "insertImage":
                _ = InsertImageFromDialogAsync();
                break;
            case "print":
                PrintDocument();
                break;
        }
    }

    /// <summary>
    /// 菜单栏剪贴板项：焦点在 WebView2 子窗口，WPF 路由命令到不了页面，改为菜单关闭后
    /// 把焦点还给 WebView2，再注入 Ctrl+字母（与用户按快捷键等价）。
    /// </summary>
    void SendClipboardKey(Key key)
    {
        var virtualKey = key switch
        {
            Key.X => 0x58,
            Key.C => 0x43,
            Key.V => 0x56,
            Key.A => 0x41,
            _ => 0,
        };
        if (virtualKey == 0) return;
        Dispatcher.BeginInvoke(() =>
        {
            Web.Focus();
            NativeMethods.SendCtrl(virtualKey);
        });
    }

    void AskForLink()
    {
        var href = TextPrompt.Show(this, "插入链接", "链接地址：", "https://");
        if (href is null) return;
        _ = RunOpAsync("formatCommand", new JsonObject
        {
            ["name"] = "toggleLinkCommand",
            ["payload"] = new JsonObject { ["href"] = href },
        });
    }

    async Task InsertMathBlockAsync()
    {
        await RunOpAsync("insertMarkdown", new JsonObject { ["md"] = "$$\n\n$$" });
        await RunOpAsync("focus");
    }

    void PrintDocument()
    {
        try
        {
            Web.CoreWebView2?.ShowPrintUI();
        }
        catch (Exception ex)
        {
            Log.Warn("print failed: " + ex.Message);
        }
    }

    Task PrintAsync()
    {
        PrintDocument();
        return Task.CompletedTask;
    }

    /// <summary>诊断：输出当前菜单树的条目、勾选与可用态（无界面验收菜单结构）。</summary>
    internal Task<string> DiagMenuAsync()
    {
        var lines = new List<string>();
        void Walk(ItemsControl parent, string indent)
        {
            foreach (var raw in parent.Items)
            {
                if (raw is MenuItem item)
                {
                    var check = item.IsCheckable ? (item.IsChecked ? " [x]" : " [ ]") : "";
                    var state = item.IsEnabled ? "" : " (disabled)";
                    lines.Add($"{indent}{item.Header}{check}{state}");
                    if (item.Items.Count > 0) Walk(item, indent + "  ");
                }
                else if (raw is Separator)
                {
                    lines.Add(indent + "---");
                }
            }
        }
        Walk(MainMenu, "");
        return Task.FromResult(string.Join(Environment.NewLine, lines));
    }
}
