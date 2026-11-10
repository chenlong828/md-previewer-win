---
layer: history
module: shell
status: frozen
updated: 2026-11-10
---

# 工序 brief：目录模式与抽屉对齐（W-04，P2）

## 1. 需求

对齐 macOS 的目录导航能力：

1. CLI 传目录 → 目录模式窗口：空编辑器 + 抽屉导航；
2. 文件菜单「打开文件夹…」（Ctrl+Shift+O）→ 新窗口进入目录模式；
3. 抽屉锚点菜单：列出盘符根 → 当前目录的祖先链，选中即重锚；
4. 窗口重新激活时刷新抽屉根层（节流，避免抖动）；
5. 拖入 `.md` 打开窗口（本工序评估后延后，见 §6）。

## 2. 设计

- 目录模式用 `_directoryRoot` 区分：`IsDirectoryMode` 时 `_path == null`，标题/状态栏显示目录名；
- 抽屉根目录唯一写入口 `SetDrawerRoot`：更新标签 + 重载树；上移/Home/锚点菜单都走它；
- 锚点链是纯函数 `DrawerAnchor.Chain`（盘符根 → 当前），可单测；菜单项勾选当前锚点；
- 激活刷新 `RefreshDrawerOnActivate` 2 秒节流；只重载根层，展开态会重置（与 macOS 的
  `rescan=重读根层` 同口径，展开态保留后续再做）；
- 打开文件夹总是新窗口，不覆盖当前文档；CLI 目录复用同一 `OpenDirectoryInitialAsync` 路径。

## 3. 范围

| 文件 | 改动 |
|---|---|
| `Shell/DocumentWindow.xaml(.cs)` | `_directoryRoot`、目录模式初始化/标题/状态、锚点按钮与菜单、激活刷新、打开文件夹入口 |
| `Shell/DrawerAnchor.cs`（新） | 祖先链与显示名纯函数 |
| `Bridge/webview2-prelude.js` | Ctrl+Shift+O 转发 |
| `App.xaml.cs` | `--diag dir`；诊断允许目录参数 |
| `tests/.../DrawerAnchorTests.cs`（新） | 链与盘符根显示名 |

## 4. 非目标

- 不做拖入 `.md`（需要关闭 WebView2 外部拖放并改走 WPF Drop，会危及已验证的图片拖放；
  登记 OQ-W14 后续单独验证）；
- 不保留抽屉展开态（本工序只保证 rescan 读根层）；
- 不做锚点菜单的 Finder 图标（WPF 用文字 + 勾选）。

## 5. 验收标准

1. `--diag dir samples`：`directory=` 指向目录、`title=samples — MDPreview`、`entries>0`、
   `drawer=True`、`root=samples`；
2. `DrawerAnchor.Chain(@"C:\work\docs")` → `C:\`、`C:\work`、`C:\work\docs`；
3. `dotnet build` 0 error；C# 单测全绿（≥55）；
4. 实机：`MDPreview.exe <目录>` 进入目录模式；文件→打开文件夹新开窗口；锚点菜单可重锚；
   切回窗口根层刷新。

## 6. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | `_directoryRoot` + 目录模式初始化/标题/状态 | change | CMP-app-shell | ✅ 完成 |
| T-02 | 抽屉根唯一写入口 + 锚点菜单 + 激活刷新 | change | CMP-app-shell | ✅ 完成 |
| T-03 | 打开文件夹入口 + 快捷键 + `--diag dir` | change | CMP-app-shell | ✅ 完成 |
| T-04 | 单测/诊断/result/review/归档 | verify | — | ✅ 自动化/诊断通过；实机与拖入见 OQ-W14/W15 |

## 7. 装载清单

1. `doc/history/works/2026-11-04-windows-port/result.md` §6.3（抽屉已知偏差）；
2. `src/MDPreview.Win/Io/FileTree.cs`（根解析与枚举）；
3. `src/MDPreview.Win/Shell/DocumentWindow.xaml(.cs)`（抽屉 UI 与状态）；
4. macOS `FileDrawerView.swift` / `DrawerAnchorControl.swift`（锚点契约）。

## 8. 验证债

- 锚点菜单与目录模式的真实交互（点击/刷新观感）需在有桌面的会话验收；
- 拖入 `.md` 未实现，OQ-W14 记录。
