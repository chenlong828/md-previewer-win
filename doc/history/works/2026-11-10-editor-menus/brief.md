---
layer: history
module: shell
status: frozen
updated: 2026-11-10
---

# 工序 brief：菜单栏补全（W-03，P2）

## 1. 需求

把 macOS 菜单栏的能力补到 Windows，菜单与右键共用一份命令注册表：

1. 编辑：撤销/重做、剪切/拷贝/粘贴/全选、查找/查找下一个/查找上一个；
2. 格式：加粗/斜体/删除线/行内代码/链接；标题 1–3/正文；引用/无序/有序列表；代码块/数学公式块/插入表格；表格子菜单（行列插入删除）；分割线；插入图片；
3. 视图：源码模式（从编辑移入）、显示行号（代码文档专用，全局偏好跨窗口广播）；
4. 文件：打印（`ShowPrintUI`）；
5. 右键：在文件/表格组之外补查找/源码、行内格式、插入表格/分割线组。

## 2. 背景与设计

- 内核已有 `formatCommand`（命令名 + 位置参数）、`insertTable`、`print`、`setCodeLineNumbers`；壳只需把菜单动作映射到 op 与参数。
- 菜单栏与右键共用 `EditorCommands`：Kind=Shell/Op/Format/Table，Argument 是内核命令名，Payload 是位置参数；新增命令只改一处。
- 表格子菜单的可用性由 `selinfo` 通知维护 `inTable`；代码文档整组不可用。
- 行号开关是全局偏好；切换后广播所有已打开窗口（`App.ApplyCodeLineNumbersToAll`），新窗口继承。
- 剪贴板菜单项：焦点在 WebView2 子窗口，WPF 路由命令到不了页面；菜单关闭后把焦点还给 WebView2 并注入 Ctrl+字母（`NativeMethods.SendCtrl`），与用户按快捷键等价。
- 链接：用壳内 `TextPrompt` 收地址，传 `toggleLinkCommand` 的 `{href}`；不启用脚本对话框。
- 打印：`CoreWebView2.ShowPrintUI()`（与内核 `print` 同一浏览器打印面）。

## 3. 范围

| 文件 | 改动 |
|---|---|
| `Shell/EditorCommands.cs` | 命令模型扩展（Kind/Argument/Payload/分组），全部菜单命令 |
| `Shell/ContextMenuPlan.cs` | Markdown 增加查找/源码、行内格式、插入组 |
| `Shell/DocumentWindow.xaml.cs` | 菜单重构（文件/编辑/格式/视图/帮助）、表格/行号状态、selinfo、快捷键分支 |
| `Shell/DocumentWindow.Commands.cs`（新） | 命令分发、剪贴板注入、链接、数学块、打印、菜单诊断 |
| `Shell/TextPrompt.cs`（新） | 链接地址输入对话框 |
| `Shell/NativeMethods.cs` | `SendCtrl` 按键注入 |
| `Bridge/webview2-prelude.js` | Ctrl+P / Ctrl+G / Ctrl+Shift+G 转发 |
| `App.xaml.cs` | 行号偏好广播、`--diag menu` 入口 |
| `tests/.../ContextMenuPlanTests.cs` | 分组与格式命令断言 |

## 4. 非目标

- 不改内核 op 与契约；不新增桥消息；
- 不做格式快捷键的壳级拦截（加粗/斜体等沿用 Milkdown 键位；菜单项不带手势的以菜单为准）；
- 不做目录模式/抽屉（W-04）、不做发布收口（W-05）。

## 5. 验收标准

1. `dotnet build` 0 error；C# 单测全绿（≥53）；
2. `--diag menu samples/功能演示.md` 输出文件/编辑/格式/视图/帮助五组，格式组含全部命令，表格子菜单未在表格内时 disabled；
3. `--diag menu <code.cs>` 输出格式组 disabled、源码模式 disabled、显示行号勾选且可用；
4. `--diag state` / `--diag code` 窗口初始化正常；
5. 实机：加粗/标题/列表/插入表格/表格行列/分割线/行号开关/打印可用；剪贴板菜单项与链接对话框可用。

## 6. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | 命令模型扩展 + 格式/表格命令表 | change | CMP-app-shell | ✅ 完成 |
| T-02 | 菜单栏重构 + 表格/行号状态 + selinfo | change | CMP-app-shell | ✅ 完成 |
| T-03 | 命令分发：剪贴板注入/链接/数学块/打印 | change | CMP-app-shell | ✅ 完成 |
| T-04 | `--diag menu` + 单测 + 实机验收 | verify | — | ✅ 自动化/诊断通过；实机见 OQ-W13 |

## 7. 装载清单

1. `kernel/src/kernel/components/editor/commands.js`（formatCommand 命令名）；
2. `kernel/src/kernel/components/editor/ops.js`（insertTable/tableCommand 形状）；
3. `kernel/src/kernel/contract/ops.js`（selinfo 通知、print/setCodeLineNumbers）；
4. macOS `EditorCommands.swift` 与 `AppDelegate.buildMenus`（分组与手势对照）；
5. `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs`（现有菜单与状态）。

## 8. 验证债

- 剪贴板注入、链接对话框、打印对话框依赖交互桌面，需实机点验；
- 格式快捷键未做壳级拦截，部分命令（删除线/行内代码/链接/标题）目前只有菜单入口。
