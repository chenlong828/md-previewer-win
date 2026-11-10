---
layer: history
module: shell
status: frozen
updated: 2026-11-11
---

# 工序结果：抽屉双击同窗覆盖（W-07，P3）

## 1. 结论

抽屉双击不再新开窗口：改走 `OpenPathAsync(path, reanchor:false)`，在当前窗口覆盖打开，
与 macOS `openDocument(at:reanchor:false)` 同口径——

- 脏文档先走与关闭同一套「保存/不保存/取消」；
- 打开文件即退出目录模式（标题/状态换成文件）；
- 文件仍在锚点子树内时保持抽屉锚点，离开锚点才重锚；
- 外部打开（CLI/拖放/资源管理器/打开文件夹）仍新窗口。

顺带补齐 Ctrl+O 同窗覆盖时缺失的脏确认（此前会静默丢弃未保存修改）。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs` | `OpenPathAsync(path, reanchor)` 增加确认、退目录模式、监听基线；新增 `UpdateDrawerForOpen`；`OnDrawerDoubleClick` 由 `App.OpenDocument` 改为同窗打开 |
| `src/MDPreview.Win/Shell/DrawerAnchor.cs` | `RootAfterOpen` 锚点归属纯函数 |
| `src/MDPreview.Win/Io/FileTree.cs` | `IsWithin` 子树判定（按目录段、大小写不敏感） |
| `src/MDPreview.Win/Shell/DocumentWindow.Commands.cs` | `DiagDrawerOpenAsync` |
| `src/MDPreview.Win/App.xaml.cs` | `--diag open <目录> <文件>` 接线 |
| `tests/MDPreview.Win.Tests/FileTreeTests.cs`、`DrawerAnchorTests.cs` | `IsWithin` 与 `RootAfterOpen` 的 3 正 1 反 |
| `doc/design/architecture.md` | 组件地图与 §8.1 打开路由、诊断样例 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error |
| C# 单测 | `MDPreview.Win.Tests.exe` | 59 passed / 0 failed（新增 4） |
| 同窗诊断（直接子文件） | `MDPreview.exe --diag open <dir> <dir>\a.md` | `path=...a.md`、`title=a.md — MDPreview`、`directory=`、`root=<dir>`、`selected=...a.md` |
| 同窗诊断（嵌套文件） | `MDPreview.exe --diag open <dir> <dir>\sub\b.md` | `path=...b.md`、`title=b.md — MDPreview`、`directory=`、`root=<dir>`（锚点保持） |
| 同窗诊断（离开锚点） | `MDPreview.exe --diag open <dir> samples\code-sample.py` | `root=...samples`（按规则重锚）、`selected=...code-sample.py` |

`--diag open` 走生产入口 `OpenPathAsync(..., reanchor:false)`；若该方法退回新窗，诊断窗口会
仍停留在目录模式、标题仍是目录名——实测三项均显示文件状态，证明是当前窗口内覆盖。

## 4. 偏差与遗留

- **真实双击未点验**：本会话 WebView2 需完全权限才能初始化，无法模拟鼠标；已入 OQ-W15。
- **「最近打开」仍新窗**：REQ-0006 要求当前窗口替换，本次只改抽屉入口；登记 OQ-W16。
- 嵌套文件打开后锚点保持，但树只装载根层，不会自动展开到该文件（选中为空）；
  与既有 rescan 只读根层同口径，未在本次扩大范围。

## 5. 状态

自动化、诊断与文档通过；真实双击与最近文件入口转待办。
