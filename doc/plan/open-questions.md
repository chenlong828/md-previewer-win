---
layer: plan
module: global
status: current
updated: 2026-11-05
---

# 未决问题

| ID | 问题 | 现状 | 默认（若不再确认） |
|---|---|---|---|
| OQ-W1 | 壳技术栈：WPF / WPF+Win11 外观 / WinUI 3 | ⏳ 待用户选择 | WPF + WebView2 |
| OQ-W2 | 内核 Windows 化补丁：就地打补丁 vs 先提上游 PR | ⏳ 待用户选择 | 就地补丁（`kernel/PATCHES.md` + 后续提上游） |
| OQ-W3 | 首版范围 | ✅ 已确认 M1 垂直切片 | — |
| OQ-W4 | 单实例：是否默认转发到已有窗口 | 未确认 | 是（命名管道 + `--new-instance` 逃生） |
| OQ-W5 | 最低系统：Win10 1809 是否算支持 | 未确认 | 是（WebView2 下限），M4 实机验证 |
| OQ-W6 | 分发形态：MSIX vs 便携 zip vs 安装器 | M4 再定 | 便携 zip + HKCU 关联，签名后置 |
| OQ-W7 | 诊断入口：命令行子命令 vs 设置页 | M3 再定 | 子命令 `selftest` / `screenshot` |
| OQ-W8 | 上游快照同步节奏 | 未确认 | 每个里程碑收口对照一次，`kernel/SOURCE.md` 记差异 |
| OQ-W9 | 缩略图默认值 | 跟上游（默认显示） | 默认开，视图菜单可关，偏好跨窗口 |
| OQ-W10 | Win10 1809 实机验证 | 待用户侧执行 | 本机只到 Win11 24H2；静态保障已做（DWM 调用失败即忽略、tar 打包、Runtime 检测提示），真机解压 zip 跑 `--diag info` + 打开/保存一次即可闭环 |
| OQ-W11 | 壳内写 `HKCU\Software\Classes` 被拒 | 受限令牌所致，非代码问题 | 提供 `tools/associate.ps1` 等效入口，壳内异常转提示；普通用户会话下 `--install-association` 应正常 |

## 已收敛（留档）

- 内核路线：不做 v2 重构，vendor 上游成品（提案 §2 方案 A）。
- 图片路径语义：只在 `kernel/semantics` 一处，壳不碰 Markdown 文本（上游 R9 已关闭，Windows 继续守）。
- 磁盘写入口：只在壳（原子写、编码、尾换行）。
