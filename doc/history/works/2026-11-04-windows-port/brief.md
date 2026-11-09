---
layer: history
module: global
status: frozen
updated: 2026-11-09
---

# 工序 brief：MDPreview Windows 移植（M1 垂直切片 → M4 分发）

## 1. 需求

把上游 macOS 版 md-previewer 移植为 Windows 原生应用：WPF 壳 + WebView2 承载同一套 JS 内核，
交互同构、功能对齐；Windows 侧文档与技能同步维护。

## 2. 背景（2026-11-05 复核上游 `64ba91b`）

- 内核 v2 已落地并归档：四层（transport/contract/semantics/components）+ 41 op / 7 通知 /
  2 内核请求 / 5 错误码；macOS 壳已整体切换，R9（路径语义双头）**已关闭**。
- `core/` 已解体为七个功能组件 + shared + 8 端口，`check-kernel.mjs` 守禁边，Node 套件 86 项。
- 新功能：右侧文档缩略图（REQ-0011 / CMP-minimap），内核自带、默认显示。
- 因此本工序不再做「内核 v2 化」：直接 **vendor 上游 `web/`**，Windows 侧只补一处平台接缝。

## 3. 决策状态

| 决策 | 状态 |
|---|---|
| 首版范围：M1 垂直切片 | ✅ 用户已确认（2026-11-04） |
| 内核路线：v2（现落地为「快照 vendor + 最小补丁」） | ✅ 方向确认；补丁方式已确认就地补丁（2026-11-05，P-1） |
| 壳技术栈：WPF + WebView2 + Win11 外观 | ✅ 已确认（2026-11-05） |
| 单实例 + 命名管道、HKCU 文件关联 | ✅ 默认执行（M1/M4 交付） |

## 4. 范围（M1）

打开 → 编辑 → 保存全链路可用，且与上游 v2 契约完全对齐：

1. 内核 vendor 与门禁（`check-kernel` + Node 套件在 Windows 仓库可跑）；
2. 内核 Windows 化补丁：`PathPolicy` 参数化（资源 URL scheme + `win32` 路径风格）；
3. C# `KernelClient` v2（帧编解码 / id 关联 / 超时 / 两请求应答）；
4. WebView2 宿主：虚拟主机、`mdres://` 资源 scheme、CSP、右键菜单、拖放与快捷键；
5. 文档 IO：UTF-8/GB18030 检测、原子写、尾换行归一、外部修改监听；
6. 壳骨架：窗口、菜单、最近文件、单实例转发、主题/缩放、缩略图偏好；
7. `build.ps1` 一键构建；端到端冒烟。

## 5. 非目标

- 文件抽屉、代码只读视图、PDF/HTML 导出、诊断子命令（M2/M3）；
- 安装包、代码签名、自动更新（M4）；
- 任何内核功能重写或分叉；上游未合入的功能不抢先做。

## 6. 验收标准

1. `./build.ps1` 从干净仓库一次成功（内核门 → 壳编译 → 组装）；
2. `kernel/` 的 `node check-kernel.mjs` 与 `node --test test/` 在 Windows 仓库全绿；
3. 打开 `samples/功能演示.md`，标题/表格/公式/Mermaid/任务列表正确渲染，右侧缩略图可见并跟手；
4. 编辑后 `Ctrl+S` 保存：内容、换行、编码与上游一致（GB18030 文件往返不变形）；
5. 多窗口各自独立，关闭脏窗口有确认；`Ctrl+N/O/S`、主题切换、缩放、缩略图开关可用；
6. 图片相对路径显示、粘贴导入落盘、保存还原为相对路径（复用内核语义，壳不碰文本）；
7. 文档与实现一致：architecture 转 `current`，决策归档。

## 7. 任务表

| ID | 任务 | 组件 | 依赖 | 状态 |
|---|---|---|---|---|
| T-01 | vendor 内核 `web/`→`kernel/`，记 `SOURCE.md`，跑通 Node 门 | CMP-kernel-* | 无 | ✅ M1 |
| T-02 | 内核补丁：`PathPolicy`（scheme/style）参数化 + 单测 | CMP-kernel-semantics | T-01 | ✅ M1（P-1） |
| T-03 | C# `KernelClient` v2 + 单测（帧/超时/错误码） | CMP-shell-bridge | 无 | ✅ M1 |
| T-04 | WebView2 宿主：虚拟主机、`mdres`、CSP、菜单/拖放/快捷键 | CMP-shell-bridge | T-03 | ✅ M1（同源前缀替代 `mdres`） |
| T-05 | 文档 IO：编码、原子写、外部修改 | CMP-document-io | 无 | ✅ M1 |
| T-06 | 壳骨架：窗口/菜单/最近/单实例/主题缩放/缩略图偏好 | CMP-app-shell | T-04,T-05 | ✅ M1 |
| T-07 | `build.ps1`：内核门 → `dotnet publish` → 组装 | — | T-01,T-06 | ✅ M1 |
| T-08 | 端到端冒烟：打开→编辑→保存→图片→缩略图 | — | T-06 | ✅ M1–M4（`--diag` + 实机） |
| T-09 | 文档收口：决策归档、architecture 转 current | — | T-08 | ✅ 2026-11-09 |

## 8. 装载清单

1. `kernel/src/kernel/{transport,contract,semantics,shared,components}`（vendor，读顺序：transport → contract → components/document）；
2. `kernel/SOURCE.md`（上游 commit 与补丁说明）；
3. `D:\Github\md-previewer\Sources\MDPreview\KernelClient.swift`（壳侧协议实现参照）；
4. `doc/history/works/2026-11-04-kernel-v2/spec-delta.md` §3（协议实现细节）；
5. `.agents/skills/`（改代码前过 design-before-coding / dev-execution-spec）。

## 9. 验证债

- 打印与「另存为后 baseDir」时序沿用上游人工点验清单，M3 再自动化；
- 上游快照与主本漂移：M1 收口时对照一次，写入 `kernel/SOURCE.md`；
- Win10（1809）实机未验；已登记 OQ-W10，待用户侧在 Win10 解压便携包补测。

## 10. 收口（2026-11-09）

M1–M4 全部交付；本工序运行态复核后关闭并归档：

- 内核门：`check-kernel` 43 文件 OK；5 套件 95 项全绿；语言表 44 门 / 78 扩展名 OK。
- 壳：`dotnet build -c Debug` 0 error；单测 44 项全绿。
- 分发：`dist/MDPreview/MDPreview.exe --diag info` 退出 0（protocol 2 / kernel=True / languages 44）。
- 文档：`architecture.md` 转 `current`；OQ-W1/W2/W6/W7/W9 标已确认；工序目录移入
  `doc/history/works/2026-11-04-windows-port/`（frontmatter `frozen`）。
- 遗留仍只在 `plan/open-questions.md`（OQ-W10 Win10 实机、OQ-W11 受限令牌关联）与
  `result.md` §8.4 / §5 的体验债，不阻塞本工序关闭。
