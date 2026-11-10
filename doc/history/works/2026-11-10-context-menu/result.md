---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序结果：右键菜单 + 文件路径动作（W-02，P2）

## 1. 结论

Windows 文档右键从「Chromium 默认菜单」升级为「默认编辑项 + 文件级动作 + 表格行列组」：

- 已保存文档右键出现「复制文件路径」「在资源管理器中显示」；
- 表格内右键先 `focusAt` 再 `tableInfo`，出现 4 个插入项与 3 个删除项，命令走内核
  `tableCommand`，插入后光标进入新行（W-01 行为）；
- 代码只读文档只保留文件级动作；未保存文档没有文件级动作；
- 文件菜单同步增加「在资源管理器中显示」，未保存时置灰；
- 浏览器专属项（后退/刷新/另存/打印/分享/Inspect 等）从右键菜单移除。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/Shell/EditorCommands.cs`（新） | 右键命令表 + 文件动作参数口径 |
| `src/MDPreview.Win/Shell/ContextMenuPlan.cs`（新） | 纯函数计划：路径/代码/表格三态决定分组 |
| `src/MDPreview.Win/Shell/DocumentWindow.ContextMenu.cs`（新） | `ContextMenuRequested` 接线、默认项过滤、自定义项复用与分发 |
| `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs` | 初始化接线、文件菜单定位项、可用态 |
| `src/MDPreview.Win/Bridge/webview2-prelude.js` | 记录右键点 `event.clientX/Y` |
| `src/MDPreview.Win/MDPreview.Win.csproj` | `InternalsVisibleTo` 测试程序集 |
| `tests/MDPreview.Win.Tests/ContextMenuPlanTests.cs`（新） | 7 项：四态分组、命令路由、路径参数 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error（2 个 NU1900 离线警告） |
| C# 单测 | `tests/MDPreview.Win.Tests.exe` | 51 passed / 0 failed（新增 7） |
| 窗口初始化 | `MDPreview.exe --diag state samples/功能演示.md` | ready=true；菜单项创建无异常 |
| 代码文档 | `MDPreview.exe --diag code src/MDPreview.Win/AppInfo.cs` | codeMode=true；初始化无异常 |

## 4. 偏差与遗留

- **实机右键未在会话内验证**：桌面窗口被系统放到屏幕外（`-12800,-12800`），合成
  `WM_CONTEXTMENU` 不触发 WebView2 菜单事件；复制路径/资源管理器定位也未实际点击。
  已登记为人工验收项（review.md §5），需用户在有桌面的会话右键一次。
- 自定义项固定复用（WebView2 文档建议），跨事件复用的兼容性未经自动化证明；异常时回退为
  每事件创建。
- 右键菜单样式由 WebView2 绘制，深色主题下不保证与壳完全一致（非目标）。

## 5. 状态

自动化与窗口初始化通过，代码合入；实机右键与剪贴板/Explorer 动作待用户验收后关闭工序。
