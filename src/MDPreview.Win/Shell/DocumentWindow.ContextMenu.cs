using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MDPreview.Win.Shell;

/// <summary>
/// 文档右键菜单（W-02）：保留 WebView2 默认编辑项，在 <c>ContextMenuRequested</c> 里追加
/// 文件级动作与表格行列组；菜单呈现前先 <c>focusAt</c>，行列命令才作用于点击的单元格。
/// </summary>
public partial class DocumentWindow
{
    /// <summary>WebView2 默认菜单里的浏览器项：编辑型应用不展示（按非本地化 Name 过滤）。</summary>
    static readonly HashSet<string> BrowserMenuItems = new(StringComparer.Ordinal)
    {
        "back", "forward", "reload", "saveAs", "saveImageAs", "print", "share", "inspect",
        "openLinkInNewWindow", "openLinkInNewTab", "openImageInNewTab", "emoji",
    };

    readonly Dictionary<string, CoreWebView2ContextMenuItem> _contextItems = new(StringComparer.Ordinal);
    CoreWebView2ContextMenuItem[] _contextSeparators = [];
    bool _contextMenuReady;

    /// <summary>在 CoreWebView2 就绪后调用一次：建自定义项、挂右键事件。</summary>
    void InitializeContextMenu(CoreWebView2 core)
    {
        if (_contextMenuReady) return;
        _contextMenuReady = true;

        foreach (var command in EditorCommands.All)
        {
            var item = core.Environment.CreateContextMenuItem(
                command.Title, null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += (sender, _) => OnContextItemSelected(sender as CoreWebView2ContextMenuItem);
            _contextItems[command.Id] = item;
        }
        // 文件组 / 插入行列组 / 删除组各一条分隔线；固定复用，避免耗尽 1000 项配额。
        _contextSeparators = Enumerable.Range(0, 3)
            .Select(_ => core.Environment.CreateContextMenuItem(
                "", null, CoreWebView2ContextMenuItemKind.Separator))
            .ToArray();

        core.ContextMenuRequested += OnContextMenuRequested;
    }

    async void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (!_contextMenuReady) return;
        using var deferral = e.GetDeferral();
        try
        {
            RemoveBrowserItems(e.MenuItems);

            var inTable = false;
            if (!IsCodeDocument && _ready && _kernel is not null)
            {
                var point = await ReadContextPointAsync();
                if (point is { } p)
                {
                    // 与 macOS 同一时序：先把选区放到右键点，再按 inTable 组菜单。
                    await _kernel.SendAsync("focusAt", new JsonObject { ["x"] = p.X, ["y"] = p.Y });
                    var info = await _kernel.SendAsync("tableInfo");
                    inTable = info?["inTable"]?.GetValue<bool>() == true;
                }
            }

            AppendGroups(e.MenuItems, ContextMenuPlan.Build(_path, IsCodeDocument, inTable));
        }
        catch (Exception ex)
        {
            Log.Warn("context menu failed: " + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>去掉浏览器专属项，并清理因此留下的首尾/连续分隔线。</summary>
    static void RemoveBrowserItems(IList<CoreWebView2ContextMenuItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (BrowserMenuItems.Contains(items[i].Name)) items.RemoveAt(i);
        }
        while (items.Count > 0 && items[items.Count - 1].Kind == CoreWebView2ContextMenuItemKind.Separator)
            items.RemoveAt(items.Count - 1);
        for (var i = items.Count - 1; i > 0; i--)
        {
            if (items[i].Kind == CoreWebView2ContextMenuItemKind.Separator
                && items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator)
                items.RemoveAt(i);
        }
    }

    void AppendGroups(IList<CoreWebView2ContextMenuItem> items, IReadOnlyList<IReadOnlyList<EditorCommand>> groups)
    {
        var separator = 0;
        foreach (var group in groups)
        {
            if (group.Count == 0) continue;
            if (items.Count > 0) items.Add(_contextSeparators[separator++]);
            foreach (var command in group) items.Add(_contextItems[command.Id]);
        }
    }

    async Task<(double X, double Y)?> ReadContextPointAsync()
    {
        if (_host is null) return null;
        var json = await _host.Core.ExecuteScriptAsync("window.__mdCtxPoint || null");
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        try
        {
            if (JsonNode.Parse(json) is JsonArray { Count: 2 } point)
                return (point[0]!.GetValue<double>(), point[1]!.GetValue<double>());
        }
        catch (Exception ex)
        {
            Log.Warn("context point parse failed: " + ex.Message);
        }
        return null;
    }

    void OnContextItemSelected(CoreWebView2ContextMenuItem? item)
    {
        if (item is null) return;
        var command = EditorCommands.All.FirstOrDefault(c =>
            _contextItems.TryGetValue(c.Id, out var candidate) && ReferenceEquals(candidate, item));
        if (command is null) return;
        Dispatcher.BeginInvoke(() => ExecuteContextCommand(command));
    }

    void ExecuteContextCommand(EditorCommand command)
    {
        switch (command.Id)
        {
            case EditorCommands.CopyFilePathId:
                CopyDocumentPath();
                break;
            case EditorCommands.RevealInExplorerId:
                RevealDocument();
                break;
            default:
                if (command.Op is not null)
                    _ = RunOpAsync(command.Op, new JsonObject { ["name"] = command.OpName });
                break;
        }
    }

    /// <summary>「复制文件路径」：只写系统剪贴板，不写文件、不产生 dirty。</summary>
    internal bool CopyDocumentPath()
    {
        if (_path is null) return false;
        Clipboard.SetText(DocumentFileActions.ClipboardText(_path));
        return true;
    }

    /// <summary>「在资源管理器中显示」：Explorer 选中当前文件；只定位，不写文件。</summary>
    internal bool RevealDocument()
    {
        if (_path is null || !File.Exists(_path)) return false;
        Process.Start(new ProcessStartInfo("explorer.exe", DocumentFileActions.RevealArguments(_path))
        {
            UseShellExecute = true,
        });
        return true;
    }
}
