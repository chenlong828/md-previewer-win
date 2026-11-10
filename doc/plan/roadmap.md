---
layer: plan
module: global
status: current
updated: 2026-11-10
---

# 里程碑路线图（Windows 移植）

上游 `64ba91b` 起，内核 v2 与缩略图已交付，Windows 侧的工作从「重建内核」变成
「接入内核 + 写壳」。2026-11-10 上游升到 `05493a3`（v0.6.0）后，M5 转为「全量对齐」。
按此重排：

| 里程碑 | 内容 | 依赖 | 状态 |
|---|---|---|---|
| **M0 方向确认** | 壳选型、内核接入方式、首版范围（决策提案 + 架构文档） | 无 | ✅ 2026-11-05（WPF 壳；内核快照 + P-1） |
| **M1 垂直切片** | vendor 内核 + PathPolicy 补丁 + C# 桥 + WebView2 宿主 + 文档 IO + 壳骨架 + `build.ps1` + 冒烟 | M0 | ✅ 2026-11-05（见 [result.md](../history/works/2026-11-04-windows-port/result.md)） |
| **M2 浏览与媒体** | 文件抽屉（懒加载目录树）、图片粘贴/拖放导入、最近文件完善 | M1 | ✅ 2026-11-05（见 result.md「M2」） |
| **M3 阅读与输出** | 代码只读视图（44 语言/行号/缩略图）、PDF/HTML/打印、查找栏与源码模式验收、诊断子命令 | M2 | ✅ 2026-11-05（见 result.md「M3」） |
| **M4 分发** | 便携 zip、文件关联、图标、Win10 实机验证 | M3 | ✅ 2026-11-05（Win10 实机待用户侧验证，见 [result.md](../history/works/2026-11-04-windows-port/result.md)「M4」与 OQ-W10） |

> M1–M4 工序已于 2026-11-09 收口并归档：`doc/history/works/2026-11-04-windows-port/`。
> 新工序在 `doc/works/` 立项。

## M5 全量对齐（2026-11-10 立项）

对照基准：上游 `origin/main` = `05493a3`（v0.6.0，2026-11-10）+ macOS README「主要功能」
与菜单/右键动作清单。拆成五个工序，按组件 WIP=1 顺序推进；一个工序收口后才立下一个。

| 工序 | 内容 | 组件 | 依赖 | 状态 |
|---|---|---|---|---|
| W-01 `2026-11-10-kernel-sync` | 内核 0.6.0 同步：插入行列光标跟随、`focusAt` 聚焦、`tableInfo` 行列号、core 测试 | 内核 | 无 | ✅ 2026-11-10（[已归档](../history/works/2026-11-10-kernel-sync/result.md)） |
| W-02 `2026-11-10-context-menu` | 自建文档右键菜单（编辑/只读可用项）+ 拷贝文件路径 + 资源管理器定位 + 表格行列组 | 壳 | W-01 | ✅ 2026-11-10（[已归档](../history/works/2026-11-10-context-menu/result.md)；实机右键见 OQ-W12） |
| W-03 `2026-11-10-editor-menus` | 格式/表格/剪贴板/查找下一个/行号开关/打印菜单补全（命令注册表统一驱动） | 壳 | W-02 | ✅ 2026-11-10（[已归档](../history/works/2026-11-10-editor-menus/result.md)；实机见 OQ-W13） |
| W-04 `2026-11-10-directory-drawer` | 打开文件夹/目录模式、抽屉锚点菜单与激活 rescan、拖入 `.md` 开窗 | 壳 | W-03 | ✅ 2026-11-10（[已归档](../history/works/2026-11-10-directory-drawer/result.md)；拖入 `.md` 见 OQ-W14、实机见 OQ-W15） |
| W-05 `2026-11-10-release-docs` | README/版本 0.6.0、REQ 与场景收口、architecture 回写、工序归档 | 文档 | W-01–W-04 | ✅ 2026-11-10（[已归档](../history/works/2026-11-10-release-docs/result.md)） |
| W-06 `2026-11-10-drag-drop` | 拖入 `.md`/目录/图片：WPF Drop + 关闭 WebView2 外部拖放 | 壳 | W-04 | ✅ 2026-11-10（[已归档](../history/works/2026-11-10-drag-drop/result.md)；实机见 OQ-W14） |

> M5 代码与文档已收口（2026-11-10）：内核 0.6.0、右键菜单、格式/表格菜单、目录模式、拖入文件全部落地；实机验收见 OQ-W12–W15，拖入实机见 OQ-W14。新增组件/行为以 `design/architecture.md` §8.1 为准。

验收：每个工序 brief 的任务全绿 + 门禁（`build.ps1`、Node 套件、C# 单测、`--diag`）+ 场景
`verified`；M5 完成 = macOS README「主要功能」与菜单/右键动作逐项可在 Windows 走通。

## 与上游的关系

- 内核 = 上游 `web/` 快照（记录 commit）+ Windows 平台补丁；上游修复按主题 cherry-pick，
  每个工序收口对照一次（`kernel/SOURCE.md` 记差异）。
- 文档 = 五态骨架同步；需求卡全量同步（11 张），场景与组件规格在 Windows 侧实现时另写，
  未实现前只链上游，不复制成「假真相」。

## 执行纪律

- 一个任务一个组件、一个提交；P1 变更（壳选型、内核补丁、契约变更）先确认再动手。
- 每阶段收口：更新 `design/`、写 `result.md`、把活结论登记落点，再进下一阶段。
- 门禁先立：`build.ps1` 在内核 Node 门与 C# 编译都绿之前不算完成。
