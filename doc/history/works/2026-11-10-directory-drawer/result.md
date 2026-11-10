---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序结果：目录模式与抽屉对齐（W-04，P2）

## 1. 结论

Windows 具备目录导航能力：

- `MDPreview.exe <目录>` 或「文件 → 打开文件夹…」（Ctrl+Shift+O）进入目录模式：
  空编辑器 + 抽屉，标题/状态栏显示目录名；
- 抽屉根目录改为唯一写入口 `SetDrawerRoot`，↑/Home/锚点菜单都走同一条路径；
- 锚点按钮「▾」列出盘符根 → 当前目录的祖先链，当前项勾选，选中即重锚；
- 窗口重新激活时按 2 秒节流重载根层；
- `--diag dir` 可无界面取目录模式状态。

拖入 `.md` 未实现（见 §4）。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/Shell/DocumentWindow.xaml` | 抽屉锚点按钮「▾」 |
| `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs` | 目录模式字段/初始化/标题状态、`SetDrawerRoot`、锚点菜单、激活刷新、打开文件夹入口 |
| `src/MDPreview.Win/Shell/DrawerAnchor.cs`（新） | 祖先链与显示名纯函数 |
| `src/MDPreview.Win/Bridge/webview2-prelude.js` | Ctrl+Shift+O |
| `src/MDPreview.Win/App.xaml.cs` | `--diag dir`；诊断允许目录参数 |
| `tests/MDPreview.Win.Tests/DrawerAnchorTests.cs`（新） | 链与盘符根显示名 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error |
| C# 单测 | `tests/MDPreview.Win.Tests.exe` | 55 passed / 0 failed |
| 目录诊断 | `MDPreview.exe --diag dir samples` | `directory=...\samples`、`title=samples — MDPreview`、`entries=2`、`drawer=True`、`root=...\samples` |
| 锚点纯函数 | `DrawerAnchorTests` | `C:\`→`C:\work`→`C:\work\docs`；盘符根显示完整路径 |

## 4. 偏差与遗留

- **拖入 `.md` 未实现**：需要把 WebView2 `AllowExternalDrop` 关掉改走 WPF Drop，可能破坏
  已验证的图片拖放；本会话无桌面可验证，登记 OQ-W14。
- 锚点菜单/目录模式/激活刷新的真实交互未点验（OQ-W15）。
- 激活刷新只重载根层，展开态会重置；保留展开态留待后续。

## 5. 状态

自动化与诊断通过，代码合入；交互项与拖入 `.md` 转待办。
