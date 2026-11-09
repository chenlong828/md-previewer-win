<!-- 文档树导航页：位于文档根，不属五态中的任何一态，故不带 layer/status frontmatter -->

# 文档导航

本树按「五态分离」组织：位置即状态。分层规则见 `.agents/skills/doc-layout-spec`。

## 当前结构

```
doc/
├── requirements/                     ← 需求：为什么做（11 张，同步自上游 64ba91b）
│   ├── REQ-0001-file-drawer.md … REQ-0010-code-view-line-numbers.md
│   └── REQ-0011-document-minimap.md  ← 右侧文档缩略图
├── plan/                             ← 调度：接下来做什么
│   ├── roadmap.md                    ← M0–M4（M1–M4 已交付）
│   ├── risks.md                      ← R-W1…R-W9
│   └── open-questions.md             ← OQ-W1…OQ-W11
├── design/                           ← 真相：系统现在是什么
│   └── architecture.md               ← Windows 版架构（current）
├── works/                            ← 执行：正在做什么（新工序在此立项）
└── history/                          ← 记忆：怎么走到这里的
    ├── decisions/2026-10-21-editor-kernel-spec.md   ← 内核/壳分层与 v2 契约（上游存档）
    ├── works/2026-11-04-kernel-v2/                  ← 上游 v2 落地记录（含 spec-delta）
    ├── works/2026-11-04-windows-port/               ← Windows 移植 M1–M4（brief/result/选型提案）
    └── pitfalls.md                                  ← 踩过的坑（同步自上游）
```

## 约定

- **反占位**：不预建空目录；`design/components/` 与 `design/scenarios/` 在 Windows 侧组件
  真正实现时才创建并写卡片。未实现前，组件与场景以上游文档为准（跨仓链接）。
- **状态与位置一致**：需求 `accepted/delivered`；plan `current`；design `draft → current`；
  works `active`；history `frozen`。
- **需求卡平台化**：同步时统一把 `⌘` 改写为 `Ctrl`、`Finder`→资源管理器、`Dock`→任务栏，
  并保留平台注记；不改需求本身。
- **内核文档归属**：内核 v2 的组件规格在上游 `doc/design/components/CMP-kernel-*`；
  Windows 侧只维护 `kernel/PATCHES.md`（平台补丁）与 `kernel/SOURCE.md`（快照来源）。

## 阅读顺序

1. 为什么做 → `requirements/`（先看 REQ-0006 平台中立）。
2. 接下来做什么 → `plan/roadmap.md`；风险与未决 → `risks.md`、`open-questions.md`。
3. 系统现在是什么 → `design/architecture.md`。
4. 最近一工序怎么干的 → `history/works/2026-11-04-windows-port/`（新工序在 `works/` 立项）。
5. 怎么动手 → `.agents/skills/` 五个技能；历史结论 → `history/`。
