---
layer: history
module: global
status: frozen
updated: 2026-11-10
---

# 工序结果：内核同步到上游 0.6.0（`64ba91b` → `05493a3`）

## 1. 结论

上游 0.6.0 的内核增量已全部吸收，Windows 快照与本工序范围外的上游内容保持零差异：

- 4 个内核文件同步：插入行列后光标进入新单元格、`focusAt` 聚焦编辑器、`tableInfo`
  返回行列号、core 套件补回归用例；
- `diagnostics-ops.js` 是 P-1 补丁文件，手工合并后与上游只差 P-1 的 `pathPolicy` 两行；
- `kernel/SOURCE.md` 快照 commit 更新为 `05493a3`；
- 门禁全绿，`kernel/dist` 已用新源码重建（未入库，构建产物）。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `kernel/src/kernel/components/editor/tables.js` | 新增 `caretIntoCell`；插入行落新行首格、插入列按最终 doc 重算新单元格位置 |
| `kernel/src/kernel/components/editor/ops.js` | `focusAt` 设置选区后 `view.focus()`，原生菜单弹出前 caret 可见 |
| `kernel/src/kernel/components/editor/diagnostics-ops.js` | 合并上游 `findTableCellDepth` 导入与 `row/col`；保留 P-1 的 `state.pathPolicy` |
| `kernel/test/core.test.js` | 新增「表格行列：插入后光标进入新行/新列（连续操作沿同向累积）」 |
| `kernel/SOURCE.md` | 快照 commit/日期与说明更新 |
| `doc/plan/roadmap.md` | M5 五个工序与验收基准 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 内核禁边 | `node kernel/check-kernel.mjs` | OK（43 个内核文件） |
| semantics | `node test/semantics.test.js` | 50 pass / 0 fail |
| contract | `node test/contract.test.js` | 19 pass / 0 fail |
| paths-win32 | `node test/paths-win32.test.js` | 9 pass / 0 fail |
| core（jsdom） | `node test/core.test.js` | 12 pass / 0 fail（含新增行列用例） |
| minimap（jsdom） | `node test/minimap.test.js` | 6 pass / 0 fail |
| 内核打包 | `node kernel/build.mjs` | `dist/bundle.js` 4.0 MB |
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error（2 个 NU1900 离线警告） |
| C# 单测 | `tests/MDPreview.Win.Tests.exe` | 44 passed / 0 failed |

上游对照：`git diff` 后 `kernel/src` 相对 `05493a3` 的差异只剩 `PATCHES.md` 登记的 P-1
六文件（其中本工序只改 `diagnostics-ops.js` 的 P-1 保留行），其余逐字节一致。

## 4. 偏差与遗留

- 无功能偏差。`kernel/dist` 是 `.gitignore` 的构建产物，源码提交后由 `build.ps1` 重建；
  本工序已在本地重建并验证。
- 本工序定级 P3（纯内容同步，不新增组件/契约/数据所有权），按纪律免 `review.md`。

## 5. 状态

工序关闭并归档；下一工序 W-02（右键菜单）按 roadmap 顺序立项。
