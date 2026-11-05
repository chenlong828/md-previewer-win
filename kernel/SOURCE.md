# 内核快照来源

本目录是上游 [md-previewer](https://github.com/chenlong828/md-previewer) 的 `web/` 快照，
用于 Windows 壳的 JS 内核。**除 `PATCHES.md` 记录的补丁外，不得改动本目录。**

| 项 | 值 |
|---|---|
| 上游仓库 | `https://github.com/chenlong828/md-previewer.git` |
| 快照 commit | `64ba91b`（2026-11-05，fix(minimap): 长文缩略图裁切与视口跟手） |
| 复制范围 | `web/src/`、`web/test/`、`build.mjs`、`check-kernel.mjs`、`check-deps.mjs`、`package.json`、`eslint.config.js` |
| 未复制 | `web/node_modules`、`web/dist`（构建产物）、macOS 壳 `Sources/` |
| 协议 | v2：41 op / 7 通知 / 2 内核请求 / 5 错误码 |

## 门禁

```powershell
node kernel/check-kernel.mjs          # 分层禁边
node kernel/test/semantics.test.js     # 语义 50 项（纯函数，无依赖）
node kernel/test/contract.test.js      # 契约 19 项（纯 JS，无依赖）
node kernel/test/paths-win32.test.js   # Windows 补丁 9 项
node kernel/test/core.test.js          # 需 jsdom
node kernel/test/minimap.test.js       # 需 jsdom
```

受限会话下 `node --test <dir>` 会 spawn 子进程（EPERM），改为直接执行测试文件即可。

## 同步方式

1. 在上游确认目标 commit，逐主题 cherry-pick（不整体覆盖，避免吞掉 `PATCHES.md` 的补丁）；
2. 补丁冲突时先读 `PATCHES.md` 的"上游合并状态"，再决定重放或等上游；
3. 更新本文件的 commit 与日期，跑全部门禁后单独提交。
