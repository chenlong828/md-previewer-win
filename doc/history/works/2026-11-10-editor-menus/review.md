---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序评审：菜单栏补全（W-03，P2）

## 1. 裁决摘要

blocker 0 / decision 4（已落文档与测试）/ hold 1（实机交互）。

## 2. Blocker

无。

- `dotnet build src/MDPreview.Win -c Debug` → 0 error；
- C# 单测 53 passed / 0 failed；
- `--diag menu` 在 Markdown 与代码文档下均输出完整菜单树且可用态正确。

## 3. Decision now

| 决策 | 选择 | 被否决项 | 落点 |
|---|---|---|---|
| 命令模型 | `EditorCommands` 统一 Kind=Shell/Op/Format/Table + Argument/Payload，菜单栏与右键共用 | 各菜单各写一套转发 | `EditorCommands.cs`；`ContextMenuPlan`；`DocumentWindow.Commands.cs` |
| 表格组可用性 | `selinfo.inTable` 维护，代码文档整组禁用 | 每次弹菜单前查询内核 | `OnKernelNotification` / `UpdateDocumentMenuState` |
| 剪贴板菜单项 | 菜单关闭后聚焦 WebView2 + 注入 Ctrl+字母 | JS `execCommand`（粘贴不可用）/ WPF 路由命令（到不了页面） | `SendClipboardKey` / `NativeMethods.SendCtrl` |
| 打印 | `CoreWebView2.ShowPrintUI()` | 内核 `print`（`window.print()` 在 WebView2 不弹系统对话框） | `PrintDocument` |

链接用 `TextPrompt` 收地址后传 `toggleLinkCommand` 的 `{href}`；不启用脚本对话框（安全取舍延续）。

pinning：`ContextMenuPlanTests` 覆盖分组、格式命令名与标题 payload；菜单结构由 `--diag menu` 可重复取证。

## 4. Verified sound

| 区域 | 证据 |
|---|---|
| 命令表 | 全部命令的 Kind/Argument/Payload 与内核命令名/op 对齐；53 项单测 |
| 菜单结构 | `--diag menu samples/功能演示.md`：文件/编辑/格式/视图/帮助五组，格式组含 4+4+3+3 项与表格子菜单、分割线、插入图片 |
| 代码文档 | `--diag menu <code.cs>`：格式组 disabled、源码 disabled、显示行号 `[x]` 且可用 |
| 表格门控 | 非表格光标下「表格」子菜单 disabled；`selinfo` 切换时更新 |
| 行号广播 | `App.ApplyCodeLineNumbersToAll` 遍历窗口；新窗口由 init `lineNumbers` 继承 |
| 打印/剪贴板/链接 | 代码路径已接通；交互弹窗未在无桌面会话点验 |

## 5. Hold

1. 实机点验：加粗/标题/列表/插入表格/行列/分割线/行号/打印/剪贴板/链接（登记 OQ-W13）。
2. 格式快捷键未做壳级拦截：删除线/行内代码/标题等目前只有菜单入口；Milkdown 自带加粗/斜体键位。
3. 剪贴板注入依赖前台焦点，菜单关闭后由 `Dispatcher.BeginInvoke` 还焦点；异常表现为无动作，不回滚文档。

## 6. 结论

有条件通过：自动化全绿，结构可复现取证；交互项转人工验收，不阻塞合入。
