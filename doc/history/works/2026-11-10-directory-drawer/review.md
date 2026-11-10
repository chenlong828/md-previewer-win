---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序评审：目录模式与抽屉对齐（W-04，P2）

## 1. 裁决摘要

blocker 0 / decision 3（已落文档与测试）/ hold 2（拖入 `.md`、实机交互）。

## 2. Blocker

无。

- `dotnet build` 0 error；C# 单测 55 passed；
- `--diag dir samples` 输出目录、标题、条目、抽屉状态齐全。

## 3. Decision now

| 决策 | 选择 | 被否决项 | 落点 |
|---|---|---|---|
| 目录模式表示 | `_directoryRoot` 非空即目录模式，`_path` 保持 null | 伪造空文档路径 | `IsDirectoryMode` / `OpenDirectoryInitialAsync` |
| 抽屉根写入口 | `SetDrawerRoot` 单点（标签 + 重载树） | ↑/Home/锚点各写一套 | `DocumentWindow.xaml.cs` |
| 打开文件夹 | 总是新窗口，不覆盖当前文档 | 当前窗口切换目录 | `OpenFolderViaDialogAsync` |
| 激活刷新 | 2 秒节流，重载根层 | 每次激活全量重扫 | `RefreshDrawerOnActivate` |

pinning：`DrawerAnchorTests` 固定祖先链与盘符根显示名；`--diag dir` 固定目录模式诊断面。

## 4. Verified sound

| 区域 | 证据 |
|---|---|
| 目录模式 | `--diag dir samples`：root/entries/title/drawer 全对 |
| CLI 目录 | 诊断走 `new DocumentWindow(settings, dir)` 同一初始化路径 |
| 锚点链 | 单测：`C:\`→`C:\work`→`C:\work\docs`；盘符根显示回退完整路径 |
| 抽屉重锚 | ↑/Home/锚点菜单统一 `SetDrawerRoot`，`Directory.Exists` 校验 |
| 快捷键 | prelude `Ctrl+Shift+O` → `openFolder` → 对话框 |

## 5. Hold

1. **拖入 `.md` 未实现**：改动 `AllowExternalDrop` 可能破坏图片拖放，且本会话无法验证拖放；
   登记 OQ-W14，后续单独工序处理。
2. 锚点菜单/目录模式/激活刷新的真实交互未点验，登记 OQ-W15。

## 6. 结论

有条件通过：自动化与诊断全绿；拖入 `.md` 与实机交互转待办，不阻塞合入。
