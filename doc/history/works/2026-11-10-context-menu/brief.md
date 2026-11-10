---
layer: history
module: shell
status: frozen
updated: 2026-11-10
---

# 工序 brief：文档右键菜单 + 文件路径动作（W-02，P2）

## 1. 需求

对齐上游 0.6.0 的两个右键能力，并把表格行列操作接到右键：

1. 已保存文档右键「复制文件路径」——绝对 Windows 路径写入剪贴板，不写文件、不产生 dirty；
2. 已保存文档右键「在资源管理器中显示」——Explorer 选中当前文件；
3. 光标在表格内时右键出现行列组：上/下插入行、左/右插入列、删除行/列/表；
4. 代码只读文档：只出现文件路径动作，不出现表格组；
5. 未保存文档：不出现文件路径动作。

## 2. 背景与现状

- Windows 目前 `AreDefaultContextMenusEnabled = true`，右键是 Chromium 默认菜单：有剪切/复制/
  粘贴，但没有文件路径、资源管理器定位、表格行列。
- 内核已有 `focusAt` / `tableInfo` / `tableCommand`（0.6.0 同步后 `tableInfo` 带 `row/col`），
  壳从未发送 `tableCommand`；WYSIWYG 下无法插入/删除表格行列。
- 上游 macOS 的右键先 `focusAt` 再按 `inTable` 建菜单；Windows 需要等价时序。

## 3. 设计决策（P2）

**方案 A（采用）**：保留 WebView2 默认菜单，在 `CoreWebView2.ContextMenuRequested` 里
追加自定义项，`Handled=false` 由 WebView2 呈现。

- 保留系统剪切/复制/粘贴/全选/撤销/重做及其启用态，不重实现剪贴板；
- `GetDeferral()` 在菜单呈现前完成：读页面右键点 → `focusAt` → `tableInfo` → 追加行列组；
- 右键点由前导脚本记录 `event.clientX/Y`（CSS 像素），与 `posAtCoords` 同口径，不做 DIP 换算；
- 按 `Name`（非本地化）过滤浏览器项：`back/forward/reload/saveAs/print/share/inspect`；
- 自定义项固定复用（≤1000 配额），`CustomItemSelected` 分发到壳动作或内核 op。

**否决 B**：关默认菜单 + 自建 WPF ContextMenu。需重写剪贴板/焦点/DPI 坐标，菜单字体与
页面不一致，失败面明显更大。

**否决 C**：JS `contextmenu` 拦截 + 壳弹 WPF 菜单。剪贴板与坐标问题同 B，且丢失原生菜单
的启用态，收益只有样式一致。

**定级 P2**：新增壳内部模块（命令表 + 菜单计划）、跨文件改壳与宿主前导脚本；不改桥契约、
数据所有权或内核语义。

## 4. 范围

| 文件 | 改动 |
|---|---|
| `Shell/EditorCommands.cs`（新） | 右键命令定义（Id/标题/内核 op/参数/是否需表格） |
| `Shell/ContextMenuPlan.cs`（新） | 纯函数：按 `路径/代码文档/在表格内` 返回分组命令 |
| `Shell/DocumentWindow.ContextMenu.cs`（新） | WebView2 菜单事件接线、默认项过滤、自定义项复用与分发 |
| `Shell/DocumentWindow.xaml.cs` | 初始化接线、文件菜单「在资源管理器中显示」、菜单可用态 |
| `Bridge/webview2-prelude.js` | 记录右键点 `event.clientX/Y` |
| `tests/.../ContextMenuPlanTests.cs`（新） | 计划四态 + 路径动作参数 |

## 5. 非目标

- 不在右键加格式命令、插入表格/分割线、查找、源码模式（W-03 用同一注册表扩展）；
- 不改桥契约（不加 op）、不改内核（表格命令已存在）；
- 不做自绘菜单、不做右键 hover 行高亮；
- 不把「复制文件路径」加进菜单栏（只进右键）。

## 6. 验收标准

1. 单元测试：`ContextMenuPlan.Build` 四种状态（已保存 Markdown、未保存、代码文档、表格内）
   返回预期分组；路径/定位参数使用绝对路径；
2. 实机：打开 `samples/功能演示.md`，在表格内右键出现行列组；点「在下方插入行」表格增一行
   且光标在新行；表格外右键不出现行列组；
3. 实机：右键「复制文件路径」后粘贴得到 `C:\…` 绝对路径；「在资源管理器中显示」打开
   Explorer 并选中文件；
4. 代码文档右键只有文件路径动作；未保存文档没有这两项；
5. `dotnet build` 0 error；`--diag info` 正常；44+ C# 单测全绿；
6. `review.md` 记录四档裁决（P2 至少 blocker / decision / verified sound）。

## 7. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | 命令表 + 计划纯函数 + 单测 | change | CMP-app-shell | ✅ 完成 |
| T-02 | WebView2 菜单接线 + 前导脚本右键点 | change | CMP-app-shell / CMP-shell-bridge | ✅ 完成 |
| T-03 | 文件菜单定位项 + 菜单可用态 | change | CMP-app-shell | ✅ 完成 |
| T-04 | 实机验收、review、result、归档 | verify | — | ✅ 自动化/review/归档；实机右键见 OQ-W12 |

## 8. 装载清单

1. `kernel/src/kernel/contract/ops.js`（`focusAt/tableInfo/tableCommand` 形状）；
2. `kernel/src/kernel/components/editor/{ops,tables}.js`（0.6.0 行为）；
3. `src/MDPreview.Win/Bridge/KernelHost.cs`（CoreWebView2 初始化点）；
4. `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs`（`_path` / `IsCodeDocument` / `RunOpAsync`）；
5. 上游 `doc/requirements/REQ-0012/0013` 与 `SCN-0018/0019`（行为约定）。

## 9. 验证债

- WebView2 菜单项复用是否被 API 允许（同一 item 跨事件加入 MenuItems）需实机确认；
  若不允许，改为每次事件创建并回收，并在本文件记录。
- 右键菜单样式由 WebView2 决定，深色主题下不保证与壳完全一致（非目标）。
