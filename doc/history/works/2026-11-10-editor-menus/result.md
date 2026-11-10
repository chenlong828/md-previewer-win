---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序结果：菜单栏补全（W-03，P2）

## 1. 结论

Windows 菜单栏从「文件/编辑/视图/帮助四组少量项」补齐到与 macOS 对齐：

- 编辑：撤销/重做、剪切/拷贝/粘贴/全选、查找/查找下一个/查找上一个；
- 格式（新）：加粗/斜体/删除线/行内代码/链接；标题 1–3/正文；引用/无序/有序列表；代码块/数学公式块/插入表格；表格子菜单；分割线；插入图片；
- 视图：源码模式、显示行号（代码文档可用、全局偏好跨窗口广播）、缩略图、抽屉、语言、主题；
- 文件：打印（浏览器打印面）；
- 右键：在文件/表格组之外补查找/源码、行内格式、插入表格/分割线组。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/Shell/EditorCommands.cs` | 命令模型扩展为 Kind=Shell/Op/Format/Table + Argument/Payload，登记全部菜单/右键命令 |
| `src/MDPreview.Win/Shell/ContextMenuPlan.cs` | Markdown 右键增加查找/源码、行内格式、插入组 |
| `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs` | 菜单重构为构建方法；表格/行号状态；`selinfo`；打印/查找快捷键分支 |
| `src/MDPreview.Win/Shell/DocumentWindow.Commands.cs`（新） | 命令分发、剪贴板按键注入、链接对话框、数学块、打印、`--diag menu` |
| `src/MDPreview.Win/Shell/TextPrompt.cs`（新） | 链接地址输入 |
| `src/MDPreview.Win/Shell/NativeMethods.cs` | `SendCtrl` 按键注入 |
| `src/MDPreview.Win/Bridge/webview2-prelude.js` | Ctrl+P / Ctrl+G / Ctrl+Shift+G |
| `src/MDPreview.Win/App.xaml.cs` | 行号偏好广播、`--diag menu` 入口 |
| `tests/MDPreview.Win.Tests/ContextMenuPlanTests.cs` | 9 项：分组、格式命令名、标题 payload、文件参数 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error |
| C# 单测 | `tests/MDPreview.Win.Tests.exe` | 53 passed / 0 failed |
| 菜单树（Markdown） | `MDPreview.exe --diag menu samples/功能演示.md` | 五组菜单完整；表格子菜单 disabled（光标不在表格） |
| 菜单树（代码） | `MDPreview.exe --diag menu src/MDPreview.Win/AppInfo.cs` | 格式组 disabled；源码 disabled；显示行号 `[x]` |
| 窗口初始化 | `--diag state` / `--diag code` | ready / codeMode 正常 |

## 4. 偏差与遗留

- **交互项未在会话内点验**：桌面窗口在屏幕外，格式命令、打印、剪贴板注入、链接对话框需用户在
  正常桌面验收（OQ-W13）。
- 格式快捷键未做壳级拦截：只有菜单入口；加粗/斜体沿用 Milkdown 自带键位。
- 剪贴板菜单项用 Ctrl+字母注入，依赖菜单关闭后焦点回到 WebView2；异常时无动作。
- 表格子菜单在光标不在表格内时整组 disabled；右键菜单仍按点击位置动态判定。

## 5. 状态

自动化与菜单结构验证通过，代码合入；交互项待用户验收后关闭工序。
