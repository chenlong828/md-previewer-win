// Windows 平台补丁 P-1 的单测：PathPolicy（资源 URL 前缀 + win32 路径风格）。
// 纯函数，不依赖 jsdom / milkdown，可在受限会话直接跑：
//   node kernel/test/paths-win32.test.js
import test from 'node:test';
import assert from 'node:assert/strict';

import {
  fileUrlFromPath,
  resourceUrlFromPath,
  resourcePathFromUrl,
  normalizePolicy,
} from '../src/kernel/semantics/paths.js';
import { restoreForSave, collectResourceRefs } from '../src/kernel/semantics/markdown.js';

const WIN = { urlPrefix: 'https://mdpreview.local/__res', style: 'win32' };
const MAC = { urlPrefix: 'file://', style: 'posix' };

test('normalizePolicy：缺省/非法值回落 macOS 默认；前缀去尾斜杠', () => {
  assert.deepEqual(normalizePolicy(undefined), MAC);
  assert.deepEqual(normalizePolicy({ urlPrefix: WIN.urlPrefix + '/', style: 'win32' }), WIN);
  assert.deepEqual(normalizePolicy({ urlPrefix: '', style: 'nope' }), MAC);
});

test('默认策略：与旧 fileUrlFromPath 逐字节一致', () => {
  for (const p of ['/Users/me/a b/图.png', '/tmp/x(y).png', '/a/b/c.md']) {
    assert.equal(resourceUrlFromPath(p, MAC), fileUrlFromPath(p));
  }
});

test('win32：盘符路径 → URL → 路径', () => {
  const url = resourceUrlFromPath('C:\\docs\\a b\\图.png', WIN);
  assert.equal(url, 'https://mdpreview.local/__res/C:/docs/a%20b/%E5%9B%BE.png');
  assert.equal(resourcePathFromUrl(url, WIN), 'C:\\docs\\a b\\图.png');
});

test('UNC 路径保留双反斜杠', () => {
  const url = resourceUrlFromPath('\\\\server\\share\\a.png', WIN);
  assert.equal(url, 'https://mdpreview.local/__res//server/share/a.png');
  assert.equal(resourcePathFromUrl(url, WIN), '\\\\server\\share\\a.png');
});

test('其它前缀 / 远程 / 相对引用不被误认', () => {
  assert.equal(resourcePathFromUrl('https://x/y.png', WIN), null);
  assert.equal(resourcePathFromUrl('img/a.png', WIN), null);
  assert.equal(resourcePathFromUrl('file:///C:/x.png', WIN), null);
});

test('restoreForSave：资源 URL 还原为文档内相对路径（正斜杠）/ 目录外绝对路径', () => {
  const baseDir = 'C:\\docs';
  const md = '![a](https://mdpreview.local/__res/C:/docs/img/a.png)\n![b](https://mdpreview.local/__res/C:/other/b.png)';
  assert.equal(
    restoreForSave(md, { baseDir, ...WIN }),
    '![a](img/a.png)\n![b](C:\\other\\b.png)',
  );
});

test('restoreForSave：macOS 默认口径零回归', () => {
  const md = '![a](file:///Users/me/docs/img/a.png)';
  assert.equal(restoreForSave(md, { baseDir: '/Users/me/docs' }), '![a](img/a.png)');
});

test('collectResourceRefs：资源 URL 解码后进入待保护集合', () => {
  const refs = collectResourceRefs('![a](https://mdpreview.local/__res/C:/docs/img/a.png)', WIN);
  assert.deepEqual(refs, ['C:\\docs\\img\\a.png']);
});

test('前缀大小写与主机名不敏感（Chromium 可能小写化）', () => {
  const url = 'HTTPS://MDPREVIEW.LOCAL/__res/C:/docs/a.png';
  assert.equal(resourcePathFromUrl(url, WIN), 'C:\\docs\\a.png');
});
