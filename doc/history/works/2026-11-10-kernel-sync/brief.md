---
layer: history
module: global
status: frozen
updated: 2026-11-10
---

# 工序 brief：内核同步到上游 0.6.0（`64ba91b` → `05493a3`）

## 1. 需求

把 `kernel/` 快照从 `64ba91b` 同步到上游 `origin/main` 的最新内容（`05493a3`，v0.6.0），
只吸收上游增量，不新增 P-1 之外的 Windows 补丁：

1. 表格插入行/列后光标进入新单元格（连续插入沿同向累积）；
2. `focusAt` 设置选区后聚焦编辑器，原生右键菜单弹出前 caret 可见；
3. `tableInfo` 增加 0 起算 `row/col`（诊断与自检断言用）；
4. core 套件补「插入后 row/col 递增」回归用例。

## 2. 背景

- Windows 快照停在 `64ba91b`（2026-11-05）；上游最新 `05493a3`（2026-11-10，v0.6.0）。
- 上游 `64ba91b..05493a3` 的内容增量只有 10 个代码文件；其中内核 4 个、macOS 壳 6 个。
- 内核 48 个文件中 42 个与 `64ba91b` 逐字节一致、6 个是 `PATCHES.md` 的 P-1 补丁文件、
  0 个多出；本次要动的 `editor/ops.js`、`editor/tables.js`、`test/core.test.js` 均未被 P-1
  修改，可直接覆盖；`editor/diagnostics-ops.js` 同时被 P-1 与上游修改，需手工合并。
- 上游历史被重写（本地 checkout 的 `main` 与 `origin/main` 无共同祖先），只能按内容
  cherry-pick，不能按 commit 区间操作。

## 3. 范围

| 上游文件 | 处理 |
|---|---|
| `web/src/kernel/components/editor/tables.js` | 整体覆盖（新增 `caretIntoCell`；插入行列后落光标） |
| `web/src/kernel/components/editor/ops.js` | 整体覆盖（`focusAt` 后 `view.focus()`） |
| `web/src/kernel/components/editor/diagnostics-ops.js` | 手工合并：保留 P-1 的 `pathPolicy`，补 `findTableCellDepth` 导入与 `row/col` 返回 |
| `web/test/core.test.js` | 整体覆盖（新增 row/col 累积用例，其余同 `64ba91b`） |

## 4. 非目标

- 不改壳、不改桥契约（op 仍 41 个）、不改内核目录结构、不新增 op；
- 不吸收 macOS 壳的 `EditorWebView` / 右键菜单代码（属 W-02）；
- 不更新 Windows 侧需求卡与架构（属 W-02/W-05）。

## 5. 验收标准

1. `kernel/src` 对上游 `05493a3` 的差异仅剩 P-1 的 6 个补丁文件，且差异内容仍只是 PathPolicy；
2. `node kernel/check-kernel.mjs` 绿；
3. 六个 Node 套件全绿：`semantics` / `contract` / `paths-win32` / `core` / `minimap`（按 `SOURCE.md` 的命令逐个执行）；
4. core 套件新增用例「表格行列：插入后光标进入新行/新列（连续操作沿同向累积）」通过；
5. `kernel/SOURCE.md` 快照 commit 更新为 `05493a3`、日期与套件说明同步；
6. `git diff` 过目后单提交（内核同步一件事）。

## 6. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | 覆盖 tables.js / ops.js / core.test.js，合并 diagnostics-ops.js | change | CMP-kernel-editor | ✅ 完成 |
| T-02 | 更新 `kernel/SOURCE.md`（commit / 日期 / 套件） | doc | — | ✅ 完成 |
| T-03 | 跑内核门与套件、重建 kernel/dist、提交 | verify | — | ✅ 完成 |

## 7. 装载清单

1. `kernel/PATCHES.md`（P-1 文件清单与约定）；
2. `kernel/SOURCE.md`（快照来源与门禁命令）；
3. 上游 diff：`git -C D:\Github\md-previewer diff 64ba91b 05493a3 -- web/src web/test`；
4. `kernel/src/kernel/components/editor/diagnostics-ops.js`（P-1 合并点）；
5. `kernel/test/core.test.js`（用例插入位置）。

## 8. 验证债

- 若受限沙箱无法运行 esbuild/pnpm，`kernel/dist` 不在本工序重建，记入 result 由下一次
  `build.ps1` 全量构建覆盖；源码门禁不受影响。
