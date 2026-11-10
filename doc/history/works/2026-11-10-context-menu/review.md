---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序评审：右键菜单 + 文件路径动作（W-02，P2）

## 1. 裁决摘要

blocker 0 / decision 3（已落文档与测试）/ hold 1（转人工验收）。

## 2. Blocker

无。

- `dotnet build src/MDPreview.Win -c Debug` → 0 error；
- `tests/MDPreview.Win.Tests.exe` → 51 passed / 0 failed（新增 7 项）；
- `--diag state` 与 `--diag code` 均完成窗口初始化并输出 ready。

## 3. Decision now

| 决策 | 选择 | 被否决项 | 落点 |
|---|---|---|---|
| 菜单实现路线 | 保留 WebView2 默认菜单，`ContextMenuRequested` 追加自定义项（方案 A） | 自建 WPF 菜单（B）、JS 拦截 + WPF 菜单（C） | 本工序 brief §3；`DocumentWindow.ContextMenu.cs` |
| 右键点坐标源 | 前导脚本记录 `event.clientX/Y`（CSS 像素），`focusAt` 直接消费 | `args.Location` + DIP/zoom 换算 | `webview2-prelude.js`；`ReadContextPointAsync` |
| 浏览器菜单项过滤 | 按非本地化 `Name` 移除导航/保存/打印/分享/Inspect | 保留默认全部项 | `BrowserMenuItems` |

pinning：`ContextMenuPlanTests` 覆盖四种文档状态与命令路由；坐标与过滤无自动化入口，列入
hold 的人工验收。

## 4. Verified sound

| 区域 | 证据 |
|---|---|
| 分组计划 | 7 项单测：已保存/未保存/表格内/代码文档、表格命令走 `tableCommand`、剪贴板路径无 `file://`、Explorer 参数 `/select,"…"` |
| 菜单初始化 | `--diag state` / `--diag code` 均通过；`CreateContextMenuItem` 未抛错 |
| 文件菜单定位项 | `BuildMenu` 增加「在资源管理器中显示」；`_path` 为空时 `UpdateTitle`/`UpdateMenuChecks` 置灰 |
| 代码文档只读边界 | 计划在 `isCodeDocument=true` 时剔除表格组，只保留文件组 |

## 5. Hold

1. **实机右键渲染与动作未验证**：本会话桌面窗口位于屏幕外（`GetWindowRect` 为
   `-12800,-12800`），合成 `WM_CONTEXTMENU` 不会触发 WebView2 的菜单事件；需用户在有桌面
   的会话右键一次：表格内出现行列组、复制路径可粘贴、资源管理器定位可打开。
2. **自定义项跨事件复用**：按 WebView2 文档建议固定复用（≤1000 配额），但自动化无法证明
   同一 item 反复加入 `MenuItems` 不报错；若实机出现异常，改为每事件创建并在菜单关闭后释放。
3. 菜单样式由 WebView2 决定，深色主题下与壳不完全一致（非目标，登记为体验债）。

## 6. 结论

有条件通过：阻断项清零；决策已落文档与测试；实机交互项转用户验收，不阻塞代码合入。
