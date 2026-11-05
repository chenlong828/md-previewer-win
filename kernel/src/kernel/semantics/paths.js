// PathPolicy 的文本侧：URL 组装/解码/规范化。纯函数、不抛错、不碰文件系统。
// 编码口径与 Swift `URL.absoluteString`（现行行为）逐字节对齐：
// 路径段允许 pchar（unreserved + sub-delims + ':' + '@'），只编码空格、非 ASCII 与控制字符。

const PATH_HARDENED = /%(21|24|26|27|28|29|2A|2B|2C|3A|3B|3D|40)/gi;

function encodeSegment(segment) {
  return encodeURIComponent(segment).replace(PATH_HARDENED, (match) =>
    String.fromCharCode(parseInt(match.slice(1), 16)));
}

/** 绝对文件系统路径 → file:// URL（与 Swift URL.absoluteString 同口径） */
export function fileUrlFromPath(absPath) {
  if (typeof absPath !== 'string' || absPath.length === 0) return null;
  return `file://${absPath.split('/').map(encodeSegment).join('/')}`;
}

/** file:// 之后的部分 → 路径：手工分段解码（不用整体解码，见 Util.swift 注释的 %2520 陷阱） */
export function percentDecodedPath(afterScheme) {
  return String(afterScheme)
    .split('/')
    .map((segment) => {
      try {
        return decodeURIComponent(segment);
      } catch {
        return segment;
      }
    })
    .join('/');
}

/** file:// URL → 文件系统路径；非 file:// 返回 null（调用方保持原文） */
export function urlToPath(url) {
  if (typeof url !== 'string' || !url.startsWith('file://')) return null;
  return percentDecodedPath(url.slice('file://'.length));
}

// ---------------------------------------------------------------------------
// Windows 平台补丁（kernel/PATCHES.md P-1）：把 macOS 常量（file:// + posix）
// 参数化为 PathPolicy = { urlPrefix, style }。默认值与 macOS 行为逐字节一致。
// 用前缀而非 scheme，是因为宿主可以选「同源路径前缀」（WebView2 虚拟主机），
// 这样图片与页面同源，CSP 无需额外放行、也不依赖自定义 scheme 注册。
// ---------------------------------------------------------------------------

export const DEFAULT_PATH_POLICY = Object.freeze({ urlPrefix: 'file://', style: 'posix' });

/** 宽松归一策略对象：缺省/非法值一律回落默认，调用方不必判空 */
export function normalizePolicy(policy) {
  let urlPrefix = policy && typeof policy.urlPrefix === 'string' && policy.urlPrefix
    ? policy.urlPrefix.replace(/\/+$/, '')
    : DEFAULT_PATH_POLICY.urlPrefix;
  // 'file://' 被去尾斜杠后只剩 'file:'，补回 scheme 分隔符
  if (/^[a-z][a-z0-9+.-]*:$/i.test(urlPrefix)) urlPrefix += '//';
  const style = policy && policy.style === 'win32' ? 'win32' : 'posix';
  return { urlPrefix, style };
}

/** Windows 盘符/UNC 判定（相对路径返回 false） */
export function isWindowsAbsolute(path) {
  return /^[a-zA-Z]:[\\/]/.test(path) || /^\\\\/.test(path);
}

/**
 * 绝对路径 → 可加载 URL。
 * - 默认（file:// + posix）：与旧 `fileUrlFromPath` 完全一致（macOS 行为不变）。
 * - Windows（https://…/__res + win32）：`<prefix>/C:/x`。
 * UNC 路径保留双斜杠形式（`<prefix>//server/share/x`）。
 */
export function resourceUrlFromPath(absPath, policy) {
  if (typeof absPath !== 'string' || absPath.length === 0) return null;
  const { urlPrefix, style } = normalizePolicy(policy);
  if (urlPrefix === 'file://' && style === 'posix') return fileUrlFromPath(absPath);

  const normalized = absPath.startsWith('\\\\')
    ? absPath // UNC：不交给 normalizePath（会吞掉第二个反斜杠）
    : normalizePath(absPath, { style });
  if (!normalized) return null;
  const posix = normalized.replaceAll('\\', '/');
  const withSlash = posix.startsWith('/') ? posix : `/${posix}`;
  return urlPrefix + withSlash.split('/').map(encodeSegment).join('/');
}

/**
 * 可加载 URL → 文件系统路径；不属于本策略的 URL 返回 null（调用方保持原文）。
 * 与 `restoreForSave` 旧口径一致：默认策略仍走 percentDecodedPath → normalizePath。
 */
export function resourcePathFromUrl(url, policy) {
  if (typeof url !== 'string' || url.length === 0) return null;
  const { urlPrefix, style } = normalizePolicy(policy);
  if (url.slice(0, urlPrefix.length).toLowerCase() !== urlPrefix.toLowerCase()) return null;

  let raw = percentDecodedPath(url.slice(urlPrefix.length));
  if (style === 'win32') {
    if (/^\/\/[^/]/.test(raw)) return `\\\\${raw.slice(2).replaceAll('/', '\\')}`;
    if (/^\/[a-zA-Z]:/.test(raw)) raw = raw.slice(1);
  }
  return normalizePath(raw, { style });
}

/**
 * 规范化：折叠重复分隔符、消解 . 与 ..、去尾斜杠。
 * 不做符号链接解析（Swift 用 standardizedFilePath，同样不解析软链）。
 */
export function normalizePath(path, { style = 'posix' } = {}) {
  if (typeof path !== 'string' || path.length === 0) return '';
  if (style === 'win32') {
    const drive = /^[a-zA-Z]:/.exec(path);
    const prefix = drive ? drive[0] : '';
    const rest = prefix ? path.slice(prefix.length) : path;
    const parts = rest.replaceAll('/', '\\').split('\\');
    const out = [];
    for (const part of parts) {
      if (part === '' || part === '.') continue;
      if (part === '..') {
        if (out.length > 0) out.pop();
        continue;
      }
      out.push(part);
    }
    return `${prefix}\\${out.join('\\')}`;
  }
  const absolute = path.startsWith('/');
  const out = [];
  for (const part of path.split('/')) {
    if (part === '' || part === '.') continue;
    if (part === '..') {
      if (out.length > 0) out.pop();
      continue;
    }
    out.push(part);
  }
  const joined = out.join('/');
  return absolute ? `/${joined}` : joined;
}

/** 文档目录 + 相对引用 → 绝对路径（已规范化）。绝对引用原样返回。 */
export function joinBase(baseDir, ref, { style = 'posix' } = {}) {
  if (typeof ref !== 'string' || ref.length === 0) return '';
  if (style === 'win32') {
    if (/^[a-zA-Z]:/.test(ref) || ref.startsWith('\\\\')) return normalizePath(ref, { style });
    return normalizePath(`${baseDir}\\${ref}`, { style });
  }
  if (ref.startsWith('/')) return normalizePath(ref, { style });
  return normalizePath(`${baseDir}/${ref}`, { style });
}
