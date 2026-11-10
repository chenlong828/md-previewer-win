---
layer: history
module: shell
status: frozen
updated: 2026-11-11
---

# 工序 brief：抽屉双击同窗覆盖（W-07，P3）

## 1. 需求

用户报告：Windows 从左侧文件抽屉每次双击都新弹窗口，与 macOS 不一致。对照上游
`FileDrawerView.onOpenFile → openDocument(at:reanchor:false)` 与本站需求：

- REQ-0001 §1/§3：双击文件在当前窗口打开，不做多标签；
- REQ-0006 §4：目录模式只占一个窗口，双击文件退出目录模式并覆盖当前窗口；
- SCN-0001 运行序 5：双击文件在当前窗口覆盖打开；脏文档先确认；抽屉双击保持锚点。

Windows 现行实现 `OnDrawerDoubleClick → App.OpenDocument` 直接开新窗，属实现偏差。

## 2. 设计（P3，单组件内）

- 同窗覆盖打开收敛到唯一入口 `DocumentWindow.OpenPathAsync(path, reanchor)`：
  脏文档走与关闭同一套「保存/不保存/取消」；打开文件即退出目录模式；重置监听基线；
  抽屉传 `reanchor:false`，外部来源（Ctrl+O）用默认 true。
- 锚点为纯函数 `DrawerAnchor.RootAfterOpen(currentRoot, path, reanchor)`：reanchor 或文件离开
  锚点子树时锚到文件目录，否则保持当前锚点；包含判定 `FileTree.IsWithin` 按目录段比较，
  避免 `C:\work2` 误判进 `C:\work`。
- 不改桥协议、内核、公共契约；外部打开（CLI/拖放/资源管理器/打开文件夹）仍新窗。

## 3. 范围

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs` | `OpenPathAsync(path, reanchor)`：确认/退目录模式/监听基线；`UpdateDrawerForOpen`；抽屉双击改同窗 |
| `src/MDPreview.Win/Shell/DrawerAnchor.cs` | `RootAfterOpen` 锚点归属 |
| `src/MDPreview.Win/Io/FileTree.cs` | `IsWithin` 子树判定 |
| `src/MDPreview.Win/Shell/DocumentWindow.Commands.cs` | `--diag open` 取数 |
| `src/MDPreview.Win/App.xaml.cs` | `--diag open` 接线 |
| `tests/MDPreview.Win.Tests/FileTreeTests.cs`、`DrawerAnchorTests.cs` | 3 正 1 反 |

## 4. 非目标

- 「最近打开」入口仍新窗（登记 OQ-W16）；
- 打开文件夹仍新窗（W-04 的外部打开语义）；
- 不保留抽屉展开态、不展开到嵌套文件的祖先链；
- 不改内核与 v2 契约。

## 5. 验收

1. `dotnet build` 0 error；C# 单测 ≥59 全绿（新增 4）；
2. `--diag open <目录> <文件>`：`directory=` 为空、`path`/`title` 换成文件、`root` 保持目录、
   `selected` 为该文件；嵌套文件同样保持锚点；离开锚点的文件重锚；
3. 真实双击（同窗、锚点不变、脏文档确认）需桌面会话，登记 OQ-W15。

## 6. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | 同窗打开单入口 + 脏确认 + 退目录模式 | change | CMP-app-shell | ✅ 完成 |
| T-02 | 锚点归属纯函数与子树判定 + 单测 | change | CMP-drawer | ✅ 完成 |
| T-03 | 抽屉双击改同窗 + `--diag open` | change | CMP-app-shell | ✅ 完成 |
| T-04 | 构建/单测/诊断/文档/归档 | verify | — | ✅ 完成 |

## 7. 装载清单

1. `doc/requirements/REQ-0001-file-drawer.md`、`REQ-0006-windows.md`；
2. 上游 SCN-0001 运行序与 `DocumentWindowController.openDocument(at:reanchor:)`；
3. `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs`；
4. `src/MDPreview.Win/Shell/DrawerAnchor.cs`、`src/MDPreview.Win/Io/FileTree.cs`。

## 8. 验证债

- 真实鼠标双击与脏确认弹窗未在桌面会话点验（OQ-W15）。
